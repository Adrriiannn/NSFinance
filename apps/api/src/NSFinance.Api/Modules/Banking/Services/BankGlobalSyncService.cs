using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.Services.Models;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankGlobalSyncService(
    AppDbContext dbContext,
    BankSyncService bankSyncService,
    IAuditService auditService,
    ILogger<BankGlobalSyncService> logger)
{
    private const string TriggerManual = "manual";
    private const string TriggerAuto = "auto";
    private static readonly TimeSpan ManualSyncCooldown = TimeSpan.FromHours(1);
    private static readonly TimeSpan AutoSyncDueInterval = TimeSpan.FromHours(1);

    private static readonly HashSet<string> ManualEligibleStatuses =
    [
        BankConnectionStatuses.ConnectedPendingSync,
        BankConnectionStatuses.Connected,
        BankConnectionStatuses.Synced,
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
        string? ProviderDisplayName,
        DateTime? LastSuccessfulSyncUtc,
        DateTime UpdatedUtc);

    public async Task<BankGlobalSyncResult> ExecuteAsync(
        Guid userId,
        string? trigger,
        string? source,
        CancellationToken cancellationToken)
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
                    x.ProviderDisplayName,
                    x.LastSuccessfulSyncUtc,
                    x.UpdatedUtc))
                .ToListAsync(cancellationToken);

            var eligibleStatuses = normalizedTrigger == TriggerAuto
                ? AutoEligibleStatuses
                : ManualEligibleStatuses;

            var eligibleCandidates = connectionCandidates
                .Where(x => eligibleStatuses.Contains(x.Status))
                .ToList();
            var dueNow = IsAnyConnectionDue(eligibleCandidates, requestedAtUtc);
            var lastSuccessfulSyncUtc = MaxUtc(eligibleCandidates.Select(x => x.LastSuccessfulSyncUtc));

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
                    lastSuccessfulSyncUtc);
            }

            if (normalizedTrigger == TriggerAuto && !dueNow)
            {
                logger.LogInformation(
                    "Global banking sync skipped as not due userId={UserId} trigger={Trigger} eligibleConnectionCount={EligibleConnectionCount}",
                    userId,
                    normalizedTrigger,
                    eligibleCandidates.Count);

                return CreateSkippedResult(
                    trigger: normalizedTrigger,
                    outcome: "skipped_not_due",
                    requestedAtUtc,
                    dueNow,
                    cooldownRemainingSeconds: 0,
                    cooldownUntilUtc: null,
                    eligibleConnectionCount: eligibleCandidates.Count,
                    lastSuccessfulSyncUtc);
            }

            if (normalizedTrigger == TriggerManual)
            {
                var cooldownState = await GetManualCooldownStateSafeAsync(userId, requestedAtUtc, cancellationToken);
                if (cooldownState.RemainingSeconds > 0)
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
                        lastSuccessfulSyncUtc);
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
                    eligibleConnectionCount = eligibleCandidates.Count
                },
                cancellationToken);

            var connectionResults = new List<BankGlobalSyncConnectionResult>(eligibleCandidates.Count);
            var changedConnectionCount = 0;
            var noChangeConnectionCount = 0;
            var failedConnectionCount = 0;
            var skippedConnectionCount = 0;

            foreach (var candidate in eligibleCandidates)
            {
                if (candidate.Status == BankConnectionStatuses.SyncPending)
                {
                    skippedConnectionCount++;
                    connectionResults.Add(new BankGlobalSyncConnectionResult(
                        candidate.Id,
                        candidate.ProviderDisplayName,
                        candidate.Status,
                        Outcome: "skipped_sync_in_progress",
                        AccountsSynced: 0,
                        BalancesSynced: 0,
                        TransactionsImported: 0,
                        SyncedAtUtc: null,
                        DataChanged: false,
                        ErrorCode: null,
                        ErrorMessage: "Sync already in progress for this connection."));
                    continue;
                }

                ServiceResult<BankSyncResult> syncResult;
                try
                {
                    syncResult = await bankSyncService.SyncConnectionAsync(userId, candidate.Id, cancellationToken);
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
                        candidate.Status,
                        Outcome: "failed",
                        AccountsSynced: 0,
                        BalancesSynced: 0,
                        TransactionsImported: 0,
                        SyncedAtUtc: null,
                        DataChanged: false,
                        ErrorCode: "sync_unexpected_exception",
                        ErrorMessage: "Unexpected sync error. Please retry."));
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
                        ErrorCode: null,
                        ErrorMessage: null));
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
                    candidate.Status,
                    skipped ? "skipped_unavailable" : "failed",
                    AccountsSynced: 0,
                    BalancesSynced: 0,
                    TransactionsImported: 0,
                    SyncedAtUtc: null,
                    DataChanged: false,
                    ErrorCode: error.Code,
                    ErrorMessage: error.Message));
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

            return new BankGlobalSyncResult(
                Trigger: normalizedTrigger,
                Outcome: "completed",
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
        DateTime? lastSuccessfulSyncUtc)
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
            Connections: []);
    }

    private async Task<(int RemainingSeconds, DateTime? CooldownUntilUtc)> GetManualCooldownStateAsync(
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
            return (0, null);
        }

        var cooldownUntilUtc = lastManualRequestUtc.Value.Add(ManualSyncCooldown);
        if (cooldownUntilUtc <= now)
        {
            return (0, cooldownUntilUtc);
        }

        var remainingSeconds = (int)Math.Ceiling((cooldownUntilUtc - now).TotalSeconds);
        return (Math.Max(1, remainingSeconds), cooldownUntilUtc);
    }

    private async Task<(int RemainingSeconds, DateTime? CooldownUntilUtc)> GetManualCooldownStateSafeAsync(
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
            return (0, null);
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

    private static bool IsAnyConnectionDue(IEnumerable<ConnectionSyncCandidate> candidates, DateTime now)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Status == BankConnectionStatuses.SyncPending)
            {
                continue;
            }

            if (candidate.Status == BankConnectionStatuses.ConnectedPendingSync)
            {
                return true;
            }

            if (!candidate.LastSuccessfulSyncUtc.HasValue)
            {
                return true;
            }

            if (now - candidate.LastSuccessfulSyncUtc.Value >= AutoSyncDueInterval)
            {
                return true;
            }
        }

        return false;
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
}
