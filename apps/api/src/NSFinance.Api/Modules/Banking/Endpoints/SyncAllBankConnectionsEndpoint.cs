using System.Linq;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class SyncAllBankConnectionsEndpoint
{
    public static async Task<IResult> HandleAsync(
        GlobalBankSyncRequest? request,
        ICurrentUserProvider currentUserProvider,
        BankGlobalSyncService globalSyncService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await globalSyncService.ExecuteAsync(
            userId,
            trigger: request?.Trigger,
            source: request?.Source,
            cancellationToken);

        var response = new GlobalBankSyncResponse(
            Trigger: result.Trigger,
            Outcome: result.Outcome,
            RequestedAtUtc: result.RequestedAtUtc,
            CompletedAtUtc: result.CompletedAtUtc,
            DueNow: result.DueNow,
            CooldownRemainingSeconds: result.CooldownRemainingSeconds,
            CooldownUntilUtc: result.CooldownUntilUtc,
            EligibleConnectionCount: result.EligibleConnectionCount,
            ChangedConnectionCount: result.ChangedConnectionCount,
            NoChangeConnectionCount: result.NoChangeConnectionCount,
            FailedConnectionCount: result.FailedConnectionCount,
            SkippedConnectionCount: result.SkippedConnectionCount,
            LastSuccessfulSyncUtc: result.LastSuccessfulSyncUtc,
            Connections: result.Connections
                .Select(connection => new GlobalBankSyncConnectionResponse(
                    ConnectionId: connection.ConnectionId,
                    ProviderDisplayName: connection.ProviderDisplayName,
                    Status: connection.Status,
                    Outcome: connection.Outcome,
                    AccountsSynced: connection.AccountsSynced,
                    BalancesSynced: connection.BalancesSynced,
                    TransactionsImported: connection.TransactionsImported,
                    SyncedAtUtc: connection.SyncedAtUtc,
                    DataChanged: connection.DataChanged,
                    ErrorCode: connection.ErrorCode,
                    ErrorMessage: connection.ErrorMessage))
                .ToList());

        return Results.Ok(response);
    }
}
