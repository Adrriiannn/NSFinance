using System.Security.Claims;

namespace NSFinance.Api.Modules.Auth.Services;

public interface IMicrosoftAccessTokenVerifier
{
    Task<ClaimsPrincipal> ValidateAsync(string accessToken, CancellationToken cancellationToken);
}
