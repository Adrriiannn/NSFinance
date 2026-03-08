using System.Security.Claims;

namespace NSFinTech.Api.Modules.Users.Services;

public sealed class HttpContextCurrentUserProvider(IHttpContextAccessor httpContextAccessor) : ICurrentUserProvider
{
    public Guid UserId
    {
        get
        {
            if (TryGetUserId(out var userId))
            {
                return userId;
            }

            throw new InvalidOperationException("Authenticated user ID is not available in the current request context.");
        }
    }

    public bool TryGetUserId(out Guid userId)
    {
        var subjectClaim = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

        if (Guid.TryParse(subjectClaim, out userId))
        {
            return true;
        }

        userId = Guid.Empty;
        return false;
    }
}
