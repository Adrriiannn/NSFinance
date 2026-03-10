using Google.Apis.Auth;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class GoogleIdTokenVerifier : IGoogleIdTokenVerifier
{
    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(
        string idToken,
        string audience,
        CancellationToken cancellationToken)
    {
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [audience]
        };

        return GoogleJsonWebSignature.ValidateAsync(idToken, settings);
    }
}
