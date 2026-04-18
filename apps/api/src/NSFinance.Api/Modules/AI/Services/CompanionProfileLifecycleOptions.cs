namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionProfileLifecycleOptions
{
    public const string SectionName = "CompanionAI:ProfileLifecycle";

    public int StaleAfterHours { get; set; } = 24;
    public int RefreshNeededAfterHours { get; set; } = 72;
    public bool RefreshWhenStale { get; set; }
    public int MaxActivePlans { get; set; } = 5;
    public int MaxRecurringObligations { get; set; } = 12;
    public int SpendingAnalysisLookbackDays { get; set; } = 60;
    public decimal StrongRecurringMonthlyTotalThreshold { get; set; } = 150m;
    public decimal StrongLargestExpenseThreshold { get; set; } = 75m;
    public decimal StrongAverageDailySpendThreshold { get; set; } = 8m;
    public int StrongSpendingDomainCountThreshold { get; set; } = 2;
    public decimal StrongMonthToDateSpendThreshold { get; set; } = 150m;
    public int ProfileSchemaVersion { get; set; } = 1;
}
