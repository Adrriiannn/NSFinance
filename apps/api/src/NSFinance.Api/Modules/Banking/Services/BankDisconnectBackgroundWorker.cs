using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

public interface IBankDisconnectQueue
{
    ValueTask QueueDisconnectCleanupAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);
}

internal sealed record BankDisconnectWorkItem(Guid UserId, Guid ConnectionId);

public sealed class BankDisconnectBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BankDisconnectBackgroundWorker> logger) : BackgroundService, IBankDisconnectQueue
{
    private readonly Channel<BankDisconnectWorkItem> _queue = Channel.CreateUnbounded<BankDisconnectWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private int _queueDepth;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RequeuePendingDisconnectsAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    public ValueTask QueueDisconnectCleanupAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var queueDepth = Interlocked.Increment(ref _queueDepth);
        logger.LogInformation(
            "Queued bank disconnect cleanup for connectionId={ConnectionId} userId={UserId} queueDepth={QueueDepth}",
            connectionId,
            userId,
            queueDepth);

        try
        {
            return _queue.Writer.WriteAsync(new BankDisconnectWorkItem(userId, connectionId), cancellationToken);
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
            var connectionService = scope.ServiceProvider.GetRequiredService<BankConnectionService>();

            try
            {
                logger.LogInformation(
                    "Starting queued bank disconnect cleanup for connectionId={ConnectionId} userId={UserId} queueDepth={QueueDepth}",
                    workItem.ConnectionId,
                    workItem.UserId,
                    Math.Max(queueDepth, 0));

                await connectionService.RunDisconnectCleanupAsync(
                    workItem.UserId,
                    workItem.ConnectionId,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Stopping queued bank disconnect cleanup worker.");
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Queued bank disconnect cleanup crashed for connectionId={ConnectionId}",
                    workItem.ConnectionId);
            }
        }
    }

    private async Task RequeuePendingDisconnectsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingConnections = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.Status == BankConnectionStatuses.DisconnectPending)
            .Select(x => new { x.UserId, x.Id })
            .ToListAsync(cancellationToken);

        if (pendingConnections.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Requeueing {Count} pending bank disconnect cleanup item(s) during startup.",
            pendingConnections.Count);

        foreach (var pendingConnection in pendingConnections)
        {
            await QueueDisconnectCleanupAsync(
                pendingConnection.UserId,
                pendingConnection.Id,
                cancellationToken);
        }
    }
}
