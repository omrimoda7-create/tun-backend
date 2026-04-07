namespace TunSociety.Api.DTOs.Community;

public class ConversationResponse
{
    public Guid PartnerUserId { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerRole { get; set; } = string.Empty;
    public Guid? PartnerLastVisibleMessageId { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public bool IsPartnerOnline { get; set; }
    public int UnreadCount { get; set; }
    public List<DirectMessageResponse> Messages { get; set; } = [];
}
