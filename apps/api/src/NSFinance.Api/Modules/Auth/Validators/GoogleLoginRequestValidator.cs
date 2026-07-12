using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class GoogleLoginRequestValidator
{
    public static Dictionary<string, string[]> Validate(GoogleLoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            errors["idToken"] = ["Google ID token is required."];
        }
        else if (request.IdToken.Length > 8192)
        {
            errors["idToken"] = ["Google ID token is invalid."];
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
