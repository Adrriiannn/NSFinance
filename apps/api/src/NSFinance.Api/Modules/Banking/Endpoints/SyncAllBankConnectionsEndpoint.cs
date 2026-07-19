using System.Diagnostics;
using System;
using System.Linq;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class SyncAllBankConnectionsEndpoint
{
    public static async Task<IResult> HandleAsync(
        GlobalBankSyncRequest? request,
        ICurrentUserProvider currentUserProvider,
        BankGlobalSyncService globalSyncService,
        MerchantCategorizationBackfillService merchantBackfillService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }
        var logger = loggerFactory.CreateLogger("Banking.SyncAllBankConnectionsEndpoint");
        var endpointStopwatch = Stopwatch.StartNew();
        var detachedSyncToken = CancellationToken.None;

        try
        {
            logger.LogInformation(
                "Starting global banking sync request userId={UserId} trigger={Trigger} source={Source} requestTokenCanBeCanceled={RequestTokenCanBeCanceled} requestTokenAlreadyCanceled={RequestTokenAlreadyCanceled} executionTokenDetached={ExecutionTokenDetached}",
                userId,
                request?.Trigger ?? "manual",
                request?.Source ?? "unspecified",
                cancellationToken.CanBeCanceled,
                cancellationToken.IsCancellationRequested,
                true);

            var result = await globalSyncService.ExecuteAsync(
                userId,
                trigger: request?.Trigger,
                source: request?.Source,
                force: request?.Force ?? false,
                detachedSyncToken);

            logger.LogInformation(
                "Completed global banking sync request userId={UserId} trigger={Trigger} outcome={Outcome} elapsedMs={ElapsedMs}",
                userId,
                result.Trigger,
                result.Outcome,
                endpointStopwatch.ElapsedMilliseconds);

            if (merchantBackfillService.IsEnabled)
            {
                try
                {
                    await merchantBackfillService.BackfillAsync(userId, detachedSyncToken);
                }
                catch (Exception backfillException)
                {
                    // Categorization must never fail a sync response.
                    logger.LogError(
                        backfillException,
                        "Merchant categorization backfill failed after global sync userId={UserId}",
                        userId);
                }
            }

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
                LastManualSyncRequestUtc: result.LastManualSyncRequestUtc,
                NextEligibleManualSyncUtc: result.NextEligibleManualSyncUtc,
                ProviderBackoffConnectionCount: result.ProviderBackoffConnectionCount,
                NoNewerRowsConnectionCount: result.NoNewerRowsConnectionCount,
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
                        LastSyncAttemptedUtc: connection.LastSyncAttemptedUtc,
                        LastSuccessfulSyncUtc: connection.LastSuccessfulSyncUtc,
                        ProviderBackoffUntilUtc: connection.ProviderBackoffUntilUtc,
                        LatestFetchedRowUtc: connection.LatestFetchedRowUtc,
                        HasFetchedRowNewerThanCheckpoint: connection.HasFetchedRowNewerThanCheckpoint,
                        FreshnessSummary: connection.FreshnessSummary,
                        HistoricalEnrichmentInProgress: connection.HistoricalEnrichmentInProgress,
                        HistoricalEnrichmentCompleted: connection.HistoricalEnrichmentCompleted,
                        HistoricalEnrichmentProgressPercent: connection.HistoricalEnrichmentProgressPercent,
                        HistoricalEnrichmentCheckpointUtc: connection.HistoricalEnrichmentCheckpointUtc,
                        ErrorCode: connection.ErrorCode,
                        ErrorMessage: connection.ErrorMessage))
                    .ToList());

            return Results.Ok(response);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected error while handling global banking sync endpoint userId={UserId} elapsedMs={ElapsedMs}",
                userId,
                endpointStopwatch.ElapsedMilliseconds);

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
                LastManualSyncRequestUtc: null,
                NextEligibleManualSyncUtc: null,
                ProviderBackoffConnectionCount: 0,
                NoNewerRowsConnectionCount: 0,
                Connections: []));
        }
    }
}
