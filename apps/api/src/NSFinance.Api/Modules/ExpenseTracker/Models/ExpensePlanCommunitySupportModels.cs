namespace NSFinance.Api.Modules.ExpenseTracker.Models;

public sealed record ExpensePlanPublicationLineItemSnapshot(
    int TaxonomyDomainId,
    string DomainName,
    int TaxonomyCategoryId,
    string CategoryName,
    int TaxonomySubcategoryId,
    string SubcategoryName,
    string DisplayNameSnapshot,
    string HierarchyPathSnapshot,
    decimal ExpectedAmount,
    string? Notes,
    int SortOrder);

public sealed record ExpensePlanPublicationSnapshot(
    Guid SourcePlanId,
    int SourcePlanVersion,
    string PlanType,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    string CurrencyCode,
    decimal ExpectedIncomeTotal,
    decimal ExpectedSpendTotal,
    decimal ExpectedRemainingTotal,
    bool IsTemplate,
    bool IsRecurring,
    string? RecurrenceRuleJson,
    IReadOnlyList<ExpensePlanPublicationLineItemSnapshot> LineItems);

public sealed record ExpensePlanModerationScanResult(
    string ModerationStatus,
    bool ShouldBlock,
    bool ShouldQueueReview,
    string Summary,
    IReadOnlyList<string> MatchedRules);
