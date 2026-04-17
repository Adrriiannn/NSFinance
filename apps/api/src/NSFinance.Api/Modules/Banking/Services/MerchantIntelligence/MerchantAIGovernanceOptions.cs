namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantAIGovernanceOptions
{
    public const string SectionName = "MerchantIntelligence:AIGovernance";

    public bool Enabled { get; set; }
    public bool AllowD1AIByDefault { get; set; }
    public bool SuggestionOnlyForD3 { get; set; } = true;
    public int MaxAICallsPerSyncRun { get; set; } = 8;
    public int MaxAICallsPerConnectionPerRun { get; set; } = 5;
    public int MaxAICallsPerUserPer24h { get; set; } = 10;
    public int QueueTopMerchantsPerRun { get; set; } = 10;
    public int QueueTopMerchantsPerConnectionPerRun { get; set; } = 6;
    public int InvestigationLockTimeoutMinutes { get; set; } = 20;
    public int MerchantInvestigationCooldownDays { get; set; } = 7;
    public int FailureCooldownHours { get; set; } = 24;
    public int LowConfidenceCooldownHours { get; set; } = 72;
    public int MinimumOccurrencesForExpectedValue { get; set; } = 2;
    public decimal MeaningfulSpendThreshold { get; set; } = 75m;
    public double ExpectedValueThreshold { get; set; } = 0.85d;
    public double ExpectedValueCountWeight { get; set; } = 0.38d;
    public double ExpectedValueSpendWeight { get; set; } = 0.26d;
    public double ExpectedValueRecencyWeight { get; set; } = 0.16d;
    public double ExpectedValueReusabilityWeight { get; set; } = 0.20d;
    public double ExpectedValueLowConfidencePenaltyWeight { get; set; } = 0.20d;
    public double QueuePriorityCountWeight { get; set; } = 0.33d;
    public double QueuePrioritySpendWeight { get; set; } = 0.22d;
    public double QueuePriorityRecencyWeight { get; set; } = 0.18d;
    public double QueuePriorityImpactWeight { get; set; } = 0.17d;
    public double QueuePriorityDomainWeight { get; set; } = 0.10d;
    public decimal SpendNormalizerAmount { get; set; } = 250m;
    public int ExpectedValueRecencyHorizonDays { get; set; } = 21;
}
