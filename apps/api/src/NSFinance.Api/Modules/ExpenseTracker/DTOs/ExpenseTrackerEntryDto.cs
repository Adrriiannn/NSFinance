namespace NSFinance.Api.Modules.ExpenseTracker.DTOs;

public sealed record ExpenseTrackerEntryDto(
    Guid Id,
    string Title,
    decimal Amount,
    string Currency,
    int? DomainId,
    string? DomainName,
    int? CategoryId,
    string? CategoryName,
    int? SubcategoryId,
    string? SubcategoryName,
    string? LegacyCategoryLabel,
    string PaymentSource,
    DateTime OccurredAtUtc,
    string? Notes,
    IReadOnlyList<string> Tags,
    string Status,
    bool IsRecurring,
    string? Merchant,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
