using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public static class DeterministicReclassificationTriggerReasons
{
    public const string SyncChangesInitialImport = "sync_changes_initial_import";
    public const string SyncChangesManualRefresh = "sync_changes_manual_refresh";
    public const string SyncChangesAutoRefresh = "sync_changes_auto_refresh";
    public const string ConnectionCreatedUniverseExpansion = "connection_created_universe_expansion";
    public const string DisconnectUniverseContraction = "disconnect_universe_contraction";
    public const string ReconnectRelink = "reconnect_relink";
    public const string ProjectedRowRemapOrDedupeCorrection = "projected_row_remap_or_dedupe_correction";
    public const string DeterministicRuleVersionChanged = "deterministic_rule_version_changed";
    public const string RowTruthReclassificationDebt = "row_truth_reclassification_debt";
}

public sealed record DeterministicReclassificationTriggerRequest(
    Guid UserId,
    string Source,
    string ReasonCode,
    Guid? SourceConnectionId = null,
    IReadOnlyCollection<Guid>? ConnectionIds = null,
    IReadOnlyCollection<Guid>? FinancialAccountIds = null,
    IReadOnlyCollection<Guid>? TransactionIds = null,
    bool MarkConnectionsForHistoricalReplay = true,
    bool QueueConnections = true,
    bool RequireImportedFootprint = false);

public sealed record DeterministicReclassificationTriggerResult(
    int ConnectionsResolved,
    int ConnectionsMarked,
    int QueueRequestsAttempted,
    int QueueFailures);

public sealed class DeterministicReclassificationTriggerService(
    AppDbContext dbContext,
    IBankDeterministicEnrichmentQueue enrichmentQueue,
    ILogger<DeterministicReclassificationTriggerService> logger)
{
    public async Task<DeterministicReclassificationTriggerResult> TriggerAsync(
        DeterministicReclassificationTriggerRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw new ArgumentException("Trigger source is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw new ArgumentException("Trigger reason code is required.", nameof(request));
        }

        var targetConnectionIds = await ResolveTargetConnectionIdsAsync(request, cancellationToken);
        if (targetConnectionIds.Count == 0)
        {
            logger.LogInformation(
                "Deterministic reclassification trigger resolved no target connections source={Source} reason={Reason} userId={UserId}",
                request.Source,
                request.ReasonCode,
                request.UserId);

            return new DeterministicReclassificationTriggerResult(
                ConnectionsResolved: 0,
                ConnectionsMarked: 0,
                QueueRequestsAttempted: 0,
                QueueFailures: 0);
        }

        var connections = await dbContext.OpenBankingConnections
            .Where(x => x.UserId == request.UserId && targetConnectionIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (request.RequireImportedFootprint)
        {
            var connectionIdsWithImportedFootprint = await dbContext.LinkedBankAccounts
                .AsNoTracking()
                .Where(x =>
                    x.FinancialAccountId.HasValue
                    && x.Connection != null
                    && x.Connection.UserId == request.UserId)
                .Join(
                    dbContext.Transactions.AsNoTracking(),
                    linked => linked.FinancialAccountId!.Value,
                    tx => tx.FinancialAccountId,
                    (linked, _tx) => linked.ConnectionId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var footprintSet = connectionIdsWithImportedFootprint.ToHashSet();
            connections = connections
                .Where(x => footprintSet.Contains(x.Id))
                .ToList();
        }

        var now = DateTime.UtcNow;
        var marked = 0;
        if (request.MarkConnectionsForHistoricalReplay)
        {
            foreach (var connection in connections)
            {
                if (!MarkConnectionForHistoricalReplay(connection, now))
                {
                    continue;
                }

                marked++;
            }

            if (marked > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var queueAttempts = 0;
        var queueFailures = 0;
        if (request.QueueConnections)
        {
            foreach (var connection in connections)
            {
                queueAttempts++;
                if (!TryQueueConnectionNonBlocking(request.UserId, connection.Id, request.ReasonCode))
                {
                    queueFailures++;
                }
            }
        }

        logger.LogInformation(
            "Deterministic reclassification trigger handled source={Source} reason={Reason} userId={UserId} resolvedConnections={ResolvedConnections} markedConnections={MarkedConnections} queueAttempts={QueueAttempts} queueFailures={QueueFailures} connectionScopeCount={ConnectionScopeCount} financialAccountScopeCount={FinancialAccountScopeCount} transactionScopeCount={TransactionScopeCount}",
            request.Source,
            request.ReasonCode,
            request.UserId,
            connections.Count,
            marked,
            queueAttempts,
            queueFailures,
            request.ConnectionIds?.Count ?? 0,
            request.FinancialAccountIds?.Count ?? 0,
            request.TransactionIds?.Count ?? 0);

        return new DeterministicReclassificationTriggerResult(
            ConnectionsResolved: connections.Count,
            ConnectionsMarked: marked,
            QueueRequestsAttempted: queueAttempts,
            QueueFailures: queueFailures);
    }

    private bool TryQueueConnectionNonBlocking(
        Guid userId,
        Guid connectionId,
        string reasonCode)
    {
        try
        {
            var queueTask = enrichmentQueue.QueueConnectionAsync(
                userId,
                connectionId,
                reasonCode,
                CancellationToken.None);

            if (queueTask.IsCompleted)
            {
                try
                {
                    queueTask.GetAwaiter().GetResult();
                    return true;
                }
                catch (Exception queueException)
                {
                    logger.LogWarning(
                        queueException,
                        "Deterministic reclassification trigger queue failed userId={UserId} connectionId={ConnectionId} reason={Reason}",
                        userId,
                        connectionId,
                        reasonCode);
                    return false;
                }
            }

            if (!queueTask.IsCompletedSuccessfully)
            {
                _ = ObserveQueueCompletionFailureAsync(queueTask, userId, connectionId, reasonCode);
            }

            return true;
        }
        catch (Exception queueException)
        {
            logger.LogWarning(
                queueException,
                "Deterministic reclassification trigger queue failed userId={UserId} connectionId={ConnectionId} reason={Reason}",
                userId,
                connectionId,
                reasonCode);
            return false;
        }
    }

    private async Task ObserveQueueCompletionFailureAsync(
        ValueTask queueTask,
        Guid userId,
        Guid connectionId,
        string reasonCode)
    {
        try
        {
            await queueTask;
        }
        catch (Exception queueException)
        {
            logger.LogWarning(
                queueException,
                "Deterministic reclassification trigger enqueue completed with failure userId={UserId} connectionId={ConnectionId} reason={Reason}",
                userId,
                connectionId,
                reasonCode);
        }
    }

    private async Task<HashSet<Guid>> ResolveTargetConnectionIdsAsync(
        DeterministicReclassificationTriggerRequest request,
        CancellationToken cancellationToken)
    {
        var resolved = new HashSet<Guid>();
        if (request.SourceConnectionId.HasValue)
        {
            resolved.Add(request.SourceConnectionId.Value);
        }

        if (request.ConnectionIds is { Count: > 0 })
        {
            foreach (var connectionId in request.ConnectionIds)
            {
                resolved.Add(connectionId);
            }
        }

        if (request.FinancialAccountIds is { Count: > 0 })
        {
            var accountScope = request.FinancialAccountIds.ToHashSet();
            var fromAccounts = await dbContext.LinkedBankAccounts
                .AsNoTracking()
                .Where(x =>
                    x.FinancialAccountId.HasValue
                    && accountScope.Contains(x.FinancialAccountId.Value)
                    && x.Connection != null
                    && x.Connection.UserId == request.UserId)
                .Select(x => x.ConnectionId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var connectionId in fromAccounts)
            {
                resolved.Add(connectionId);
            }
        }

        if (request.TransactionIds is { Count: > 0 })
        {
            var transactionScope = request.TransactionIds.ToHashSet();
            var financialAccountIds = await dbContext.Transactions
                .AsNoTracking()
                .Where(x => transactionScope.Contains(x.Id))
                .Select(x => x.FinancialAccountId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var accountScope = financialAccountIds.ToHashSet();

            var fromTransactions = await dbContext.LinkedBankAccounts
                .AsNoTracking()
                .Where(x =>
                    x.FinancialAccountId.HasValue
                    && accountScope.Contains(x.FinancialAccountId.Value)
                    && x.Connection != null
                    && x.Connection.UserId == request.UserId)
                .Select(x => x.ConnectionId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var connectionId in fromTransactions)
            {
                resolved.Add(connectionId);
            }
        }

        return resolved;
    }

    private static bool MarkConnectionForHistoricalReplay(
        Persistence.Entities.OpenBankingConnection connection,
        DateTime now)
    {
        var changed = false;
        if (!connection.NeedsHistoricalReclassification)
        {
            connection.NeedsHistoricalReclassification = true;
            changed = true;
        }

        if (connection.HistoricalEnrichmentStartedUtc.HasValue)
        {
            connection.HistoricalEnrichmentStartedUtc = null;
            changed = true;
        }

        if (connection.HistoricalEnrichmentCompletedUtc.HasValue)
        {
            connection.HistoricalEnrichmentCompletedUtc = null;
            changed = true;
        }

        if (connection.HistoricalEnrichmentCheckpointUtc.HasValue)
        {
            connection.HistoricalEnrichmentCheckpointUtc = null;
            changed = true;
        }

        if (changed)
        {
            connection.UpdatedUtc = now;
        }

        return changed;
    }
}
