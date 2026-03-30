namespace NSFinance.Api.Modules.Banking.Services.Models;

public sealed record BankGlobalSyncConnectionResult(
    Guid ConnectionId,
    string? ProviderDisplayName,
    string Status,
    string Outcome,
    int AccountsSynced,
    int BalancesSynced,
    int TransactionsImported,
    DateTime? SyncedAtUtc,
    bool DataChanged,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record BankGlobalSyncResult(
    string Trigger,
    string Outcome,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    bool DueNow,
    int CooldownRemainingSeconds,
    DateTime? CooldownUntilUtc,
    int EligibleConnectionCount,
    int ChangedConnectionCount,
    int NoChangeConnectionCount,
    int FailedConnectionCount,
    int SkippedConnectionCount,
    DateTime? LastSuccessfulSyncUtc,
    IReadOnlyList<BankGlobalSyncConnectionResult> Connections);
