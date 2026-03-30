using System;
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
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }
        var logger = loggerFactory.CreateLogger("Banking.SyncAllBankConnectionsEndpoint");

        try
        {
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
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected error while handling global banking sync endpoint userId={UserId}",
                userId);

            return Results.Ok(new GlobalBankSyncResponse(
                Trigger: string.Equals(request?.Trigger, "auto", StringComparison.OrdinalIgnoreCase) ? "auto" : "manual",
                Outcome: "failed_unexpected",
                RequestedAtUtc: DateTime.UtcNow,
                CompletedAtUtc: DateTime.UtcNow,
                DueNow: false,
                CooldownRemainingSeconds: 0,
                CooldownUntilUtc: null,
                EligibleConnectionCount: 0,
                ChangedConnectionCount: 0,
                NoChangeConnectionCount: 0,
                FailedConnectionCount: 0,
                SkippedConnectionCount: 0,
                LastSuccessfulSyncUtc: null,
                Connections: []));
        }
    }
}
