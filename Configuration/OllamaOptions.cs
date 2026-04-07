namespace TunSociety.Api.Configuration;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ModerationModel { get; set; } = "gemma3:1b";
    public int TimeoutSeconds { get; set; } = 60;
}
