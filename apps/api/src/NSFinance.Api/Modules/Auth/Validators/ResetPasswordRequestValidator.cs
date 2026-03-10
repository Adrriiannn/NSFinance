using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ResetPasswordRequestValidator
{
    public static Dictionary<string, string[]> Validate(ResetPasswordRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            errors["token"] = ["Token is required."];
        }

        var passwordErrors = PasswordPolicyValidator.Validate(request.NewPassword);
        if (passwordErrors.Length > 0)
        {
            errors["newPassword"] = passwordErrors;
        }

        return errors;
    }
}
