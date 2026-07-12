using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ConfirmPasswordChangeCodeRequestValidator
{
    public static Dictionary<string, string[]> Validate(ConfirmPasswordChangeCodeRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.ChallengeId == Guid.Empty)
        {
            errors["challengeId"] = ["Verification challenge is required."];
        }

        if (string.IsNullOrWhiteSpace(request.GrantToken))
        {
            errors["grantToken"] = ["Verified password-change authorization is required."];
        }

        var passwordErrors = PasswordPolicyValidator.Validate(request.NewPassword);
        if (passwordErrors.Length > 0)
        {
            errors["newPassword"] = passwordErrors;
        }

        return errors;
    }
}
