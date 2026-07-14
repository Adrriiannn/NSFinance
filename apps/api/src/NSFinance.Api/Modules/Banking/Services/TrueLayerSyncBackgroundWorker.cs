using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.Services.Models;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

public interface ITrueLayerSyncQueue
{
    ValueTask QueueInitialSyncAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);
}

public sealed class TrueLayerSyncBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BankingSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<TrueLayerSyncBackgroundWorker> logger) : BackgroundService, ITrueLayerSyncQueue
{
    private static readonly TimeSpan RecoverySweepInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ScheduledJobPollInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 16;
    private readonly TimeSpan _pollDelay = TimeSpan.FromMilliseconds(
        Math.Clamp(options.Value.DurableJobPollMilliseconds, 100, 30_000));
    private readonly TimeSpan _syncPendingStaleAfter = TimeSpan.FromMinutes(
        Math.Clamp(options.Value.StaleSyncPendingRecoveryMinutes, 1, 24 * 60));
    private readonly bool _unattendedSyncEnabled = options.Value.UnattendedSyncEnabled;
    private readonly TimeSpan _unattendedSyncInterval = TimeSpan.FromMinutes(
        Math.Clamp(options.Value.UnattendedSyncIntervalMinutes, 6 * 60, 24 * 60));
    private readonly TimeSpan _unattendedSyncSweepInterval = TimeSpan.FromMinutes(
        Math.Clamp(options.Value.UnattendedSyncSweepMinutes, 1, 60));

    public async ValueTask QueueInitialSyncAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
        var accepted = await jobStore.EnqueueAsync(
            userId,
            connectionId,
            BankingOperationTypes.InitialSync,
            cancellationToken);
        if (!accepted)
        {
            throw new InvalidOperationException("Initial bank sync connection was not found.");
        }

        logger.LogInformation(
            "Persisted initial bank sync request connectionId={ConnectionId} userId={UserId}",
            connectionId,
            userId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRecoverySweepUtc = timeProvider.GetUtcNow().UtcDateTime;
        var nextUnattendedSyncSweepUtc = nextRecoverySweepUtc;
        var nextScheduledJobPollUtc = nextRecoverySweepUtc;
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (nowUtc >= nextRecoverySweepUtc)
            {
                try
                {
                    await RecoverPendingSyncsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Pending initial bank sync recovery sweep failed; worker will continue.");
                }

                nextRecoverySweepUtc = nowUtc.Add(RecoverySweepInterval);
            }

            if (_unattendedSyncEnabled && nowUtc >= nextUnattendedSyncSweepUtc)
            {
                try
                {
                    await RecoverScheduledSyncsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Scheduled bank sync discovery sweep failed; worker will continue.");
                }

                nextUnattendedSyncSweepUtc = nowUtc.Add(_unattendedSyncSweepInterval);
            }

            var processedAny = false;
            try
            {
                processedAny = await ProcessDueJobsAsync(BankingOperationTypes.InitialSync, stoppingToken);
                if (_unattendedSyncEnabled && nowUtc >= nextScheduledJobPollUtc)
                {
                    processedAny = await ProcessDueJobsAsync(BankingOperationTypes.ScheduledSync, stoppingToken)
                        || processedAny;
                    nextScheduledJobPollUtc = nowUtc.Add(ScheduledJobPollInterval);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Durable initial bank sync worker iteration failed.");
            }

            if (!processedAny)
            {
                await Task.Delay(_pollDelay, stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessDueJobsAsync(CancellationToken cancellationToken)
    {
        return await ProcessDueJobsAsync(BankingOperationTypes.InitialSync, cancellationToken);
    }

    private async Task<bool> ProcessDueJobsAsync(
        string operationType,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> jobIds;
        await using (var scanScope = scopeFactory.CreateAsyncScope())
        {
            var jobStore = scanScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            jobIds = await jobStore.ListDueJobIdsAsync(
                operationType,
                BatchSize,
                cancellationToken);
        }

        foreach (var jobId in jobIds)
        {
            await ProcessJobAsync(jobId, operationType, cancellationToken);
        }

        return jobIds.Count > 0;
    }

    private async Task ProcessJobAsync(
        Guid jobId,
        string operationType,
        CancellationToken stoppingToken)
    {
        BankingOperationJobLease? lease;
        TimeSpan leaseDuration;
        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var jobStore = claimScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            lease = await jobStore.TryClaimAsync(
                jobId,
                operationType,
                stoppingToken);
            leaseDuration = jobStore.LeaseDuration;
        }

        if (lease is null)
        {
            return;
        }

        logger.LogInformation(
            "Claimed durable bank sync operation={Operation} attempt={Attempt} maxAttempts={MaxAttempts}",
            operationType,
            lease.AttemptCount,
            lease.MaxAttempts);

        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = BankingOperationLeaseHeartbeat.MaintainAsync(
            scopeFactory,
            lease,
            leaseDuration,
            workCancellation,
            logger,
            operationType == BankingOperationTypes.InitialSync
                ? "Initial bank sync"
                : "Scheduled bank sync",
            stoppingToken);
        ServiceResult<BankSyncResult>? result = null;
        Exception? crash = null;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var bankSyncService = scope.ServiceProvider.GetRequiredService<BankSyncService>();
            result = await bankSyncService.SyncConnectionAsync(
                lease.UserId,
                lease.ConnectionId,
                workCancellation.Token,
                trigger: lease.OperationType);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            crash = exception;
        }
        finally
        {
            workCancellation.Cancel();
            await BankingOperationLeaseHeartbeat.AwaitShutdownAsync(heartbeat);
        }

        if (crash is not null)
        {
            logger.LogError(
                crash,
                "Durable bank sync attempt crashed operation={Operation}.",
                lease.OperationType);
            await RecordFailureAsync(
                lease,
                $"{lease.OperationType}_worker_crashed",
                lease.OperationType == BankingOperationTypes.InitialSync,
                "Bank sync worker crashed.",
                stoppingToken);
            return;
        }

        if (result?.Succeeded == true)
        {
            await using var completeScope = scopeFactory.CreateAsyncScope();
            var jobStore = completeScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            var completed = await jobStore.MarkSucceededAsync(
                lease.JobId,
                lease.LeaseId,
                stoppingToken);
            if (completed)
            {
                logger.LogInformation(
                    "Durable bank sync completed operation={Operation} attempt={Attempt} status={Status}",
                    lease.OperationType,
                    lease.AttemptCount,
                    result.Value?.Status);
            }

            return;
        }

        var error = result?.Error;
        if (ShouldCancel(error))
        {
            await using var cancelScope = scopeFactory.CreateAsyncScope();
            var jobStore = cancelScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            await jobStore.MarkCancelledAsync(
                lease.JobId,
                lease.LeaseId,
                error?.Code ?? "initial_sync_cancelled",
                stoppingToken);
            return;
        }

        await RecordFailureAsync(
            lease,
            error?.Code ?? $"{lease.OperationType}_failed",
            ShouldRetryJob(lease.OperationType, error),
            error?.Message ?? "Bank sync failed.",
            stoppingToken);
    }

    private async Task RecordFailureAsync(
        BankingOperationJobLease lease,
        string errorCode,
        bool retryable,
        string errorReason,
        CancellationToken cancellationToken)
    {
        BankingOperationJobFailureOutcome outcome;
        await using (var failureScope = scopeFactory.CreateAsyncScope())
        {
            var jobStore = failureScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            outcome = await jobStore.MarkFailedAsync(
                lease.JobId,
                lease.LeaseId,
                errorCode,
                retryable,
                cancellationToken);
        }

        if (!outcome.Found)
        {
            logger.LogWarning(
                "Bank sync result was not persisted because its lease was no longer current operation={Operation}.",
                lease.OperationType);
            return;
        }

        await using var connectionScope = scopeFactory.CreateAsyncScope();
        var connectionService = connectionScope.ServiceProvider.GetRequiredService<BankConnectionService>();
        var connection = await connectionService.FindConnectionForUserAsync(
            lease.UserId,
            lease.ConnectionId,
            cancellationToken);
        if (connection is null)
        {
            return;
        }

        if (lease.OperationType == BankingOperationTypes.ScheduledSync && !outcome.WillRetry)
        {
            await connectionService.MarkSyncAttemptStartedAsync(connection, cancellationToken);
        }

        if (outcome.WillRetry)
        {
            if (errorCode is "bank_sync_in_progress" or "bank_sync_lease_lost")
            {
                logger.LogInformation(
                    "Bank sync deferred without changing connection state operation={Operation} attempt={Attempt} nextAttemptUtc={NextAttemptUtc} code={Code}",
                    lease.OperationType,
                    outcome.AttemptCount,
                    outcome.NextAttemptUtc,
                    errorCode);
                return;
            }

            if (connection.Status is BankConnectionStatuses.ReauthRequired
                or BankConnectionStatuses.Expired
                or BankConnectionStatuses.DisconnectPending
                or BankConnectionStatuses.DisconnectFailed
                or BankConnectionStatuses.Revoked)
            {
                return;
            }

            await connectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.ConnectedPendingSync,
                errorCode,
                "Initial sync will retry automatically.",
                cancellationToken);
            logger.LogWarning(
                "Bank sync scheduled for retry operation={Operation} attempt={Attempt} nextAttemptUtc={NextAttemptUtc} code={Code}",
                lease.OperationType,
                outcome.AttemptCount,
                outcome.NextAttemptUtc,
                errorCode);
            return;
        }

        if (connection.Status is BankConnectionStatuses.ConnectedPendingSync or BankConnectionStatuses.SyncPending)
        {
            await connectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                errorCode,
                errorReason,
                cancellationToken);
        }

        logger.LogWarning(
            "Bank sync reached terminal failure operation={Operation} attempt={Attempt} code={Code}",
            lease.OperationType,
            outcome.AttemptCount,
            errorCode);
    }

    internal async Task RecoverPendingSyncsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
        var staleSyncPendingThresholdUtc = timeProvider.GetUtcNow().UtcDateTime - _syncPendingStaleAfter;
        var pendingConnections = await BuildRecoveryQuery(dbContext, staleSyncPendingThresholdUtc)
            .ToListAsync(cancellationToken);
        foreach (var connection in pendingConnections)
        {
            await jobStore.EnqueueAsync(
                connection.UserId,
                connection.ConnectionId,
                BankingOperationTypes.InitialSync,
                cancellationToken);
        }

        if (pendingConnections.Count > 0)
        {
            logger.LogInformation(
                "Recovered {Count} persisted initial bank sync connection state(s) into the durable queue.",
                pendingConnections.Count);
        }
    }

    internal async Task RecoverScheduledSyncsAsync(CancellationToken cancellationToken)
    {
        if (!_unattendedSyncEnabled)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
        var dueBeforeUtc = timeProvider.GetUtcNow().UtcDateTime - _unattendedSyncInterval;
        var dueConnections = await BuildScheduledSyncQuery(dbContext, dueBeforeUtc)
            .ToListAsync(cancellationToken);
        foreach (var connection in dueConnections)
        {
            await jobStore.EnqueueAsync(
                connection.UserId,
                connection.ConnectionId,
                BankingOperationTypes.ScheduledSync,
                cancellationToken);
        }

        if (dueConnections.Count > 0)
        {
            logger.LogInformation(
                "Discovered {Count} connection(s) due for a bounded unattended bank sync.",
                dueConnections.Count);
        }
    }

    internal static IQueryable<PendingTrueLayerSyncRow> BuildRecoveryQuery(
        AppDbContext dbContext,
        DateTime staleSyncPendingThresholdUtc)
    {
        return dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(connection =>
                connection.ProviderName == BankingProviders.TrueLayer
                && (connection.Status == BankConnectionStatuses.ConnectedPendingSync
                    || (connection.Status == BankConnectionStatuses.SyncPending
                        && connection.UpdatedUtc <= staleSyncPendingThresholdUtc)))
            .OrderBy(connection => connection.UpdatedUtc)
            .ThenBy(connection => connection.Id)
            .Select(connection => new PendingTrueLayerSyncRow(
                connection.UserId,
                connection.Id,
                connection.Status))
            .Take(64);
    }

    internal static IQueryable<PendingTrueLayerSyncRow> BuildScheduledSyncQuery(
        AppDbContext dbContext,
        DateTime dueBeforeUtc)
    {
        return dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(connection =>
                connection.ProviderName == BankingProviders.TrueLayer
                && (connection.Status == BankConnectionStatuses.Connected
                    || connection.Status == BankConnectionStatuses.Synced
                    || connection.Status == BankConnectionStatuses.Failed)
                && connection.InitialBackfillCompletedUtc != null
                && connection.Token != null
                && !connection.Token.IsRevoked
                && connection.Token.EncryptedRefreshToken != null
                && connection.Token.EncryptedRefreshToken != string.Empty
                && (connection.LastSyncAttemptedUtc
                    ?? connection.LastSuccessfulSyncUtc
                    ?? connection.UpdatedUtc) <= dueBeforeUtc)
            .OrderBy(connection => connection.LastSyncAttemptedUtc
                ?? connection.LastSuccessfulSyncUtc
                ?? connection.UpdatedUtc)
            .ThenBy(connection => connection.Id)
            .Select(connection => new PendingTrueLayerSyncRow(
                connection.UserId,
                connection.Id,
                connection.Status))
            .Take(64);
    }

    internal static bool ShouldRetryJob(string operationType, ServiceError? error)
    {
        if (operationType == BankingOperationTypes.InitialSync)
        {
            return IsRetryable(error);
        }

        return operationType == BankingOperationTypes.ScheduledSync
            && error?.Code is "bank_sync_in_progress" or "bank_sync_lease_lost";
    }

    internal static bool IsRetryable(ServiceError? error)
    {
        if (error is null)
        {
            return true;
        }

        if (error.StatusCode is StatusCodes.Status408RequestTimeout
            or StatusCodes.Status429TooManyRequests
            or >= StatusCodes.Status500InternalServerError)
        {
            return true;
        }

        var code = error.Code.ToLowerInvariant();
        return code is "bank_sync_in_progress" or "bank_sync_lease_lost"
            || code.Contains("timeout", StringComparison.Ordinal)
            || code.Contains("temporar", StringComparison.Ordinal)
            || code.Contains("unavailable", StringComparison.Ordinal)
            || code.Contains("rate_limit", StringComparison.Ordinal);
    }

    private static bool ShouldCancel(ServiceError? error)
    {
        return error?.Code is "bank_connection_not_found"
            or "bank_connection_disconnected"
            or "bank_connection_disconnect_pending";
    }
}

internal sealed record PendingTrueLayerSyncRow(
    Guid UserId,
    Guid ConnectionId,
    string Status);
