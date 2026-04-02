namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record SyncConnectionResponse(
    Guid ConnectionId,
    int AccountsSynced,
    int BalancesSynced,
    int TransactionsImported,
    string Status,
    DateTime SyncedAtUtc,
    bool DataChanged,
    bool HistoricalEnrichmentInProgress,
    bool HistoricalEnrichmentCompleted,
    double? HistoricalEnrichmentProgressPercent,
    DateTime? HistoricalEnrichmentCheckpointUtc);
