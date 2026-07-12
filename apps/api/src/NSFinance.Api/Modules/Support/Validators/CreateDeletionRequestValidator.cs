using NSFinance.Api.Modules.Support.DTOs;

namespace NSFinance.Api.Modules.Support.Validators;

public static class CreateDeletionRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateDeletionRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.ChallengeId == Guid.Empty)
        {
            errors["challengeId"] = ["Deletion verification challenge is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Code)
            || request.Code.Trim().Length != 6
            || !request.Code.All(char.IsDigit))
        {
            errors["code"] = ["Enter the six-digit verification code."];
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > 4000)
        {
            errors["notes"] = ["Notes must not exceed 4000 characters."];
        }

        return errors;
    }
}
