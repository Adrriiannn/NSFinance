using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class VerifyPasswordChangeCodeRequestValidator
{
    public static Dictionary<string, string[]> Validate(VerifyPasswordChangeCodeRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length < 6)
        {
            errors["code"] = ["Verification code is required."];
        }

        return errors;
    }
}