namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record GlobalBankSyncConnectionResponse(
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

public sealed record GlobalBankSyncResponse(
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
    IReadOnlyList<GlobalBankSyncConnectionResponse> Connections);
