using NSFinance.Api.Modules.ExpenseTracker.DTOs;

namespace NSFinance.Api.Modules.ExpenseTracker.Validators;

public static class ExpenseTrackerEntryRequestValidator
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "planned",
        "completed"
    };

    public static Dictionary<string, string[]> Validate(CreateExpenseTrackerEntryRequest request)
    {
        return ValidateCore(
            request.Title,
            request.Amount,
            request.Currency,
            request.Category,
            request.PaymentSource,
            request.OccurredAtUtc,
            request.Notes,
            request.Tags,
            request.Status,
            request.Merchant);
    }

    public static Dictionary<string, string[]> Validate(UpdateExpenseTrackerEntryRequest request)
    {
        return ValidateCore(
            request.Title,
            request.Amount,
            request.Currency,
            request.Category,
            request.PaymentSource,
            request.OccurredAtUtc,
            request.Notes,
            request.Tags,
            request.Status,
            request.Merchant);
    }

    private static Dictionary<string, string[]> ValidateCore(
        string title,
        decimal amount,
        string currency,
        string category,
        string paymentSource,
        DateTime? occurredAtUtc,
        string? notes,
        IReadOnlyList<string>? tags,
        string status,
        string? merchant)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors[nameof(title)] = ["Title is required."];
        }
        else if (title.Trim().Length > 120)
        {
            errors[nameof(title)] = ["Title must be 120 characters or fewer."];
        }

        if (amount <= 0)
        {
            errors[nameof(amount)] = ["Amount must be greater than zero."];
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            errors[nameof(currency)] = ["Currency must be a 3-letter code."];
        }

        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 80)
        {
            errors[nameof(category)] = ["Category is required and must be 80 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(paymentSource) || paymentSource.Trim().Length > 80)
        {
            errors[nameof(paymentSource)] = ["Payment source is required and must be 80 characters or fewer."];
        }

        if (occurredAtUtc.HasValue && occurredAtUtc.Value == default)
        {
            errors[nameof(occurredAtUtc)] = ["Occurred date is invalid."];
        }

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 1200)
        {
            errors[nameof(notes)] = ["Notes must be 1200 characters or fewer."];
        }

        if (!string.IsNullOrWhiteSpace(merchant) && merchant.Trim().Length > 120)
        {
            errors[nameof(merchant)] = ["Merchant must be 120 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(status) || !AllowedStatuses.Contains(status.Trim()))
        {
            errors[nameof(status)] = ["Status must be either planned or completed."];
        }

        if (tags is not null && tags.Any(tag => tag.Trim().Length > 32))
        {
            errors[nameof(tags)] = ["Tags must be 32 characters or fewer."];
        }

        return errors;
    }
}
