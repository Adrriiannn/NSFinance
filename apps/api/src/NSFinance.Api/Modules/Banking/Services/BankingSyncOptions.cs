namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankingSyncOptions
{
    public const string SectionName = "Banking:Sync";

    public int ManualCooldownMinutes { get; set; } = 60;
    public int AutoSyncIntervalMinutes { get; set; } = 60;
    public int StaleSyncPendingRecoveryMinutes { get; set; } = 10;
    public int ProviderRateLimitBackoffMinutes { get; set; } = 30;
}
