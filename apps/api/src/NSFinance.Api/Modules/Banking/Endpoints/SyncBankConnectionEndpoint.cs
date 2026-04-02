using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class SyncBankConnectionEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid connectionId,
        ICurrentUserProvider currentUserProvider,
        IAuditService auditService,
        BankSyncService bankSyncService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "manual_sync_triggered",
            targetEntityType: "open_banking_connection",
            targetEntityId: connectionId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            CancellationToken.None);

        var result = await bankSyncService.SyncConnectionAsync(userId, connectionId, CancellationToken.None);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        var response = new SyncConnectionResponse(
            result.Value!.ConnectionId,
            result.Value.AccountsSynced,
            result.Value.BalancesSynced,
            result.Value.TransactionsImported,
            result.Value.Status,
            result.Value.SyncedAtUtc,
            result.Value.DataChanged,
            result.Value.HistoricalEnrichmentInProgress,
            result.Value.HistoricalEnrichmentCompleted,
            result.Value.HistoricalEnrichmentProgressPercent,
            result.Value.HistoricalEnrichmentCheckpointUtc);

        return Results.Ok(response);
    }
}
