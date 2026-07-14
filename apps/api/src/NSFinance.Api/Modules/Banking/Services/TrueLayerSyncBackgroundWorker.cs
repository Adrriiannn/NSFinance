using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

public interface ITrueLayerSyncQueue
{
    ValueTask QueueInitialSyncAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);
}

internal sealed record TrueLayerSyncWorkItem(Guid UserId, Guid ConnectionId);

public sealed class TrueLayerSyncBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BankingSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<TrueLayerSyncBackgroundWorker> logger) : BackgroundService, ITrueLayerSyncQueue
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RecoverySweepInterval = TimeSpan.FromSeconds(30);
    private readonly Channel<TrueLayerSyncWorkItem> _queue = Channel.CreateUnbounded<TrueLayerSyncWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _queuedKeys = new(StringComparer.Ordinal);
    private readonly TimeSpan _syncPendingStaleAfter = TimeSpan.FromMinutes(
        Math.Clamp(options.Value.StaleSyncPendingRecoveryMinutes, 1, 24 * 60));

    internal int PendingQueueCount => _queuedKeys.Count;

    public async ValueTask QueueInitialSyncAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var key = BuildQueueKey(userId, connectionId);
        if (!_queuedKeys.TryAdd(key, 0))
        {
            logger.LogDebug(
                "Skipped duplicate initial bank sync queue request connectionId={ConnectionId} userId={UserId}",
                connectionId,
                userId);
            return;
        }

        logger.LogInformation(
            "Queued initial bank sync for connectionId={ConnectionId} userId={UserId} queueDepth={QueueDepth}",
            connectionId,
            userId,
            _queuedKeys.Count);

        try
        {
            await _queue.Writer.WriteAsync(
                new TrueLayerSyncWorkItem(userId, connectionId),
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
        var nextRecoverySweepUtc = timeProvider.GetUtcNow().UtcDateTime;
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

            if (!_queue.Reader.TryRead(out var workItem))
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            var key = BuildQueueKey(workItem.UserId, workItem.ConnectionId);
            using var scope = scopeFactory.CreateScope();
            var bankSyncService = scope.ServiceProvider.GetRequiredService<BankSyncService>();
            var bankConnectionService = scope.ServiceProvider.GetRequiredService<BankConnectionService>();

            try
            {
                logger.LogInformation(
                    "Starting queued initial bank sync for connectionId={ConnectionId} userId={UserId} queueDepth={QueueDepth}",
                    workItem.ConnectionId,
                    workItem.UserId,
                    Math.Max(_queuedKeys.Count - 1, 0));

                var result = await bankSyncService.SyncConnectionAsync(
                    workItem.UserId,
                    workItem.ConnectionId,
                    stoppingToken,
                    trigger: "initial_sync");

                if (!result.Succeeded)
                {
                    logger.LogWarning(
                        "Queued initial bank sync failed for connectionId={ConnectionId} code={Code}",
                        workItem.ConnectionId,
                        result.Error?.Code);

                    await MarkQueueFailureIfConnectionStillPendingAsync(
                        bankConnectionService,
                        workItem,
                        result.Error?.Code ?? "initial_sync_failed",
                        result.Error?.Message ?? "Initial sync failed.",
                        stoppingToken);
                    continue;
                }

                logger.LogInformation(
                    "Queued initial bank sync completed for connectionId={ConnectionId} status={Status}",
                    workItem.ConnectionId,
                    result.Value?.Status);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Stopping queued initial bank sync worker.");
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Queued initial bank sync crashed for connectionId={ConnectionId}",
                    workItem.ConnectionId);

                await MarkQueueFailureIfConnectionStillPendingAsync(
                    bankConnectionService,
                    workItem,
                    "initial_sync_worker_crashed",
                    "Initial sync worker crashed. Try syncing again from the app.",
                    stoppingToken);
            }
            finally
            {
                _queuedKeys.TryRemove(key, out _);
            }
        }
    }

    internal async Task RecoverPendingSyncsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staleSyncPendingThresholdUtc = timeProvider.GetUtcNow().UtcDateTime - _syncPendingStaleAfter;
        var pendingConnections = await BuildRecoveryQuery(dbContext, staleSyncPendingThresholdUtc)
            .ToListAsync(cancellationToken);

        if (pendingConnections.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Recovering {Count} persisted initial bank sync item(s) queueDepth={QueueDepth}",
            pendingConnections.Count,
            _queuedKeys.Count);

        foreach (var connection in pendingConnections)
        {
            await QueueInitialSyncAsync(connection.UserId, connection.ConnectionId, cancellationToken);
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

    private async Task MarkQueueFailureIfConnectionStillPendingAsync(
        BankConnectionService bankConnectionService,
        TrueLayerSyncWorkItem workItem,
        string errorCode,
        string errorReason,
        CancellationToken cancellationToken)
    {
        var connection = await bankConnectionService.FindConnectionForUserAsync(
            workItem.UserId,
            workItem.ConnectionId,
            cancellationToken);

        if (connection is null)
        {
            return;
        }

        if (connection.Status is not (BankConnectionStatuses.ConnectedPendingSync or BankConnectionStatuses.SyncPending))
        {
            return;
        }

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.Failed,
            errorCode,
            errorReason,
            cancellationToken);
    }

    internal bool IsQueued(Guid userId, Guid connectionId)
    {
        return _queuedKeys.ContainsKey(BuildQueueKey(userId, connectionId));
    }

    private static string BuildQueueKey(Guid userId, Guid connectionId)
    {
        return $"{userId:N}:{connectionId:N}";
    }
}

internal sealed record PendingTrueLayerSyncRow(
    Guid UserId,
    Guid ConnectionId,
    string Status);
