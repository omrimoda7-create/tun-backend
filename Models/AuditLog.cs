namespace TunSociety.Api.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Data { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
