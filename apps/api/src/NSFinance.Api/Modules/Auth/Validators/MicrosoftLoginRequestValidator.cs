using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class MicrosoftLoginRequestValidator
{
    public static Dictionary<string, string[]> Validate(MicrosoftLoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            errors["accessToken"] = ["Microsoft access token is required."];
        }
        else if (request.AccessToken.Length > 16384)
        {
            errors["accessToken"] = ["Microsoft access token is invalid."];
        }

        if (request.AcceptPolicies && string.IsNullOrWhiteSpace(request.TermsVersion))
        {
            errors["termsVersion"] = ["The accepted Terms version is required."];
        }

        if (request.AcceptPolicies && string.IsNullOrWhiteSpace(request.PrivacyVersion))
        {
            errors["privacyVersion"] = ["The accepted Privacy Policy version is required."];
        }

        if (request.TermsVersion?.Length > 40 || request.PrivacyVersion?.Length > 40)
        {
            errors["policyVersion"] = ["The accepted policy version is invalid."];
        }

        return errors;
    }
}
