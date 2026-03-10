using NSFinance.Api.Modules.Policies.DTOs;

namespace NSFinance.Api.Modules.Policies.Validators;

public static class UpdateConsentRequestValidator
{
    private static readonly HashSet<string> AllowedStatuses = ["granted", "revoked", "denied"];

    public static Dictionary<string, string[]> Validate(UpdateConsentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.ConsentType))
        {
            errors["consentType"] = ["Consent type is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Status) || !AllowedStatuses.Contains(request.Status.Trim().ToLowerInvariant()))
        {
            errors["status"] = ["Status must be one of: granted, revoked, denied."];
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            errors["source"] = ["Source is required."];
        }

        return errors;
    }
}
