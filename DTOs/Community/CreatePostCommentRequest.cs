namespace TunSociety.Api.DTOs.Community;

public class CreatePostCommentRequest
{
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
}
