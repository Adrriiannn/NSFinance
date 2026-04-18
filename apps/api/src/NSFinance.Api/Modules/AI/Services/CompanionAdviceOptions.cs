namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionAdviceOptions
{
    public const string SectionName = "CompanionAI:AdviceDecision";

    public bool EnableAiAdjudication { get; set; } = true;
    public int MaxAdjudicatedFindings { get; set; } = 3;
    public int MaxAdjudicationInputChars { get; set; } = 8_000;
    public int MaxAdjudicationOutputTokens { get; set; } = 500;
    public double BorderlineConfidenceThreshold { get; set; } = 0.62d;
    public double HighConfidenceSkipThreshold { get; set; } = 0.88d;
    public decimal CategoryPressureIncreaseRatioThreshold { get; set; } = 1.25m;
    public decimal CategoryPressureAbsoluteDeltaThreshold { get; set; } = 75m;
    public decimal RecurringPressureIncreaseRatioThreshold { get; set; } = 1.15m;
    public decimal RecurringPressureAbsoluteDeltaThreshold { get; set; } = 50m;
    public decimal RecurringToIncomePressureRatioThreshold { get; set; } = 0.55m;
    public decimal BudgetLowRemainingRatioThreshold { get; set; } = 0.15m;
    public decimal BudgetSlippageRatioThreshold { get; set; } = 0.10m;
    public decimal AffordabilityBufferRatioThreshold { get; set; } = 0.10m;
    public decimal MaterialSpendThreshold { get; set; } = 120m;
    public int BaseFreshnessHoursHighSeverity { get; set; } = 12;
    public int BaseFreshnessHoursModerateSeverity { get; set; } = 24;
    public int BaseFreshnessHoursLowSeverity { get; set; } = 36;
    public int BaseFreshnessHoursInfoSeverity { get; set; } = 48;
}
