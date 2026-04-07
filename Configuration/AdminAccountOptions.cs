namespace TunSociety.Api.Configuration;

public class AdminAccountOptions
{
    public const string SectionName = "AdminAccount";

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Administrator";
}
