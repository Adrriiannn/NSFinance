namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionAISettingsOptions
{
    public const string SectionName = "CompanionAI:Settings";

    public bool Enabled { get; set; } = true;
    public int MaxTokensPerResponse { get; set; } = 900;
    public int MaxTurnsPerSession { get; set; } = 24;
    public int DailySoftCapPerUser { get; set; } = 40;
    public bool EnforceDailySoftCap { get; set; }
    public AIModelClass PreferredModelClass { get; set; } = AIModelClass.HeavyReasoning;
    public AIModelClass SoftCapFallbackModelClass { get; set; } = AIModelClass.Fast;
}
