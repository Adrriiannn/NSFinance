using System.Threading.Channels;

namespace NSFinance.Api.Modules.Banking.Services;

public interface ITrueLayerSyncQueue
{
    ValueTask QueueInitialSyncAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);
}

internal sealed record TrueLayerSyncWorkItem(Guid UserId, Guid ConnectionId);

public sealed class TrueLayerSyncBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TrueLayerSyncBackgroundWorker> logger) : BackgroundService, ITrueLayerSyncQueue
{
    private readonly Channel<TrueLayerSyncWorkItem> _queue = Channel.CreateUnbounded<TrueLayerSyncWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private int _queueDepth;

    public ValueTask QueueInitialSyncAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var queueDepth = Interlocked.Increment(ref _queueDepth);
        logger.LogInformation(
            "Queued initial bank sync for connectionId={ConnectionId} userId={UserId} queueDepth={QueueDepth}",
            connectionId,
            userId,
            queueDepth);

        try
        {
            return _queue.Writer.WriteAsync(
                new TrueLayerSyncWorkItem(userId, connectionId),
                cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _queueDepth);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var queueDepth = Interlocked.Decrement(ref _queueDepth);
            using var scope = scopeFactory.CreateScope();
            var bankSyncService = scope.ServiceProvider.GetRequiredService<BankSyncService>();
            var bankConnectionService = scope.ServiceProvider.GetRequiredService<BankConnectionService>();

            try
            {
                logger.LogInformation(
                    "Starting queued initial bank sync for connectionId={ConnectionId} userId={UserId} queueDepth={QueueDepth}",
                    workItem.ConnectionId,
                    workItem.UserId,
                    Math.Max(queueDepth, 0));

                var result = await bankSyncService.SyncConnectionAsync(
                    workItem.UserId,
                    workItem.ConnectionId,
                    stoppingToken);

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
        }
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
}
