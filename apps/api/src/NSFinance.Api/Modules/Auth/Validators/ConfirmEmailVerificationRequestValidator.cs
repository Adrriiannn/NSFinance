using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ConfirmEmailVerificationRequestValidator
{
    public static Dictionary<string, string[]> Validate(ConfirmEmailVerificationRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            errors["token"] = ["Token is required."];
        }

        return errors;
    }
}
