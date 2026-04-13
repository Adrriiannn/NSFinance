namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankConnectionAttemptOptions
{
    public const string SectionName = "Banking:ConnectionAttempts";

    public int SweepIntervalSeconds { get; set; } = 60;
    public int ExpiryBatchSize { get; set; } = 64;
    public int StaleProcessingExpiryMinutes { get; set; } = 120;
}
