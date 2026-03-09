namespace NSFinTech.Api.Modules.Banking.DTOs;

public sealed record SyncConnectionResponse(
    Guid ConnectionId,
    int AccountsSynced,
    int BalancesSynced,
    int TransactionsImported,
    string Status,
    DateTime SyncedAtUtc);
