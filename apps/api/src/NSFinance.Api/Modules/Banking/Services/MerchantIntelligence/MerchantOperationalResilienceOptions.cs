namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantOperationalResilienceOptions
{
    public const string SectionName = "MerchantIntelligence:OperationalResilience";

    public int UnresolvedBaseCooldownMinutes { get; set; } = 30;
    public int UnresolvedMaxCooldownMinutes { get; set; } = 1_440;
    public int RejectedCooldownMinutes { get; set; } = 240;
    public int HighOccurrenceAccelerationThreshold { get; set; } = 10;
    public int HighOccurrenceAccelerationMinutes { get; set; } = 30;
    public int ActiveMerchantValidationDays { get; set; } = 120;
    public int LowConfidenceMerchantValidationDays { get; set; } = 30;
    public int CautiousMerchantValidationDays { get; set; } = 21;
    public int RevalidationMinimumIntervalMinutes { get; set; } = 60;
    public int RevalidationAliasConflictLookbackDays { get; set; } = 14;
    public int RevalidationUnresolvedPressureThreshold { get; set; } = 8;
}
