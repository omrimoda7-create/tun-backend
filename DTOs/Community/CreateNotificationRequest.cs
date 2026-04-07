namespace TunSociety.Api.DTOs.Community;

public class CreateNotificationRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "System";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
