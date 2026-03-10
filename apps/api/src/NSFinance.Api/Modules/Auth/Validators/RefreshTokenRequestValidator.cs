using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class RefreshTokenRequestValidator
{
    public static Dictionary<string, string[]> Validate(RefreshTokenRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            errors["refreshToken"] = ["Refresh token is required."];
        }

        return errors;
    }
}
