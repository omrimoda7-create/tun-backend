using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TunSociety.Api.Configuration;

namespace TunSociety.Api.Services;

public class LocalAiService
{
    private static readonly string[] AllowedCategories =
    [
        "abuse",
        "hate",
        "political",
        "pornography",
        "racism",
        "scam",
        "spam",
        "threat",
        "violence"
    ];

    private static readonly HashSet<string> AllowedCategorySet = new(AllowedCategories, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, AiFlagRule> FallbackRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["violence"] = new(0.98, ["violence", "kill", "murder", "attack", "beat you"]),
        ["threat"] = new(0.99, ["threat", "threaten", "hurt you", "i will hurt you", "i will kill you"]),
        ["hate"] = new(0.92, ["hate you", "i hate you", "hateful", "go die"]),
        ["racism"] = new(0.97, ["racist", "racism", "racial slur", "white power", "black people are", "arab people are"]),
        ["pornography"] = new(0.97, ["porn", "pornographic", "explicit sex", "nude pics", "sexual video"]),
        ["political"] = new(0.9, ["politics", "political", "election", "vote for", "government", "president"]),
        ["scam"] = new(0.95, ["scam", "fraud", "send money", "wire money", "crypto giveaway", "investment guaranteed"]),
        ["abuse"] = new(0.78, [
            "abuse",
            "stupid idiot",
            "piece of trash",
            "worthless",
            "fuck you",
            "f*ck you",
            "asshole",
            "assholes",
            "bastard",
            "moron",
            "idiot",
            "dumb",
            "shut up"
        ]),
        ["spam"] = new(0.62, ["spam", "buy now", "click here", "limited offer", "subscribe now"])
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalAiService> _logger;
    private readonly OllamaOptions _options;

    public LocalAiService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<LocalAiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<LocalAiModerationResult> ModerateAsync(
        string content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return LocalAiModerationResult.Allow();
        }

        var fallback = HeuristicModeration(content);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/generate",
                new OllamaGenerateRequest
                {
                    Model = _options.ModerationModel,
                    Prompt = BuildPrompt(content, contentType),
                    Stream = false,
                    Format = "json",
                    Options = new OllamaGenerateOptions
                    {
                        Temperature = 0,
                        NumPredict = 120
                    }
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Ollama moderation request failed with status {StatusCode}. Falling back to heuristic moderation. Response: {ResponseBody}",
                    response.StatusCode,
                    body);
                return fallback;
            }

            var envelope = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken);
            if (string.IsNullOrWhiteSpace(envelope?.Response))
            {
                _logger.LogWarning("Ollama moderation response did not contain a response payload. Falling back to heuristic moderation.");
                return fallback;
            }

            var parsed = ParseModerationPayload(envelope.Response);
            return parsed ?? fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama moderation request failed. Falling back to heuristic moderation.");
            return fallback;
        }
    }

    private static string BuildPrompt(string content, string contentType)
    {
        var sanitizedType = string.IsNullOrWhiteSpace(contentType) ? "GENERIC" : contentType.Trim();
        return $$"""
Moderate TunSociety content.
Return JSON only with keys: decision, categories, confidence, reason.
decision must be ALLOW, FLAG, or BLOCK.
BLOCK only for clear hate, racism, threats, pornography, scams, or violence.
FLAG for abuse, spam, political content, or uncertain borderline cases.
ALLOW for safe content, including greetings, short replies, typos, random harmless words, and casual conversation.
Do not block or flag a message just because it is short, misspelled, informal, or contains a single harmless word.
Allowed categories: abuse, hate, political, pornography, racism, scam, spam, threat, violence.
confidence must be between 0 and 1.
No markdown. No explanation outside JSON.
ContentType={{sanitizedType}}
Content={{content}}
""";
    }

    private static LocalAiModerationResult? ParseModerationPayload(string rawResponse)
    {
        var json = ExtractJson(rawResponse);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<OllamaModerationPayload>(json, JsonOptions);
            if (result is null)
            {
                return null;
            }

            var decision = NormalizeDecision(result.Decision);
            var categories = (result.Categories ?? [])
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category.Trim().ToLowerInvariant())
                .Where(category => AllowedCategorySet.Contains(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var confidence = Math.Clamp(result.Confidence, 0, 1);
            var reason = string.IsNullOrWhiteSpace(result.Reason) ? BuildDefaultReason(decision, categories) : result.Reason.Trim();

            return new LocalAiModerationResult(decision, categories, confidence, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJson(string rawResponse)
    {
        var trimmed = rawResponse.Trim();
        trimmed = Regex.Replace(trimmed, @"^```(?:json)?\s*", string.Empty, RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"\s*```$", string.Empty, RegexOptions.IgnoreCase);

        var start = trimmed.IndexOf('{');
        if (start < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var index = start; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];

            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return trimmed[start..(index + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static LocalAiModerationResult HeuristicModeration(string content)
    {
        var flags = HeuristicFlags(content);
        if (flags.Count == 0)
        {
            return LocalAiModerationResult.Allow();
        }

        if (flags.Count == 1 && string.Equals(flags[0], "political", StringComparison.OrdinalIgnoreCase) && LooksLikeBenignCasualText(content))
        {
            return LocalAiModerationResult.Allow();
        }

        var score = flags
            .Where(flag => FallbackRules.ContainsKey(flag))
            .Select(flag => FallbackRules[flag].Weight)
            .DefaultIfEmpty(0)
            .Max();

        if (flags.Any(flag => flag is "violence" or "threat" or "hate" or "racism" or "pornography" or "scam"))
        {
            return new LocalAiModerationResult("BLOCK", flags, score, BuildDefaultReason("BLOCK", flags));
        }

        return new LocalAiModerationResult("FLAG", flags, Math.Max(score, 0.55), BuildDefaultReason("FLAG", flags));
    }

    private static List<string> HeuristicFlags(string content)
    {
        var lowered = content.ToLowerInvariant();

        return FallbackRules
            .Where(entry => entry.Value.Patterns.Any(pattern => ContainsPattern(lowered, pattern)))
            .Select(entry => entry.Key)
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ContainsPattern(string loweredContent, string pattern)
    {
        var escaped = Regex.Escape(pattern.ToLowerInvariant()).Replace("\\ ", "\\s+");
        return Regex.IsMatch(loweredContent, $@"\b{escaped}\b", RegexOptions.CultureInvariant);
    }

    private static string NormalizeDecision(string? decision)
    {
        return decision?.Trim().ToUpperInvariant() switch
        {
            "BLOCK" => "BLOCK",
            "FLAG" => "FLAG",
            _ => "ALLOW"
        };
    }

    private static string BuildDefaultReason(string decision, IReadOnlyCollection<string> categories)
    {
        var primaryCategory = categories.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(primaryCategory))
        {
            return decision switch
            {
                "BLOCK" => "Content violates TunSociety moderation rules.",
                "FLAG" => "Content needs moderation review.",
                _ => "Content appears safe."
            };
        }

        return decision switch
        {
            "BLOCK" => $"Blocked due to moderation category: {primaryCategory}.",
            "FLAG" => $"Flagged for moderation category: {primaryCategory}.",
            _ => $"Allowed after checking category context: {primaryCategory}."
        };
    }

    private static bool LooksLikeBenignCasualText(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.Length is < 2 or > 80)
        {
            return false;
        }

        if (trimmed.Contains("http", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"\b(buy\s+now|click\s+here|limited\s+offer|subscribe\s+now|send\s+money|wire\s+money|free\s+money|earn\s+money|make\s+money\s+fast|crypto\s+giveaway|investment\s+guaranteed|dm\s+me|whatsapp\s+me|telegram)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var words = Regex.Matches(trimmed, @"[\p{L}\p{M}][\p{L}\p{M}'\-]*").Count;
        return words is > 0 and <= 6;
    }

    private sealed record AiFlagRule(double Weight, string[] Patterns);

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("options")]
        public OllamaGenerateOptions? Options { get; set; }
    }

    private sealed class OllamaGenerateOptions
    {
        [JsonPropertyName("temperature")]
        public int Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    private sealed class OllamaModerationPayload
    {
        [JsonPropertyName("decision")]
        public string Decision { get; set; } = "ALLOW";

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = [];

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}

public sealed class LocalAiModerationResult
{
    public string Decision { get; init; } = "ALLOW";
    public List<string> Categories { get; init; } = [];
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;

    public LocalAiModerationResult()
    {
    }

    public LocalAiModerationResult(string decision, List<string> categories, double confidence, string reason)
    {
        Decision = decision;
        Categories = categories;
        Confidence = confidence;
        Reason = reason;
    }

    public static LocalAiModerationResult Allow() => new()
    {
        Decision = "ALLOW",
        Categories = [],
        Confidence = 0,
        Reason = "Content appears safe."
    };
}
