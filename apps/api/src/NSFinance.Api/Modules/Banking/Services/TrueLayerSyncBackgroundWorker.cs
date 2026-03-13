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

    public ValueTask QueueInitialSyncAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Queued initial bank sync for connectionId={ConnectionId} userId={UserId}",
            connectionId,
            userId);

        return _queue.Writer.WriteAsync(
            new TrueLayerSyncWorkItem(userId, connectionId),
            cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var bankSyncService = scope.ServiceProvider.GetRequiredService<BankSyncService>();

            try
            {
                logger.LogInformation(
                    "Starting queued initial bank sync for connectionId={ConnectionId} userId={UserId}",
                    workItem.ConnectionId,
                    workItem.UserId);

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
            }
        }
    }
}
