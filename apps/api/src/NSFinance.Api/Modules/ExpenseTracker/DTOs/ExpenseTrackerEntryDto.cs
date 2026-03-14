namespace NSFinance.Api.Modules.ExpenseTracker.DTOs;

public sealed record ExpenseTrackerEntryDto(
    Guid Id,
    string Title,
    decimal Amount,
    string Currency,
    string Category,
    string PaymentSource,
    DateTime OccurredAtUtc,
    string? Notes,
    IReadOnlyList<string> Tags,
    string Status,
    bool IsRecurring,
    string? Merchant,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
