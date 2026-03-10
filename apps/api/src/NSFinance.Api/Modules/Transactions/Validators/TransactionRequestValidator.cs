using NSFinance.Api.Modules.Transactions.DTOs;

namespace NSFinance.Api.Modules.Transactions.Validators;

public static class TransactionRequestValidator
{
    private static readonly HashSet<string> AllowedDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Income",
        "Expense"
    };

    public static Dictionary<string, string[]> Validate(CreateTransactionRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.AccountId == Guid.Empty)
        {
            errors["accountId"] = ["Account is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors["description"] = ["Description is required."];
        }

        if (request.Amount == 0)
        {
            errors["amount"] = ["Amount must be non-zero."];
        }

        if (request.Amount < 0)
        {
            errors["amount"] = ["Amount must be positive; direction controls sign."];
        }

        if (string.IsNullOrWhiteSpace(request.Direction) || !AllowedDirections.Contains(request.Direction))
        {
            errors["direction"] = ["Direction must be Income or Expense."];
        }

        if (!string.IsNullOrWhiteSpace(request.Currency) && request.Currency.Trim().Length != 3)
        {
            errors["currency"] = ["Currency must be a 3-letter ISO code when provided."];
        }

        return errors;
    }
}
