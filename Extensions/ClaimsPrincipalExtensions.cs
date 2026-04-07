using System.Security.Claims;
using TunSociety.Api.Infrastructure;

namespace TunSociety.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var rawValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(rawValue, out var userId)
            ? userId
            : null;
    }

    public static bool IsAdmin(this ClaimsPrincipal principal)
    {
        return principal.IsInRole(RoleNames.Admin);
    }

    public static bool IsModeratorOrAdmin(this ClaimsPrincipal principal)
    {
        return principal.IsInRole(RoleNames.Admin) || principal.IsInRole(RoleNames.Moderator);
    }
}
