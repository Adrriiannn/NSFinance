using Google.Apis.Auth;

namespace NSFinance.Api.Modules.Auth.Services;

public interface IGoogleIdTokenVerifier
{
    Task<GoogleJsonWebSignature.Payload> ValidateAsync(
        string idToken,
        IReadOnlyCollection<string> audiences,
        CancellationToken cancellationToken);
}
