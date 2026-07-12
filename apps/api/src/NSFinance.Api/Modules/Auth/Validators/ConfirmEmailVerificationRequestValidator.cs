using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class ConfirmEmailVerificationRequestValidator
{
    public static Dictionary<string, string[]> Validate(ConfirmEmailVerificationRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.ChallengeId == Guid.Empty)
        {
            errors["challengeId"] = ["Verification challenge is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Code)
            || request.Code.Length != 6
            || !request.Code.All(char.IsDigit))
        {
            errors["code"] = ["Enter the six-digit code."];
        }

        return errors;
    }
}
