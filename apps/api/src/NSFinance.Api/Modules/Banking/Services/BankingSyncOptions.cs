namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankingSyncOptions
{
    public const string SectionName = "Banking:Sync";

    public int ManualCooldownMinutes { get; set; } = 60;
    public int AutoSyncIntervalMinutes { get; set; } = 60;
    public int StaleSyncPendingRecoveryMinutes { get; set; } = 10;
    public int ProviderRateLimitBackoffMinutes { get; set; } = 30;
    public int DurableJobMaxAttempts { get; set; } = 5;
    public int DurableJobLeaseSeconds { get; set; } = 120;
    public int DurableJobPollMilliseconds { get; set; } = 500;
    public int SyncExecutionLeaseSeconds { get; set; } = 120;
    public bool UnattendedSyncEnabled { get; set; } = true;
    public int UnattendedSyncIntervalMinutes { get; set; } = 720;
    public int UnattendedSyncSweepMinutes { get; set; } = 15;
}
