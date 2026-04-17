namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantAIGovernanceOptions
{
    public const string SectionName = "MerchantIntelligence:AIGovernance";

    public bool Enabled { get; set; }
    public bool AllowD1AIByDefault { get; set; }
    public bool SuggestionOnlyForD3 { get; set; } = true;
    public int MaxAICallsPerSyncRun { get; set; } = 3;
    public int MaxAICallsPerConnectionPerRun { get; set; } = 2;
    public int MaxAICallsPerUserPer24h { get; set; } = 10;
    public int MerchantInvestigationCooldownDays { get; set; } = 7;
    public int FailureCooldownHours { get; set; } = 24;
    public int LowConfidenceCooldownHours { get; set; } = 72;
    public int MinimumOccurrencesForExpectedValue { get; set; } = 2;
    public decimal MeaningfulSpendThreshold { get; set; } = 75m;
}

