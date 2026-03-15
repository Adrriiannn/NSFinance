namespace NSFinance.Api.Modules.ExpenseTracker.DTOs;

public sealed record ExpensePlanLineItemRequest(
    int TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    decimal ExpectedAmount,
    string? Notes,
    int SortOrder);

public sealed record ExpensePlanRecurrenceDto(
    string RecurrenceType,
    int Interval,
    DateTime? NextGenerationAtUtc,
    DateTime? RecurrenceStartAtUtc);

public sealed record CreateExpensePlanRequest(
    string Title,
    string? Description,
    string? Notes,
    string Status,
    string PlanType,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    string CurrencyCode,
    decimal ExpectedIncomeTotal,
    IReadOnlyList<ExpensePlanLineItemRequest> LineItems,
    IReadOnlyList<string>? Tags,
    bool IsTemplate,
    bool IsRecurring,
    ExpensePlanRecurrenceDto? Recurrence,
    bool IsShared,
    string? SharingMode,
    string? StatusReason,
    string? PlanOriginType);

public sealed record UpdateExpensePlanRequest(
    string Title,
    string? Description,
    string? Notes,
    string PlanType,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    string CurrencyCode,
    decimal ExpectedIncomeTotal,
    IReadOnlyList<ExpensePlanLineItemRequest> LineItems,
    IReadOnlyList<string>? Tags,
    bool IsTemplate,
    bool IsRecurring,
    ExpensePlanRecurrenceDto? Recurrence,
    bool IsShared,
    string? SharingMode,
    string? StatusReason);

public sealed record TransitionExpensePlanRequest(
    string TargetStatus,
    string? StatusReason);
