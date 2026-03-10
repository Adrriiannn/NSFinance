using NSFinance.Api.Modules.Policies.DTOs;

namespace NSFinance.Api.Modules.Policies.Validators;

public static class AcceptPolicyRequestValidator
{
    public static Dictionary<string, string[]> Validate(AcceptPolicyRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.PolicyType))
        {
            errors["policyType"] = ["Policy type is required."];
        }

        if (string.IsNullOrWhiteSpace(request.PolicyVersion))
        {
            errors["policyVersion"] = ["Policy version is required."];
        }

        if (string.IsNullOrWhiteSpace(request.AcceptanceContext))
        {
            errors["acceptanceContext"] = ["Acceptance context is required."];
        }

        return errors;
    }
}
