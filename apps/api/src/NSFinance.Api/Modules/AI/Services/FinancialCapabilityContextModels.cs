namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionFinancialSummaryContext(
    decimal IncomeLast30Days,
    decimal SpendLast30Days,
    decimal NetLast30Days,
    string Currency);

public sealed record CompanionSpendingAnalysisContext(
    IReadOnlyList<CompanionDomainSpendContextItem> TopDomainSpend,
    int DomainCount,
    decimal AverageDailySpend,
    decimal LargestExpense);

public sealed record CompanionDomainSpendContextItem(
    int DomainCode,
    decimal Amount);

public sealed record CompanionRecurringObligationsContext(
    int TotalItemCount,
    decimal EstimatedMonthlyTotal,
    IReadOnlyList<CompanionRecurringItemContext> TopItems);

public sealed record CompanionRecurringItemContext(
    string? Name,
    decimal Amount,
    string Currency,
    int FrequencyDays);

public sealed record CompanionBudgetStatusContext(
    bool HasBudgetPlan,
    decimal? MonthlyBudget,
    decimal MonthToDateSpend,
    decimal? RemainingBudget);
