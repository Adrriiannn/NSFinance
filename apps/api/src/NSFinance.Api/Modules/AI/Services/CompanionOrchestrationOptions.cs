namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionOrchestrationOptions
{
    public const string SectionName = "CompanionAI:Orchestration";

    public int MaxToolCallsPerRequest { get; set; } = 6;
    public int MaxContextKeys { get; set; } = 7;
    public int MaxSerializedContextChars { get; set; } = 10_000;
    public int MaxSpendDomains { get; set; } = 6;
    public int MaxRecurringItems { get; set; } = 5;
    public int MaxTransactionRows { get; set; } = 8;
    public int MaxPlaceItems { get; set; } = 8;
    public int MaxSummaryTextLength { get; set; } = 200;
    public int MaxSecondaryOptionalTools { get; set; } = 3;
}
