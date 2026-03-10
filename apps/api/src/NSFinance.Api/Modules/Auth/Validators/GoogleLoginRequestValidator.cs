using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class GoogleLoginRequestValidator
{
    public static Dictionary<string, string[]> Validate(GoogleLoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            errors["idToken"] = ["Google ID token is required."];
        }
        else if (request.IdToken.Length > 8192)
        {
            errors["idToken"] = ["Google ID token is invalid."];
        }

        return errors;
    }
}
