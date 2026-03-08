using NSFinTech.Api.Modules.Accounts.DTOs;

namespace NSFinTech.Api.Modules.Accounts.Validators;

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

    public static Dictionary<string, string[]> Validate(CreateAccountRequest request)
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

        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Trim().Length != 3)
        {
            errors["currency"] = ["Currency must be a 3-letter ISO code."];
        }

        return errors;
    }
}
