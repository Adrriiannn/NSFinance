using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ConfirmPasswordChangeCodeRequestValidator
{
    public static Dictionary<string, string[]> Validate(ConfirmPasswordChangeCodeRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length < 6)
        {
            errors["code"] = ["Verification code is required."];
        }

        var passwordErrors = PasswordPolicyValidator.Validate(request.NewPassword);
        if (passwordErrors.Length > 0)
        {
            errors["newPassword"] = passwordErrors;
        }

        return errors;
    }
}