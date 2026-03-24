using Google.Apis.Auth;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class GoogleIdTokenVerifier : IGoogleIdTokenVerifier
{
    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(
        string idToken,
        IReadOnlyCollection<string> audiences,
        CancellationToken cancellationToken)
    {
        if (audiences.Count == 0)
        {
            throw new InvalidOperationException("At least one Google client ID must be configured.");
        }

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = audiences
        };

        return GoogleJsonWebSignature.ValidateAsync(idToken, settings);
    }
}
