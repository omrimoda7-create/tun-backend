namespace TunSociety.Api.DTOs.Moderation;

public class FlaggedContentReviewResponse
{
    public Guid ModerationResultId { get; set; }
    public Guid ContentId { get; set; }
    public Guid MessageId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public List<string> Flags { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}
