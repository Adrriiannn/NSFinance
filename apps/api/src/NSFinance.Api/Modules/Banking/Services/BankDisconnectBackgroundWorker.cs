using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

public interface IBankDisconnectQueue
{
    ValueTask QueueDisconnectCleanupAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);
}

public sealed class BankDisconnectBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BankingSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<BankDisconnectBackgroundWorker> logger) : BackgroundService, IBankDisconnectQueue
{
    private static readonly TimeSpan RecoverySweepInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 16;
    private readonly TimeSpan _pollDelay = TimeSpan.FromMilliseconds(
        Math.Clamp(options.Value.DurableJobPollMilliseconds, 100, 30_000));

    public async ValueTask QueueDisconnectCleanupAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
        var accepted = await jobStore.EnqueueAsync(
            userId,
            connectionId,
            BankingOperationTypes.DisconnectCleanup,
            cancellationToken);
        if (!accepted)
        {
            throw new InvalidOperationException("Bank disconnect connection was not found.");
        }

        logger.LogInformation(
            "Persisted bank disconnect cleanup request connectionId={ConnectionId} userId={UserId}",
            connectionId,
            userId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRecoverySweepUtc = timeProvider.GetUtcNow().UtcDateTime;
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (nowUtc >= nextRecoverySweepUtc)
            {
                try
                {
                    await RecoverPendingDisconnectsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Pending bank disconnect recovery sweep failed; worker will continue.");
                }

                nextRecoverySweepUtc = nowUtc.Add(RecoverySweepInterval);
            }

            var processedAny = false;
            try
            {
                processedAny = await ProcessDueJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Durable bank disconnect worker iteration failed.");
            }

            if (!processedAny)
            {
                await Task.Delay(_pollDelay, stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessDueJobsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> jobIds;
        await using (var scanScope = scopeFactory.CreateAsyncScope())
        {
            var jobStore = scanScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            jobIds = await jobStore.ListDueJobIdsAsync(
                BankingOperationTypes.DisconnectCleanup,
                BatchSize,
                cancellationToken);
        }

        foreach (var jobId in jobIds)
        {
            await ProcessJobAsync(jobId, cancellationToken);
        }

        return jobIds.Count > 0;
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        BankingOperationJobLease? lease;
        TimeSpan leaseDuration;
        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var jobStore = claimScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            lease = await jobStore.TryClaimAsync(
                jobId,
                BankingOperationTypes.DisconnectCleanup,
                stoppingToken);
            leaseDuration = jobStore.LeaseDuration;
        }

        if (lease is null)
        {
            return;
        }

        logger.LogInformation(
            "Claimed durable bank disconnect attempt={Attempt} maxAttempts={MaxAttempts}",
            lease.AttemptCount,
            lease.MaxAttempts);

        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = BankingOperationLeaseHeartbeat.MaintainAsync(
            scopeFactory,
            lease,
            leaseDuration,
            workCancellation,
            logger,
            "Bank disconnect cleanup",
            stoppingToken);
        string? connectionStatus = null;
        Exception? crash = null;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var connectionService = scope.ServiceProvider.GetRequiredService<BankConnectionService>();
            await connectionService.RunDisconnectCleanupAsync(
                lease.UserId,
                lease.ConnectionId,
                workCancellation.Token);
            var connection = await connectionService.FindConnectionForUserAsync(
                lease.UserId,
                lease.ConnectionId,
                workCancellation.Token);
            connectionStatus = connection?.Status;
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
            logger.LogError(crash, "Durable bank disconnect cleanup attempt crashed.");
            await RecordFailureAsync(
                lease,
                "disconnect_cleanup_worker_crashed",
                stoppingToken);
            return;
        }

        if (connectionStatus == BankConnectionStatuses.Revoked)
        {
            await using var completeScope = scopeFactory.CreateAsyncScope();
            var jobStore = completeScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            await jobStore.MarkSucceededAsync(lease.JobId, lease.LeaseId, stoppingToken);
            logger.LogInformation(
                "Durable bank disconnect cleanup completed attempt={Attempt}",
                lease.AttemptCount);
            return;
        }

        if (connectionStatus is null)
        {
            await using var cancelScope = scopeFactory.CreateAsyncScope();
            var jobStore = cancelScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            await jobStore.MarkCancelledAsync(
                lease.JobId,
                lease.LeaseId,
                "bank_connection_not_found",
                stoppingToken);
            return;
        }

        if (connectionStatus == BankConnectionStatuses.DisconnectFailed)
        {
            await RecordFailureAsync(lease, "disconnect_cleanup_failed", stoppingToken);
            return;
        }

        await using var unexpectedScope = scopeFactory.CreateAsyncScope();
        var unexpectedStore = unexpectedScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
        await unexpectedStore.MarkCancelledAsync(
            lease.JobId,
            lease.LeaseId,
            "disconnect_state_changed",
            stoppingToken);
    }

    private async Task RecordFailureAsync(
        BankingOperationJobLease lease,
        string failureCode,
        CancellationToken cancellationToken)
    {
        BankingOperationJobFailureOutcome outcome;
        await using (var failureScope = scopeFactory.CreateAsyncScope())
        {
            var jobStore = failureScope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            outcome = await jobStore.MarkFailedAsync(
                lease.JobId,
                lease.LeaseId,
                failureCode,
                true,
                cancellationToken);
        }

        if (!outcome.Found)
        {
            logger.LogWarning("Bank disconnect result was not persisted because its lease was no longer current.");
            return;
        }

        if (!outcome.WillRetry)
        {
            logger.LogWarning(
                "Bank disconnect cleanup reached terminal failure attempt={Attempt} code={Code}",
                outcome.AttemptCount,
                failureCode);
            return;
        }

        await using var connectionScope = scopeFactory.CreateAsyncScope();
        var connectionService = connectionScope.ServiceProvider.GetRequiredService<BankConnectionService>();
        var connection = await connectionService.FindConnectionForUserAsync(
            lease.UserId,
            lease.ConnectionId,
            cancellationToken);
        if (connection is not null && connection.Status == BankConnectionStatuses.DisconnectFailed)
        {
            await connectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.DisconnectPending,
                failureCode,
                "Disconnect cleanup will retry automatically.",
                cancellationToken);
        }

        logger.LogWarning(
            "Bank disconnect cleanup scheduled for retry attempt={Attempt} nextAttemptUtc={NextAttemptUtc} code={Code}",
            outcome.AttemptCount,
            outcome.NextAttemptUtc,
            failureCode);
    }

    internal async Task RecoverPendingDisconnectsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
        var pendingConnections = await BuildRecoveryQuery(dbContext)
            .ToListAsync(cancellationToken);
        foreach (var connection in pendingConnections)
        {
            await jobStore.EnqueueAsync(
                connection.UserId,
                connection.ConnectionId,
                BankingOperationTypes.DisconnectCleanup,
                cancellationToken);
        }

        if (pendingConnections.Count > 0)
        {
            logger.LogInformation(
                "Recovered {Count} persisted disconnect connection state(s) into the durable queue.",
                pendingConnections.Count);
        }
    }

    internal static IQueryable<PendingBankDisconnectRow> BuildRecoveryQuery(AppDbContext dbContext)
    {
        return dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(connection => connection.Status == BankConnectionStatuses.DisconnectPending)
            .OrderBy(connection => connection.UpdatedUtc)
            .ThenBy(connection => connection.Id)
            .Select(connection => new PendingBankDisconnectRow(connection.UserId, connection.Id))
            .Take(64);
    }
}

internal sealed record PendingBankDisconnectRow(Guid UserId, Guid ConnectionId);
