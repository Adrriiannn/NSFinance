namespace NSFinance.Api.Modules.ExpenseTracker.Models;

public sealed record ExpensePlanRecurrenceSettings(
    string RecurrenceType,
    int Interval,
    DateTime? NextGenerationAtUtc,
    DateTime? RecurrenceStartAtUtc);

public sealed record ExpensePlanPeriodProgress(
    string PeriodState,
    decimal PercentElapsed,
    bool ShouldAutoComplete,
    bool IsCurrentlyActive);

public sealed record ExpensePlanComparableEntryFact(
    Guid EntryId,
    DateTime EffectiveOccurredAtUtc,
    decimal EffectiveAmount,
    int? TaxonomyDomainId,
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    string DomainName,
    string CategoryName,
    string SubcategoryName);
