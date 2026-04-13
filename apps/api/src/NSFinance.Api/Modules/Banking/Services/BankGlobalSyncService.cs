using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.Services.Models;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankGlobalSyncService(
    AppDbContext dbContext,
    BankSyncService bankSyncService,
    IAuditService auditService,
    IOptions<BankingSyncOptions> syncOptions,
    ILogger<BankGlobalSyncService> logger)
{
    private const string TriggerManual = "manual";
    private const string TriggerAuto = "auto";
    private readonly int _manualCooldownMinutes = Math.Clamp(syncOptions.Value.ManualCooldownMinutes, 1, 24 * 60);
    private readonly int _autoSyncIntervalMinutes = Math.Clamp(syncOptions.Value.AutoSyncIntervalMinutes, 1, 24 * 60);
    private readonly int _staleSyncPendingRecoveryMinutes = Math.Clamp(syncOptions.Value.StaleSyncPendingRecoveryMinutes, 1, 24 * 60);
    private readonly int _providerRateLimitBackoffMinutes = Math.Clamp(syncOptions.Value.ProviderRateLimitBackoffMinutes, 1, 24 * 60);
    private readonly TimeSpan _manualSyncCooldown = TimeSpan.FromMinutes(Math.Clamp(syncOptions.Value.ManualCooldownMinutes, 1, 24 * 60));
    private readonly TimeSpan _autoSyncDueInterval = TimeSpan.FromMinutes(Math.Clamp(syncOptions.Value.AutoSyncIntervalMinutes, 1, 24 * 60));
    private readonly TimeSpan _syncPendingStaleAfter = TimeSpan.FromMinutes(Math.Clamp(syncOptions.Value.StaleSyncPendingRecoveryMinutes, 1, 24 * 60));
    private readonly TimeSpan _providerRateLimitBackoff = TimeSpan.FromMinutes(Math.Clamp(syncOptions.Value.ProviderRateLimitBackoffMinutes, 1, 24 * 60));

    private static readonly HashSet<string> ManualEligibleStatuses =
    [
        BankConnectionStatuses.ConnectedPendingSync,
        BankConnectionStatuses.Connected,
        BankConnectionStatuses.Synced,
        BankConnectionStatuses.ReauthRequired,
        BankConnectionStatuses.Expired,
        BankConnectionStatuses.Failed,
        BankConnectionStatuses.SyncPending
    ];

    private static readonly HashSet<string> AutoEligibleStatuses =
    [
        BankConnectionStatuses.ConnectedPendingSync,
        BankConnectionStatuses.Connected,
        BankConnectionStatuses.Synced,
        BankConnectionStatuses.SyncPending
    ];

    private sealed record ConnectionSyncCandidate(
        Guid Id,
        string Status,
        string? ProviderId,
        string? ProviderDisplayName,
        DateTime? LastSyncAttemptedUtc,
        DateTime? LastSuccessfulSyncUtc,
        string? LastErrorCode,
        DateTime UpdatedUtc);

    private readonly record struct ManualCooldownState(
        int RemainingSeconds,
        DateTime? CooldownUntilUtc,
        DateTime? LastManualRequestUtc);

    public async Task<BankGlobalSyncResult> ExecuteAsync(
        Guid userId,
        string? trigger,
        string? source,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedTrigger = NormalizeTrigger(trigger);
        var requestedAtUtc = DateTime.UtcNow;
        var normalizedSource = NormalizeSource(source);

        try
        {
            var connectionCandidates = await dbContext.OpenBankingConnections
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new ConnectionSyncCandidate(
                    x.Id,
                    x.Status,
                    x.ProviderId,
                    x.ProviderDisplayName,
                    x.LastSyncAttemptedUtc,
                    x.LastSuccessfulSyncUtc,
                    x.LastErrorCode,
                    x.UpdatedUtc))
                .ToListAsync(cancellationToken);

            var eligibleStatuses = normalizedTrigger == TriggerAuto
                ? AutoEligibleStatuses
                : ManualEligibleStatuses;

            var eligibleCandidates = connectionCandidates
                .Where(x => eligibleStatuses.Contains(x.Status))
                .ToList();
            var ineligibleCandidates = connectionCandidates
                .Where(x => !eligibleStatuses.Contains(x.Status))
                .ToList();
            var dueNow = IsAnyConnectionDue(eligibleCandidates, requestedAtUtc);
            var lastSuccessfulSyncUtc = MaxUtc(eligibleCandidates.Select(x => x.LastSuccessfulSyncUtc));
            var providerBackoffActiveCount = eligibleCandidates.Count(candidate => GetProviderBackoffUntilUtc(candidate, requestedAtUtc).HasValue);
            ManualCooldownState cooldownState = new(0, null, null);
            DateTime? nextEligibleManualSyncUtc = null;

            if (normalizedTrigger == TriggerAuto)
            {
                foreach (var candidate in eligibleCandidates)
                {
                    var dueDecision = GetDueDecision(candidate, requestedAtUtc);
                    logger.LogInformation(
                        "Autosync evaluation connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} lastSyncUtc={LastSyncUtc} cooldownMinutes={CooldownMinutes} willRun={WillRun} reason={Reason}",
                        candidate.Id,
                        candidate.ProviderId ?? "<unknown>",
                        candidate.ProviderDisplayName ?? "<unknown>",
                        candidate.Status,
                        candidate.LastSuccessfulSyncUtc,
                        _autoSyncIntervalMinutes,
                        dueDecision.IsDue,
                        dueDecision.Reason);
                }
            }

            if (ineligibleCandidates.Count > 0)
            {
                logger.LogInformation(
                    "Global banking sync has ineligible connections userId={UserId} trigger={Trigger} ineligibleCount={IneligibleCount} ineligibleStatuses={IneligibleStatuses}",
                    userId,
                    normalizedTrigger,
                    ineligibleCandidates.Count,
                    string.Join(",",
                        ineligibleCandidates
                            .Select(candidate => candidate.Status)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(status => status, StringComparer.OrdinalIgnoreCase)));
            }

            if (eligibleCandidates.Count == 0)
            {
                return CreateSkippedResult(
                    trigger: normalizedTrigger,
                    outcome: "skipped_no_eligible_connections",
                    requestedAtUtc,
                    dueNow: false,
                    cooldownRemainingSeconds: 0,
                    cooldownUntilUtc: null,
                    eligibleConnectionCount: 0,
                    lastSuccessfulSyncUtc,
                    lastManualSyncRequestUtc: null,
                    nextEligibleManualSyncUtc: null,
                    providerBackoffConnectionCount: 0,
                    noNewerRowsConnectionCount: 0);
            }

            if (normalizedTrigger == TriggerAuto && !dueNow)
            {
                var autoSkipOutcome = providerBackoffActiveCount > 0
                    ? "skipped_provider_backoff"
                    : "skipped_not_due";
                logger.LogInformation(
                    "Global banking sync skipped userId={UserId} trigger={Trigger} outcome={Outcome} eligibleConnectionCount={EligibleConnectionCount} providerBackoffActiveCount={ProviderBackoffActiveCount}",
                    userId,
                    normalizedTrigger,
                    autoSkipOutcome,
                    eligibleCandidates.Count,
                    providerBackoffActiveCount);

                return CreateSkippedResult(
                    trigger: normalizedTrigger,
                    outcome: autoSkipOutcome,
                    requestedAtUtc,
                    dueNow,
                    cooldownRemainingSeconds: 0,
                    cooldownUntilUtc: null,
                    eligibleConnectionCount: eligibleCandidates.Count,
                    lastSuccessfulSyncUtc,
                    lastManualSyncRequestUtc: null,
                    nextEligibleManualSyncUtc: null,
                    providerBackoffConnectionCount: providerBackoffActiveCount,
                    noNewerRowsConnectionCount: 0);
            }

            if (normalizedTrigger == TriggerManual)
            {
                cooldownState = await GetManualCooldownStateSafeAsync(userId, requestedAtUtc, cancellationToken);
                nextEligibleManualSyncUtc = cooldownState.RemainingSeconds > 0
                    ? cooldownState.CooldownUntilUtc
                    : requestedAtUtc.Add(_manualSyncCooldown);
                var hasStaleSyncPendingConnection = eligibleCandidates.Any(candidate => IsStaleSyncPending(candidate, requestedAtUtc));
                var manualCooldownAllowed = force || cooldownState.RemainingSeconds <= 0 || hasStaleSyncPendingConnection;
                var manualCooldownReason = cooldownState.RemainingSeconds <= 0
                    ? "cooldown_expired"
                    : force
                        ? "force_override"
                    : hasStaleSyncPendingConnection
                        ? "stale_connection_override"
                        : "cooldown_active";

                logger.LogInformation(
                    "Manual sync cooldown check userId={UserId} lastSyncUtc={LastSyncUtc} cooldownMinutes={CooldownMinutes} allowed={Allowed} reason={Reason}",
                    userId,
                    cooldownState.LastManualRequestUtc,
                    _manualCooldownMinutes,
                    manualCooldownAllowed,
                    manualCooldownReason);

                if (cooldownState.RemainingSeconds > 0)
                {
                    if (force)
                    {
                        logger.LogWarning(
                            "Manual sync cooldown override applied because force=true userId={UserId} remainingSeconds={RemainingSeconds}",
                            userId,
                            cooldownState.RemainingSeconds);
                    }
                    else if (hasStaleSyncPendingConnection)
                    {
                        logger.LogWarning(
                            "Manual sync cooldown override applied because stale sync_pending connection exists userId={UserId} remainingSeconds={RemainingSeconds}",
                            userId,
                            cooldownState.RemainingSeconds);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Global banking sync skipped due to cooldown userId={UserId} remainingSeconds={RemainingSeconds}",
                            userId,
                            cooldownState.RemainingSeconds);

                        await WriteAuditSafeAsync(
                            category: "banking",
                            eventName: "global_manual_sync_cooldown",
                            targetEntityType: "user",
                            targetEntityId: userId.ToString(),
                            actorId: userId,
                            actorType: "user",
                            metadata: new
                            {
                                source = normalizedSource,
                                cooldownRemainingSeconds = cooldownState.RemainingSeconds,
                                cooldownUntilUtc = cooldownState.CooldownUntilUtc
                            },
                            cancellationToken);

                        return CreateSkippedResult(
                            trigger: normalizedTrigger,
                            outcome: "skipped_cooldown",
                            requestedAtUtc,
                            dueNow,
                            cooldownRemainingSeconds: cooldownState.RemainingSeconds,
                            cooldownUntilUtc: cooldownState.CooldownUntilUtc,
                            eligibleConnectionCount: eligibleCandidates.Count,
                            lastSuccessfulSyncUtc,
                            lastManualSyncRequestUtc: cooldownState.LastManualRequestUtc,
                            nextEligibleManualSyncUtc,
                            providerBackoffConnectionCount: providerBackoffActiveCount,
                            noNewerRowsConnectionCount: 0);
                    }
                }
            }

            await WriteAuditSafeAsync(
                category: "banking",
                eventName: normalizedTrigger == TriggerAuto ? "global_auto_sync_triggered" : "global_manual_sync_triggered",
                targetEntityType: "user",
                targetEntityId: userId.ToString(),
                actorId: userId,
                actorType: "user",
                metadata: new
                {
                    source = normalizedSource,
                    dueNow,
                    eligibleConnectionCount = eligibleCandidates.Count,
                    force
                },
                cancellationToken);

            var connectionResults = ineligibleCandidates
                .Select(candidate => new BankGlobalSyncConnectionResult(
                    candidate.Id,
                    candidate.ProviderDisplayName,
                    candidate.Status,
                    Outcome: "skipped_ineligible_status",
                    AccountsSynced: 0,
                    BalancesSynced: 0,
                    TransactionsImported: 0,
                    SyncedAtUtc: null,
                    DataChanged: false,
                    LastSyncAttemptedUtc: candidate.LastSyncAttemptedUtc,
                    LastSuccessfulSyncUtc: candidate.LastSuccessfulSyncUtc,
                    ProviderBackoffUntilUtc: GetProviderBackoffUntilUtc(candidate, requestedAtUtc),
                    LatestFetchedRowUtc: null,
                    HasFetchedRowNewerThanCheckpoint: null,
                    FreshnessSummary: null,
                    HistoricalEnrichmentInProgress: null,
                    HistoricalEnrichmentCompleted: null,
                    HistoricalEnrichmentProgressPercent: null,
                    HistoricalEnrichmentCheckpointUtc: null,
                    ErrorCode: "connection_status_not_syncable",
                    ErrorMessage: $"Connection status '{candidate.Status}' is not eligible for {normalizedTrigger} sync."))
                .ToList();
            var changedConnectionCount = 0;
            var noChangeConnectionCount = 0;
            var failedConnectionCount = 0;
            var skippedConnectionCount = ineligibleCandidates.Count;
            var noNewerRowsConnectionCount = 0;

            foreach (var candidate in eligibleCandidates)
            {
                logger.LogInformation(
                    "Global sync evaluating connection userId={UserId} connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} lastSyncAttemptedUtc={LastSyncAttemptedUtc} lastSuccessfulSyncUtc={LastSuccessfulSyncUtc}",
                    userId,
                    candidate.Id,
                    candidate.ProviderId ?? "<unknown>",
                    candidate.ProviderDisplayName ?? "<unknown>",
                    candidate.Status,
                    candidate.LastSyncAttemptedUtc,
                    candidate.LastSuccessfulSyncUtc);

                var effectiveStatus = candidate.Status;
                var staleSyncPendingRecovered = false;
                var providerBackoffUntilUtc = GetProviderBackoffUntilUtc(candidate, requestedAtUtc);

                if (providerBackoffUntilUtc.HasValue)
                {
                    skippedConnectionCount++;
                    connectionResults.Add(new BankGlobalSyncConnectionResult(
                        candidate.Id,
                        candidate.ProviderDisplayName,
                        effectiveStatus,
                        Outcome: "skipped_provider_backoff",
                        AccountsSynced: 0,
                        BalancesSynced: 0,
                        TransactionsImported: 0,
                        SyncedAtUtc: null,
                        DataChanged: false,
                        LastSyncAttemptedUtc: candidate.LastSyncAttemptedUtc,
                        LastSuccessfulSyncUtc: candidate.LastSuccessfulSyncUtc,
                        ProviderBackoffUntilUtc: providerBackoffUntilUtc,
                        LatestFetchedRowUtc: null,
                        HasFetchedRowNewerThanCheckpoint: null,
                        FreshnessSummary: null,
                        HistoricalEnrichmentInProgress: null,
                        HistoricalEnrichmentCompleted: null,
                        HistoricalEnrichmentProgressPercent: null,
                        HistoricalEnrichmentCheckpointUtc: null,
                        ErrorCode: candidate.LastErrorCode,
                        ErrorMessage: "Provider backoff is active for this connection."));

                    logger.LogInformation(
                        "Global sync connection skipped because provider backoff is active connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} backoffUntilUtc={BackoffUntilUtc} errorCode={ErrorCode}",
                        candidate.Id,
                        candidate.ProviderId ?? "<unknown>",
                        candidate.ProviderDisplayName ?? "<unknown>",
                        effectiveStatus,
                        providerBackoffUntilUtc,
                        candidate.LastErrorCode ?? "<none>");
                    continue;
                }

                if (effectiveStatus == BankConnectionStatuses.SyncPending)
                {
                    var pendingSinceUtc = candidate.LastSyncAttemptedUtc ?? candidate.UpdatedUtc;
                    var pendingAge = requestedAtUtc - pendingSinceUtc;
                    var isStalePending = pendingAge >= _syncPendingStaleAfter;

                    logger.LogInformation(
                        "Global sync evaluated sync_pending state connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} pendingSinceUtc={PendingSinceUtc} pendingAgeSeconds={PendingAgeSeconds} staleThresholdSeconds={StaleThresholdSeconds} isStale={IsStale}",
                        candidate.Id,
                        candidate.ProviderId ?? "<unknown>",
                        candidate.ProviderDisplayName ?? "<unknown>",
                        pendingSinceUtc,
                        (int)Math.Max(0, pendingAge.TotalSeconds),
                        (int)_syncPendingStaleAfter.TotalSeconds,
                        isStalePending);

                    if (!isStalePending)
                    {
                        skippedConnectionCount++;
                        connectionResults.Add(new BankGlobalSyncConnectionResult(
                            candidate.Id,
                            candidate.ProviderDisplayName,
                            effectiveStatus,
                            Outcome: "skipped_sync_in_progress",
                            AccountsSynced: 0,
                            BalancesSynced: 0,
                            TransactionsImported: 0,
                            SyncedAtUtc: null,
                            DataChanged: false,
                            LastSyncAttemptedUtc: candidate.LastSyncAttemptedUtc,
                            LastSuccessfulSyncUtc: candidate.LastSuccessfulSyncUtc,
                            ProviderBackoffUntilUtc: null,
                            LatestFetchedRowUtc: null,
                            HasFetchedRowNewerThanCheckpoint: null,
                            FreshnessSummary: null,
                            HistoricalEnrichmentInProgress: null,
                            HistoricalEnrichmentCompleted: null,
                            HistoricalEnrichmentProgressPercent: null,
                            HistoricalEnrichmentCheckpointUtc: null,
                            ErrorCode: null,
                            ErrorMessage: "Sync already in progress for this connection."));

                        logger.LogInformation(
                            "Global sync connection outcome connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} outcome={Outcome}",
                            candidate.Id,
                            candidate.ProviderId ?? "<unknown>",
                            candidate.ProviderDisplayName ?? "<unknown>",
                            effectiveStatus,
                            "skipped_sync_in_progress");
                        continue;
                    }

                    var recoveredStatus = candidate.LastSuccessfulSyncUtc.HasValue
                        ? BankConnectionStatuses.Synced
                        : BankConnectionStatuses.Connected;

                    var recoveredRows = await RecoverStaleSyncPendingConnectionAsync(
                        userId,
                        candidate,
                        recoveredStatus,
                        requestedAtUtc,
                        cancellationToken);

                    if (recoveredRows == 0)
                    {
                        skippedConnectionCount++;
                        connectionResults.Add(new BankGlobalSyncConnectionResult(
                            candidate.Id,
                            candidate.ProviderDisplayName,
                            effectiveStatus,
                            Outcome: "skipped_sync_in_progress",
                            AccountsSynced: 0,
                            BalancesSynced: 0,
                            TransactionsImported: 0,
                            SyncedAtUtc: null,
                            DataChanged: false,
                            LastSyncAttemptedUtc: candidate.LastSyncAttemptedUtc,
                            LastSuccessfulSyncUtc: candidate.LastSuccessfulSyncUtc,
                            ProviderBackoffUntilUtc: null,
                            LatestFetchedRowUtc: null,
                            HasFetchedRowNewerThanCheckpoint: null,
                            FreshnessSummary: null,
                            HistoricalEnrichmentInProgress: null,
                            HistoricalEnrichmentCompleted: null,
                            HistoricalEnrichmentProgressPercent: null,
                            HistoricalEnrichmentCheckpointUtc: null,
                            ErrorCode: "sync_pending_state_changed",
                            ErrorMessage: "Sync pending state changed during stale recovery check."));

                        logger.LogInformation(
                            "Global sync stale sync_pending recovery skipped because state changed connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName}",
                            candidate.Id,
                            candidate.ProviderId ?? "<unknown>",
                            candidate.ProviderDisplayName ?? "<unknown>");
                        continue;
                    }

                    staleSyncPendingRecovered = true;
                    effectiveStatus = recoveredStatus;
                    logger.LogWarning(
                        "Recovered stale sync_pending connection state connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} previousStatus={PreviousStatus} recoveredStatus={RecoveredStatus} pendingAgeSeconds={PendingAgeSeconds}",
                        candidate.Id,
                        candidate.ProviderId ?? "<unknown>",
                        candidate.ProviderDisplayName ?? "<unknown>",
                        BankConnectionStatuses.SyncPending,
                        recoveredStatus,
                        (int)Math.Max(0, pendingAge.TotalSeconds));
                }

                ServiceResult<BankSyncResult> syncResult;
                try
                {
                    syncResult = await bankSyncService.SyncConnectionAsync(
                        userId,
                        candidate.Id,
                        cancellationToken,
                        trigger: normalizedTrigger == TriggerAuto ? "auto_sync" : "manual_sync");
                }
                catch (Exception exception)
                {
                    failedConnectionCount++;
                    logger.LogError(
                        exception,
                        "Per-connection sync crashed unexpectedly userId={UserId} connectionId={ConnectionId}",
                        userId,
                        candidate.Id);

                    connectionResults.Add(new BankGlobalSyncConnectionResult(
                        candidate.Id,
                        candidate.ProviderDisplayName,
                        effectiveStatus,
                        Outcome: "failed",
                        AccountsSynced: 0,
                        BalancesSynced: 0,
                        TransactionsImported: 0,
                        SyncedAtUtc: null,
                        DataChanged: false,
                        LastSyncAttemptedUtc: candidate.LastSyncAttemptedUtc,
                        LastSuccessfulSyncUtc: candidate.LastSuccessfulSyncUtc,
                        ProviderBackoffUntilUtc: null,
                        LatestFetchedRowUtc: null,
                        HasFetchedRowNewerThanCheckpoint: null,
                        FreshnessSummary: null,
                        HistoricalEnrichmentInProgress: null,
                        HistoricalEnrichmentCompleted: null,
                        HistoricalEnrichmentProgressPercent: null,
                        HistoricalEnrichmentCheckpointUtc: null,
                        ErrorCode: "sync_unexpected_exception",
                        ErrorMessage: "Unexpected sync error. Please retry."));

                    logger.LogError(
                        "Global sync connection outcome connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} outcome={Outcome} errorCode={ErrorCode}",
                        candidate.Id,
                        candidate.ProviderId ?? "<unknown>",
                        candidate.ProviderDisplayName ?? "<unknown>",
                        effectiveStatus,
                        "failed",
                        "sync_unexpected_exception");
                    continue;
                }

                if (syncResult.Succeeded && syncResult.Value is not null)
                {
                    var value = syncResult.Value;
                    if (value.DataChanged)
                    {
                        changedConnectionCount++;
                    }
                    else
                    {
                        noChangeConnectionCount++;
                    }

                    connectionResults.Add(new BankGlobalSyncConnectionResult(
                        value.ConnectionId,
                        candidate.ProviderDisplayName,
                        value.Status,
                        value.DataChanged ? "completed_changed" : "completed_no_change",
                        value.AccountsSynced,
                        value.BalancesSynced,
                        value.TransactionsImported,
                        value.SyncedAtUtc,
                        value.DataChanged,
                        LastSyncAttemptedUtc: value.SyncedAtUtc,
                        LastSuccessfulSyncUtc: value.SyncedAtUtc,
                        ProviderBackoffUntilUtc: null,
                        LatestFetchedRowUtc: value.LatestFetchedRowUtc,
                        HasFetchedRowNewerThanCheckpoint: value.HasFetchedRowNewerThanCheckpoint,
                        FreshnessSummary: value.FreshnessSummary,
                        HistoricalEnrichmentInProgress: value.HistoricalEnrichmentInProgress,
                        HistoricalEnrichmentCompleted: value.HistoricalEnrichmentCompleted,
                        HistoricalEnrichmentProgressPercent: value.HistoricalEnrichmentProgressPercent,
                        HistoricalEnrichmentCheckpointUtc: value.HistoricalEnrichmentCheckpointUtc,
                        ErrorCode: null,
                        ErrorMessage: null));

                    if (string.Equals(value.FreshnessSummary, "no_newer_rows_returned", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value.FreshnessSummary, "no_rows_returned", StringComparison.OrdinalIgnoreCase))
                    {
                        noNewerRowsConnectionCount++;
                    }

                    logger.LogInformation(
                        "Global sync connection outcome connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} outcome={Outcome} staleSyncPendingRecovered={StaleSyncPendingRecovered} accountsSynced={AccountsSynced} balancesSynced={BalancesSynced} rawTransactionsChanged={RawTransactionsChanged} dataChanged={DataChanged}",
                        value.ConnectionId,
                        candidate.ProviderId ?? "<unknown>",
                        candidate.ProviderDisplayName ?? "<unknown>",
                        value.Status,
                        value.DataChanged ? "completed_changed" : "completed_no_change",
                        staleSyncPendingRecovered,
                        value.AccountsSynced,
                        value.BalancesSynced,
                        value.TransactionsImported,
                        value.DataChanged);
                    continue;
                }

                var error = syncResult.Error!;
                var skipped = error.Code is "bank_connection_disconnect_pending" or "bank_connection_disconnected";
                if (skipped)
                {
                    skippedConnectionCount++;
                }
                else
                {
                    failedConnectionCount++;
                }

                connectionResults.Add(new BankGlobalSyncConnectionResult(
                    candidate.Id,
                    candidate.ProviderDisplayName,
                    effectiveStatus,
                    skipped ? "skipped_unavailable" : "failed",
                    AccountsSynced: 0,
                    BalancesSynced: 0,
                    TransactionsImported: 0,
                    SyncedAtUtc: null,
                    DataChanged: false,
                    LastSyncAttemptedUtc: candidate.LastSyncAttemptedUtc,
                    LastSuccessfulSyncUtc: candidate.LastSuccessfulSyncUtc,
                    ProviderBackoffUntilUtc: GetProviderBackoffUntilUtc(candidate, requestedAtUtc),
                    LatestFetchedRowUtc: null,
                    HasFetchedRowNewerThanCheckpoint: null,
                    FreshnessSummary: null,
                    HistoricalEnrichmentInProgress: null,
                    HistoricalEnrichmentCompleted: null,
                    HistoricalEnrichmentProgressPercent: null,
                    HistoricalEnrichmentCheckpointUtc: null,
                    ErrorCode: error.Code,
                    ErrorMessage: error.Message));

                logger.LogWarning(
                    "Global sync connection outcome connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} status={Status} outcome={Outcome} errorCode={ErrorCode}",
                    candidate.Id,
                    candidate.ProviderId ?? "<unknown>",
                    candidate.ProviderDisplayName ?? "<unknown>",
                    effectiveStatus,
                    skipped ? "skipped_unavailable" : "failed",
                    error.Code ?? "<none>");
            }

            var completedAtUtc = DateTime.UtcNow;
            lastSuccessfulSyncUtc = await dbContext.OpenBankingConnections
                .AsNoTracking()
                .Where(x => x.UserId == userId && eligibleStatuses.Contains(x.Status))
                .Select(x => x.LastSuccessfulSyncUtc)
                .OrderByDescending(x => x)
                .FirstOrDefaultAsync(cancellationToken);

            await WriteAuditSafeAsync(
                category: "banking",
                eventName: normalizedTrigger == TriggerAuto ? "global_auto_sync_completed" : "global_manual_sync_completed",
                targetEntityType: "user",
                targetEntityId: userId.ToString(),
                actorId: userId,
                actorType: "user",
                metadata: new
                {
                    source = normalizedSource,
                    dueNow,
                    eligibleConnectionCount = eligibleCandidates.Count,
                    changedConnectionCount,
                    noChangeConnectionCount,
                    failedConnectionCount,
                    skippedConnectionCount
                },
                cancellationToken);

            logger.LogInformation(
                "Global banking sync completed userId={UserId} trigger={Trigger} changed={ChangedConnectionCount} noChange={NoChangeConnectionCount} failed={FailedConnectionCount} skipped={SkippedConnectionCount}",
                userId,
                normalizedTrigger,
                changedConnectionCount,
                noChangeConnectionCount,
                failedConnectionCount,
                skippedConnectionCount);

            var completedOutcome =
                changedConnectionCount == 0
                && noChangeConnectionCount == 0
                && failedConnectionCount == 0
                && connectionResults.Any(connection =>
                    string.Equals(connection.Outcome, "skipped_provider_backoff", StringComparison.Ordinal))
                    ? "skipped_provider_backoff"
                    : "completed";

            return new BankGlobalSyncResult(
                Trigger: normalizedTrigger,
                Outcome: completedOutcome,
                RequestedAtUtc: requestedAtUtc,
                CompletedAtUtc: completedAtUtc,
                DueNow: dueNow,
                CooldownRemainingSeconds: 0,
                CooldownUntilUtc: null,
                EligibleConnectionCount: eligibleCandidates.Count,
                ChangedConnectionCount: changedConnectionCount,
                NoChangeConnectionCount: noChangeConnectionCount,
                FailedConnectionCount: failedConnectionCount,
                SkippedConnectionCount: skippedConnectionCount,
                LastSuccessfulSyncUtc: lastSuccessfulSyncUtc,
                LastManualSyncRequestUtc: normalizedTrigger == TriggerManual ? requestedAtUtc : cooldownState.LastManualRequestUtc,
                NextEligibleManualSyncUtc: normalizedTrigger == TriggerManual ? requestedAtUtc.Add(_manualSyncCooldown) : null,
                ProviderBackoffConnectionCount: providerBackoffActiveCount,
                NoNewerRowsConnectionCount: noNewerRowsConnectionCount,
                Connections: connectionResults);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Global banking sync crashed unexpectedly userId={UserId} trigger={Trigger} source={Source}",
                userId,
                normalizedTrigger,
                normalizedSource);

            await WriteAuditSafeAsync(
                category: "banking",
                eventName: "global_sync_unexpected_error",
                targetEntityType: "user",
                targetEntityId: userId.ToString(),
                actorId: userId,
                actorType: "user",
                metadata: new
                {
                    trigger = normalizedTrigger,
                    source = normalizedSource,
                    message = exception.Message
                },
                cancellationToken);

            return new BankGlobalSyncResult(
                Trigger: normalizedTrigger,
                Outcome: "failed_unexpected",
                RequestedAtUtc: requestedAtUtc,
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
                LastManualSyncRequestUtc: normalizedTrigger == TriggerManual ? requestedAtUtc : null,
                NextEligibleManualSyncUtc: normalizedTrigger == TriggerManual ? requestedAtUtc.Add(_manualSyncCooldown) : null,
                ProviderBackoffConnectionCount: 0,
                NoNewerRowsConnectionCount: 0,
                Connections: []);
        }
    }

    private static BankGlobalSyncResult CreateSkippedResult(
        string trigger,
        string outcome,
        DateTime requestedAtUtc,
        bool dueNow,
        int cooldownRemainingSeconds,
        DateTime? cooldownUntilUtc,
        int eligibleConnectionCount,
        DateTime? lastSuccessfulSyncUtc,
        DateTime? lastManualSyncRequestUtc,
        DateTime? nextEligibleManualSyncUtc,
        int providerBackoffConnectionCount,
        int noNewerRowsConnectionCount)
    {
        return new BankGlobalSyncResult(
            Trigger: trigger,
            Outcome: outcome,
            RequestedAtUtc: requestedAtUtc,
            CompletedAtUtc: null,
            DueNow: dueNow,
            CooldownRemainingSeconds: cooldownRemainingSeconds,
            CooldownUntilUtc: cooldownUntilUtc,
            EligibleConnectionCount: eligibleConnectionCount,
            ChangedConnectionCount: 0,
            NoChangeConnectionCount: 0,
            FailedConnectionCount: 0,
            SkippedConnectionCount: 0,
            LastSuccessfulSyncUtc: lastSuccessfulSyncUtc,
            LastManualSyncRequestUtc: lastManualSyncRequestUtc,
            NextEligibleManualSyncUtc: nextEligibleManualSyncUtc,
            ProviderBackoffConnectionCount: providerBackoffConnectionCount,
            NoNewerRowsConnectionCount: noNewerRowsConnectionCount,
            Connections: []);
    }

    private async Task<ManualCooldownState> GetManualCooldownStateAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lastManualRequestUtc = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.ActorId == userId
                && x.EventCategory == "banking"
                && (x.EventName == "global_manual_sync_triggered" || x.EventName == "manual_sync_triggered"))
            .OrderByDescending(x => x.EventTimestampUtc)
            .Select(x => (DateTime?)x.EventTimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (!lastManualRequestUtc.HasValue)
        {
            return new ManualCooldownState(0, null, null);
        }

        var cooldownUntilUtc = lastManualRequestUtc.Value.Add(_manualSyncCooldown);
        if (cooldownUntilUtc <= now)
        {
            return new ManualCooldownState(0, cooldownUntilUtc, lastManualRequestUtc);
        }

        var remainingSeconds = (int)Math.Ceiling((cooldownUntilUtc - now).TotalSeconds);
        return new ManualCooldownState(Math.Max(1, remainingSeconds), cooldownUntilUtc, lastManualRequestUtc);
    }

    private async Task<ManualCooldownState> GetManualCooldownStateSafeAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetManualCooldownStateAsync(userId, now, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to evaluate global sync cooldown userId={UserId}; proceeding without cooldown enforcement for this request.",
                userId);
            return new ManualCooldownState(0, null, null);
        }
    }

    private async Task WriteAuditSafeAsync(
        string category,
        string eventName,
        string targetEntityType,
        string? targetEntityId,
        Guid? actorId,
        string actorType,
        object? metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.WriteEventAsync(
                category,
                eventName,
                targetEntityType,
                targetEntityId,
                actorId,
                actorType,
                metadata,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to write banking audit event eventName={EventName} actorId={ActorId}",
                eventName,
                actorId);
        }
    }

    private static string NormalizeTrigger(string? trigger)
    {
        if (string.Equals(trigger, TriggerAuto, StringComparison.OrdinalIgnoreCase))
        {
            return TriggerAuto;
        }

        return TriggerManual;
    }

    private static string NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "unspecified";
        }

        var normalized = source.Trim();
        return normalized.Length > 80 ? normalized[..80] : normalized;
    }

    private bool IsAnyConnectionDue(IEnumerable<ConnectionSyncCandidate> candidates, DateTime now)
    {
        foreach (var candidate in candidates)
        {
            var dueDecision = GetDueDecision(candidate, now);
            if (dueDecision.IsDue)
            {
                return true;
            }
        }

        return false;
    }

    private (bool IsDue, string Reason) GetDueDecision(ConnectionSyncCandidate candidate, DateTime now)
    {
        if (GetProviderBackoffUntilUtc(candidate, now).HasValue)
        {
            return (false, "provider_backoff");
        }

        if (candidate.Status == BankConnectionStatuses.SyncPending)
        {
            return (false, "in_progress");
        }

        if (candidate.Status == BankConnectionStatuses.ConnectedPendingSync)
        {
            return (true, "initial_sync_required");
        }

        if (!candidate.LastSuccessfulSyncUtc.HasValue)
        {
            return (true, "never_synced");
        }

        if (now - candidate.LastSuccessfulSyncUtc.Value >= _autoSyncDueInterval)
        {
            return (true, "eligible");
        }

        return (false, "recent_sync");
    }

    private bool IsStaleSyncPending(ConnectionSyncCandidate candidate, DateTime now)
    {
        if (!string.Equals(candidate.Status, BankConnectionStatuses.SyncPending, StringComparison.Ordinal))
        {
            return false;
        }

        var pendingSinceUtc = candidate.LastSyncAttemptedUtc ?? candidate.UpdatedUtc;
        return now - pendingSinceUtc >= _syncPendingStaleAfter;
    }

    private DateTime? GetProviderBackoffUntilUtc(ConnectionSyncCandidate candidate, DateTime now)
    {
        if (!IsRateLimitErrorCode(candidate.LastErrorCode))
        {
            return null;
        }

        var backoffStartedAtUtc = candidate.LastSyncAttemptedUtc ?? candidate.UpdatedUtc;
        var backoffUntilUtc = backoffStartedAtUtc.Add(_providerRateLimitBackoff);
        return backoffUntilUtc > now ? backoffUntilUtc : null;
    }

    private static bool IsRateLimitErrorCode(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return false;
        }

        return errorCode.Contains("too_many_requests", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("request_limit_exceeded", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("rate_limit", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? MaxUtc(IEnumerable<DateTime?> values)
    {
        DateTime? result = null;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            if (!result.HasValue || value.Value > result.Value)
            {
                result = value.Value;
            }
        }

        return result;
    }

    private async Task<int> RecoverStaleSyncPendingConnectionAsync(
        Guid userId,
        ConnectionSyncCandidate candidate,
        string recoveredStatus,
        DateTime requestedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.OpenBankingConnections
                .Where(x =>
                    x.Id == candidate.Id
                    && x.UserId == userId
                    && x.Status == BankConnectionStatuses.SyncPending
                    && x.LastSyncAttemptedUtc == candidate.LastSyncAttemptedUtc)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, recoveredStatus)
                    .SetProperty(x => x.UpdatedUtc, requestedAtUtc),
                    cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(
                exception,
                "Set-based stale sync_pending recovery is not available; falling back to tracked recovery connectionId={ConnectionId}",
                candidate.Id);
        }

        var tracked = await dbContext.OpenBankingConnections
            .SingleOrDefaultAsync(
                x => x.Id == candidate.Id
                     && x.UserId == userId
                     && x.Status == BankConnectionStatuses.SyncPending,
                cancellationToken);

        if (tracked is null)
        {
            return 0;
        }

        if (tracked.LastSyncAttemptedUtc != candidate.LastSyncAttemptedUtc)
        {
            return 0;
        }

        tracked.Status = recoveredStatus;
        tracked.UpdatedUtc = requestedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return 1;
    }
}
