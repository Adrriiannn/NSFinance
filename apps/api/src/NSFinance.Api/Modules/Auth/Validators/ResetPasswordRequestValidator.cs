using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ResetPasswordRequestValidator
{
    public static Dictionary<string, string[]> Validate(ResetPasswordRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.ChallengeId == Guid.Empty)
        {
            errors["challengeId"] = ["Recovery challenge is required."];
        }

        if (string.IsNullOrWhiteSpace(request.RecoveryToken))
        {
            errors["recoveryToken"] = ["Recovery authorization is required."];
        }

        var passwordErrors = PasswordPolicyValidator.Validate(request.NewPassword);
        if (passwordErrors.Length > 0)
        {
            errors["newPassword"] = passwordErrors;
        }

        return errors;
    }
}
