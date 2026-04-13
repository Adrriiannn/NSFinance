using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public interface IBankDeterministicEnrichmentQueue
{
    ValueTask QueueConnectionAsync(
        Guid userId,
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default);
}

internal sealed record BankDeterministicEnrichmentWorkItem(
    Guid UserId,
    Guid ConnectionId,
    string Reason);

public static class DeterministicEnrichmentContinuationPolicy
{
    public static bool ShouldContinue(
        int rowsRemaining,
        int rowsActionableRemaining,
        int rowsNotEvaluated,
        int rowsEvaluating,
        int rowsVersionBehind,
        int rowsMarkedForReclassification,
        int rowsSupersededRecomputeRequired)
    {
        if (rowsActionableRemaining > 0)
        {
            return true;
        }

        if (rowsNotEvaluated > 0
            || rowsEvaluating > 0
            || rowsVersionBehind > 0
            || rowsMarkedForReclassification > 0
            || rowsSupersededRecomputeRequired > 0)
        {
            return true;
        }

        return false;
    }

    public static string ResolveReason(
        int rowsRemaining,
        int rowsActionableRemaining,
        int rowsNotEvaluated,
        int rowsEvaluating,
        int rowsVersionBehind,
        int rowsMarkedForReclassification,
        int rowsSupersededRecomputeRequired)
    {
        if (rowsActionableRemaining > 0)
        {
            return "actionable_remaining_rows";
        }

        if (rowsNotEvaluated > 0)
        {
            return "not_evaluated_rows_remaining";
        }

        if (rowsVersionBehind > 0)
        {
            return "version_behind_rows_remaining";
        }

        if (rowsMarkedForReclassification > 0 || rowsSupersededRecomputeRequired > 0)
        {
            return "explicit_recompute_rows_remaining";
        }

        if (rowsEvaluating > 0)
        {
            return "evaluating_rows_remaining";
        }

        return rowsRemaining > 0
            ? "deferred_only_remaining_rows"
            : "no_remaining_rows";
}
}

public sealed class BankDeterministicEnrichmentBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BankDeterministicEnrichmentBackgroundWorker> logger) : BackgroundService, IBankDeterministicEnrichmentQueue
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PendingSweepInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaxContinuationRuntimePerWorkItem = TimeSpan.FromMinutes(2);
    private const int DeterministicEnrichmentCurrentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;
    private const int MaxContinuationRunsPerWorkItem = 512;

    private readonly Channel<BankDeterministicEnrichmentWorkItem> _queue = Channel.CreateUnbounded<BankDeterministicEnrichmentWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _queuedKeys = new(StringComparer.Ordinal);

    public async ValueTask QueueConnectionAsync(
        Guid userId,
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var key = BuildQueueKey(userId, connectionId);
        if (!_queuedKeys.TryAdd(key, 0))
        {
            logger.LogDebug(
                "Skipped duplicate deterministic enrichment queue request connectionId={ConnectionId} userId={UserId} reason={Reason}",
                connectionId,
                userId,
                reason);
            return;
        }

        logger.LogInformation(
            "Queued deterministic enrichment connectionId={ConnectionId} userId={UserId} reason={Reason} queueDepth={QueueDepth}",
            connectionId,
            userId,
            reason,
            _queuedKeys.Count);

        try
        {
            await _queue.Writer.WriteAsync(
                new BankDeterministicEnrichmentWorkItem(userId, connectionId, reason),
                cancellationToken);
        }
        catch
        {
            _queuedKeys.TryRemove(key, out _);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextSweepAtUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow >= nextSweepAtUtc)
            {
                try
                {
                    await EnqueuePendingConnectionsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception sweepException)
                {
                    logger.LogError(
                        sweepException,
                        "Deterministic enrichment pending sweep crashed; worker will continue running.");
                }

                nextSweepAtUtc = DateTime.UtcNow.Add(PendingSweepInterval);
            }

            if (!_queue.Reader.TryRead(out var workItem))
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            _queuedKeys.TryRemove(BuildQueueKey(workItem.UserId, workItem.ConnectionId), out _);

            using var scope = scopeFactory.CreateScope();
            var bankSyncService = scope.ServiceProvider.GetRequiredService<BankSyncService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                var runStartUtc = DateTime.UtcNow;
                var continuationRuns = 0;
                var activeReason = workItem.Reason;

                while (!stoppingToken.IsCancellationRequested)
                {
                    var nowUtc = DateTime.UtcNow;
                    var deferredCounterpartyExpiryThresholdUtc = nowUtc.AddHours(-48);
                    var deferredMoreContextExpiryThresholdUtc = nowUtc.AddHours(-24);
                    var connectionFinancialAccountIds = await dbContext.LinkedBankAccounts
                        .AsNoTracking()
                        .Where(x => x.ConnectionId == workItem.ConnectionId && x.FinancialAccountId.HasValue)
                        .Select(x => x.FinancialAccountId!.Value)
                        .Distinct()
                        .ToListAsync(stoppingToken);
                    var sameUserFinancialAccountIds = await dbContext.LinkedBankAccounts
                        .AsNoTracking()
                        .Where(x =>
                            x.FinancialAccountId.HasValue
                            && x.Connection != null
                            && x.Connection.UserId == workItem.UserId)
                        .Select(x => x.FinancialAccountId!.Value)
                        .Distinct()
                        .ToListAsync(stoppingToken);

                    var actionableRowsQuery = dbContext.Transactions
                        .AsNoTracking()
                        .Where(t =>
                            t.NeedsDeterministicReclassification
                            || !t.DeterministicClassificationVersion.HasValue
                            || t.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion
                            || t.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired
                            || (!t.DeterministicClassificationTerminal
                                && (t.DeterministicClassificationStatus != DeterministicClassificationStatus.DeferredWaitingForCounterparty
                                    && t.DeterministicClassificationStatus != DeterministicClassificationStatus.DeferredWaitingForMoreContext
                                    || (t.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty
                                        && t.BookedAtUtc <= deferredCounterpartyExpiryThresholdUtc)
                                    || (t.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext
                                        && t.BookedAtUtc <= deferredMoreContextExpiryThresholdUtc))));
                    var actionableRowsOnConnection = connectionFinancialAccountIds.Count == 0
                        ? 0
                        : await actionableRowsQuery
                            .Where(t => connectionFinancialAccountIds.Contains(t.FinancialAccountId))
                            .CountAsync(stoppingToken);
                    var actionableRowsForUser = sameUserFinancialAccountIds.Count == 0
                        ? 0
                        : await actionableRowsQuery
                            .Where(t => sameUserFinancialAccountIds.Contains(t.FinancialAccountId))
                            .CountAsync(stoppingToken);
                    var connectionAccountIdsText = connectionFinancialAccountIds.Count == 0
                        ? "none"
                        : string.Join(
                            ",",
                            connectionFinancialAccountIds
                                .OrderBy(x => x)
                                .Take(12)
                                .Select(x => x.ToString("N")));

                    logger.LogInformation(
                        "Deterministic enrichment worker pickup connectionId={ConnectionId} userId={UserId} reason={Reason} continuationRun={ContinuationRun} connectionFinancialAccountCount={ConnectionFinancialAccountCount} connectionFinancialAccountIds={ConnectionFinancialAccountIds} actionableRowsOnConnection={ActionableRowsOnConnection} actionableRowsForUser={ActionableRowsForUser}",
                        workItem.ConnectionId,
                        workItem.UserId,
                        activeReason,
                        continuationRuns,
                        connectionFinancialAccountIds.Count,
                        connectionAccountIdsText,
                        actionableRowsOnConnection,
                        actionableRowsForUser);

                    var result = await bankSyncService.RunDeterministicEnrichmentAsync(
                        workItem.UserId,
                        workItem.ConnectionId,
                        "background_queue",
                        stoppingToken);

                    if (!result.Succeeded)
                    {
                        logger.LogWarning(
                            "Deterministic enrichment batch failed connectionId={ConnectionId} userId={UserId} continuationRun={ContinuationRun} code={Code}",
                            workItem.ConnectionId,
                            workItem.UserId,
                            continuationRuns,
                            result.Error?.Code);
                        break;
                    }

                    logger.LogInformation(
                        "Deterministic enrichment batch processed connectionId={ConnectionId} userId={UserId} continuationRun={ContinuationRun} inProgress={InProgress} completed={Completed} progressPercent={ProgressPercent} rowsEvaluated={RowsEvaluated} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining} rowsDeferredRemaining={RowsDeferredRemaining} rowsDeferredCounterparty={RowsDeferredCounterparty} rowsDeferredMoreContext={RowsDeferredMoreContext} rowsDeferredLegitimateWaiting={RowsDeferredLegitimateWaiting} rowsDeferredReadyForTerminalization={RowsDeferredReadyForTerminalization} rowsRejectedAmbiguous={RowsRejectedAmbiguous} rowsEvaluatedNoMatch={RowsEvaluatedNoMatch} fullCounterpartyUniversePresent={FullCounterpartyUniversePresent} deferredReasonBreakdown={DeferredReasonBreakdown} deferredFamilyBreakdown={DeferredFamilyBreakdown}",
                        workItem.ConnectionId,
                        workItem.UserId,
                        continuationRuns,
                        result.Value.HistoricalEnrichmentInProgress,
                        result.Value.HistoricalEnrichmentCompleted,
                        result.Value.HistoricalEnrichmentProgressPercent,
                        result.Value.RowsEvaluated,
                        result.Value.RowsRemaining,
                        result.Value.RowsActionableRemaining,
                        result.Value.RowsDeferredRemaining,
                        result.Value.RowsDeferredWaitingForCounterparty,
                        result.Value.RowsDeferredWaitingForMoreContext,
                        result.Value.RowsDeferredLegitimateWaiting,
                        result.Value.RowsDeferredReadyForTerminalization,
                        result.Value.RowsRejectedAmbiguous,
                        result.Value.RowsEvaluatedNoMatchingRule,
                        result.Value.FullSameUserCounterpartyUniversePresent,
                        result.Value.DeferredReasonBreakdown,
                        result.Value.DeferredFamilyBreakdown);

                    var shouldContinue = DeterministicEnrichmentContinuationPolicy.ShouldContinue(
                        result.Value.RowsRemaining,
                        result.Value.RowsActionableRemaining,
                        result.Value.RowsNotEvaluated,
                        result.Value.RowsEvaluating,
                        result.Value.RowsVersionBehind,
                        result.Value.RowsMarkedForReclassification,
                        result.Value.RowsSupersededRecomputeRequired);
                    var decisionReason = DeterministicEnrichmentContinuationPolicy.ResolveReason(
                        result.Value.RowsRemaining,
                        result.Value.RowsActionableRemaining,
                        result.Value.RowsNotEvaluated,
                        result.Value.RowsEvaluating,
                        result.Value.RowsVersionBehind,
                        result.Value.RowsMarkedForReclassification,
                        result.Value.RowsSupersededRecomputeRequired);

                    if (!shouldContinue)
                    {
                        logger.LogInformation(
                            "Deterministic enrichment continuation decision connectionId={ConnectionId} userId={UserId} decision=stop reason={Reason} continuationRun={ContinuationRun} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining} rowsNotEvaluated={RowsNotEvaluated} rowsEvaluating={RowsEvaluating} rowsVersionBehind={RowsVersionBehind} rowsMarkedForReclassification={RowsMarkedForReclassification} rowsSupersededRecomputeRequired={RowsSupersededRecomputeRequired} rowsDeferredRemaining={RowsDeferredRemaining} rowsDeferredCounterparty={RowsDeferredCounterparty} rowsDeferredMoreContext={RowsDeferredMoreContext} rowsDeferredLegitimateWaiting={RowsDeferredLegitimateWaiting} rowsDeferredReadyForTerminalization={RowsDeferredReadyForTerminalization} rowsRejectedAmbiguous={RowsRejectedAmbiguous} rowsEvaluatedNoMatch={RowsEvaluatedNoMatch} fullCounterpartyUniversePresent={FullCounterpartyUniversePresent} deferredReasonBreakdown={DeferredReasonBreakdown} deferredFamilyBreakdown={DeferredFamilyBreakdown}",
                            workItem.ConnectionId,
                            workItem.UserId,
                            decisionReason,
                            continuationRuns,
                            result.Value.RowsRemaining,
                            result.Value.RowsActionableRemaining,
                            result.Value.RowsNotEvaluated,
                            result.Value.RowsEvaluating,
                            result.Value.RowsVersionBehind,
                            result.Value.RowsMarkedForReclassification,
                            result.Value.RowsSupersededRecomputeRequired,
                            result.Value.RowsDeferredRemaining,
                            result.Value.RowsDeferredWaitingForCounterparty,
                            result.Value.RowsDeferredWaitingForMoreContext,
                            result.Value.RowsDeferredLegitimateWaiting,
                            result.Value.RowsDeferredReadyForTerminalization,
                            result.Value.RowsRejectedAmbiguous,
                            result.Value.RowsEvaluatedNoMatchingRule,
                            result.Value.FullSameUserCounterpartyUniversePresent,
                            result.Value.DeferredReasonBreakdown,
                            result.Value.DeferredFamilyBreakdown);
                        break;
                    }

                    continuationRuns++;
                    var elapsed = DateTime.UtcNow - runStartUtc;
                    var withinRunBudget = continuationRuns < MaxContinuationRunsPerWorkItem
                        && elapsed < MaxContinuationRuntimePerWorkItem;
                    if (!withinRunBudget)
                    {
                        var budgetReason = continuationRuns >= MaxContinuationRunsPerWorkItem
                            ? "max_runs_per_work_item_reached"
                            : "max_runtime_per_work_item_reached";

                        logger.LogInformation(
                            "Deterministic enrichment continuation budget exhausted connectionId={ConnectionId} userId={UserId} continuationRun={ContinuationRun} elapsedSeconds={ElapsedSeconds} budgetReason={BudgetReason} decision=enqueue_follow_up",
                            workItem.ConnectionId,
                            workItem.UserId,
                            continuationRuns,
                            (int)Math.Max(0, elapsed.TotalSeconds),
                            budgetReason);

                        await QueueConnectionAsync(
                            workItem.UserId,
                            workItem.ConnectionId,
                            "continue_historical_backfill",
                            stoppingToken);
                        break;
                    }

                    logger.LogInformation(
                        "Deterministic enrichment continuation decision connectionId={ConnectionId} userId={UserId} decision=continue_inline reason={Reason} continuationRun={ContinuationRun} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining} rowsNotEvaluated={RowsNotEvaluated} rowsEvaluating={RowsEvaluating} rowsVersionBehind={RowsVersionBehind} rowsMarkedForReclassification={RowsMarkedForReclassification} rowsSupersededRecomputeRequired={RowsSupersededRecomputeRequired}",
                        workItem.ConnectionId,
                        workItem.UserId,
                        decisionReason,
                        continuationRuns,
                        result.Value.RowsRemaining,
                        result.Value.RowsActionableRemaining,
                        result.Value.RowsNotEvaluated,
                        result.Value.RowsEvaluating,
                        result.Value.RowsVersionBehind,
                        result.Value.RowsMarkedForReclassification,
                        result.Value.RowsSupersededRecomputeRequired);

                    activeReason = "continue_historical_backfill_inline";
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Deterministic enrichment worker crashed for connectionId={ConnectionId} userId={UserId}",
                    workItem.ConnectionId,
                    workItem.UserId);
            }
        }
    }

    private async Task EnqueuePendingConnectionsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nowUtc = DateTime.UtcNow;
        var deferredCounterpartyExpiryThresholdUtc = nowUtc.AddHours(-48);
        var deferredMoreContextExpiryThresholdUtc = nowUtc.AddHours(-24);
        var connectionIdsWithImportedRows = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => x.FinancialAccountId.HasValue)
            .Join(
                dbContext.Transactions
                    .AsNoTracking(),
                linked => linked.FinancialAccountId!.Value,
                tx => tx.FinancialAccountId,
                (linked, _tx) => linked.ConnectionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var pendingByFlags = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x =>
                (x.NeedsHistoricalReclassification
                 || !x.HistoricalEnrichmentCompletedUtc.HasValue
                 || (x.HistoricalEnrichmentVersion ?? 0) < DeterministicEnrichmentCurrentVersion)
                && connectionIdsWithImportedRows.Contains(x.Id)
                && (x.Status == BankConnectionStatuses.ConnectedPendingSync
                    || x.Status == BankConnectionStatuses.Connected
                    || x.Status == BankConnectionStatuses.SyncPending
                    || x.Status == BankConnectionStatuses.Synced
                    || x.Status == BankConnectionStatuses.ReauthRequired
                    || x.Status == BankConnectionStatuses.Expired
                    || x.Status == BankConnectionStatuses.Failed)
                && x.Status != BankConnectionStatuses.DisconnectPending
                && x.Status != BankConnectionStatuses.DisconnectFailed
                && x.Status != BankConnectionStatuses.Revoked)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new
            {
                x.UserId,
                x.Id,
                x.NeedsHistoricalReclassification,
                x.HistoricalEnrichmentCompletedUtc,
                x.HistoricalEnrichmentVersion
            })
            .Take(32)
            .ToListAsync(cancellationToken);

        var actionableByRowTruth = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && (x.Connection.Status == BankConnectionStatuses.ConnectedPendingSync
                    || x.Connection.Status == BankConnectionStatuses.Connected
                    || x.Connection.Status == BankConnectionStatuses.SyncPending
                    || x.Connection.Status == BankConnectionStatuses.Synced
                    || x.Connection.Status == BankConnectionStatuses.ReauthRequired
                    || x.Connection.Status == BankConnectionStatuses.Expired
                    || x.Connection.Status == BankConnectionStatuses.Failed)
                && x.Connection.Status != BankConnectionStatuses.DisconnectPending
                && x.Connection.Status != BankConnectionStatuses.DisconnectFailed
                && x.Connection.Status != BankConnectionStatuses.Revoked)
            .Join(
                dbContext.Transactions
                    .AsNoTracking()
                    .Where(t =>
                        t.NeedsDeterministicReclassification
                        || !t.DeterministicClassificationVersion.HasValue
                        || t.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion
                        || t.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired
                        || (!t.DeterministicClassificationTerminal
                            && (t.DeterministicClassificationStatus != DeterministicClassificationStatus.DeferredWaitingForCounterparty
                                && t.DeterministicClassificationStatus != DeterministicClassificationStatus.DeferredWaitingForMoreContext
                                || (t.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty
                                    && t.BookedAtUtc <= deferredCounterpartyExpiryThresholdUtc)
                                || (t.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext
                                    && t.BookedAtUtc <= deferredMoreContextExpiryThresholdUtc)))),
                linked => linked.FinancialAccountId!.Value,
                tx => tx.FinancialAccountId,
                (linked, _tx) => new { linked.ConnectionId, linked.Connection!.UserId })
            .Distinct()
            .Take(64)
            .ToListAsync(cancellationToken);

        var pendingConnections = pendingByFlags
            .Select(x => new
            {
                UserId = x.UserId,
                Id = x.Id,
                Reason = ResolvePendingFlagReason(
                    x.NeedsHistoricalReclassification,
                    x.HistoricalEnrichmentCompletedUtc,
                    x.HistoricalEnrichmentVersion)
            })
            .Concat(actionableByRowTruth.Select(x => new
            {
                UserId = x.UserId,
                Id = x.ConnectionId,
                Reason = DeterministicReclassificationTriggerReasons.RowTruthReclassificationDebt
            }))
            .Distinct()
            .Take(64)
            .ToList();

        foreach (var connection in pendingConnections)
        {
            await QueueConnectionAsync(
                connection.UserId,
                connection.Id,
                connection.Reason,
                cancellationToken);
        }
    }

    private static string ResolvePendingFlagReason(
        bool needsHistoricalReclassification,
        DateTime? historicalEnrichmentCompletedUtc,
        int? historicalEnrichmentVersion)
    {
        if ((historicalEnrichmentVersion ?? 0) < DeterministicEnrichmentCurrentVersion)
        {
            return DeterministicReclassificationTriggerReasons.DeterministicRuleVersionChanged;
        }

        if (!historicalEnrichmentCompletedUtc.HasValue)
        {
            return DeterministicReclassificationTriggerReasons.SyncChangesInitialImport;
        }

        if (needsHistoricalReclassification)
        {
            return DeterministicReclassificationTriggerReasons.SyncChangesManualRefresh;
        }

        return DeterministicReclassificationTriggerReasons.RowTruthReclassificationDebt;
    }

    private static string BuildQueueKey(Guid userId, Guid connectionId)
    {
        return $"{userId:N}:{connectionId:N}";
    }
}
