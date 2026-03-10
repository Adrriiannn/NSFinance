using NSFinance.Api.Modules.Support.DTOs;

namespace NSFinance.Api.Modules.Support.Validators;

public static class CreateDeletionRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateDeletionRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.VerificationCode) || request.VerificationCode.Trim().Length < 6)
        {
            errors["verificationCode"] = ["Verification code is required."];
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > 4000)
        {
            errors["notes"] = ["Notes must not exceed 4000 characters."];
        }

        return errors;
    }
}