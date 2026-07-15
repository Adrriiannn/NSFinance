using NSFinance.Api.Modules.Accounts.DTOs;

namespace NSFinance.Api.Modules.Accounts.Validators;

public static class AccountRequestValidator
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Current",
        "Savings",
        "Credit",
        "Cash",
        "Other"
    };

    public static Dictionary<string, string[]> Validate(UpdateAccountRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Account name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Type) || !AllowedTypes.Contains(request.Type))
        {
            errors["type"] = ["Account type must be one of: Current, Savings, Credit, Cash, Other."];
        }

        return errors;
    }
}
