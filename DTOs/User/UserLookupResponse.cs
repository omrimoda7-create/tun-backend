using TunSociety.Api.Infrastructure;

namespace TunSociety.Api.DTOs.User;

public class UserLookupResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    public static UserLookupResponse FromEntity(Models.User user)
    {
        return new UserLookupResponse
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Gender = user.Gender,
            Age = user.Age,
            AvatarUrl = UserAvatarHelper.Resolve(user.AvatarUrl, user.Gender),
            Role = user.Role
        };
    }
}
