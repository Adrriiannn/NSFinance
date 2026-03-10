using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ChangePasswordRequestValidator
{
    public static Dictionary<string, string[]> Validate(ChangePasswordRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            errors["currentPassword"] = ["Current password is required."];
        }

        var passwordErrors = PasswordPolicyValidator.Validate(request.NewPassword);
        if (passwordErrors.Length > 0)
        {
            errors["newPassword"] = passwordErrors;
        }

        return errors;
    }
}
