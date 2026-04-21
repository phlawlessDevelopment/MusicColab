using System.Security.Claims;

namespace MusicColab.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static Guid RequireUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated user id claim is missing.");
    }
}
