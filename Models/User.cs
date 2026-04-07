namespace TunSociety.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Gender { get; set; } = "Male";
    public int? Age { get; set; }
    public string AvatarUrl { get; set; } = "/b.png";
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public bool IsFrozen { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<PostComment> PostComments { get; set; } = new List<PostComment>();
    public ICollection<PostReaction> PostReactions { get; set; } = new List<PostReaction>();
    public ICollection<FriendRequest> SentFriendRequests { get; set; } = new List<FriendRequest>();
    public ICollection<FriendRequest> ReceivedFriendRequests { get; set; } = new List<FriendRequest>();
    public ICollection<CommunityNotification> Notifications { get; set; } = new List<CommunityNotification>();
    public ICollection<DirectMessage> SentDirectMessages { get; set; } = new List<DirectMessage>();
    public ICollection<DirectMessage> ReceivedDirectMessages { get; set; } = new List<DirectMessage>();
}
