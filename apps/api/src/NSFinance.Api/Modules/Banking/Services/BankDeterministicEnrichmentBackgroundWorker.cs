using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;

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

public sealed class BankDeterministicEnrichmentBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BankDeterministicEnrichmentBackgroundWorker> logger) : BackgroundService, IBankDeterministicEnrichmentQueue
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PendingSweepInterval = TimeSpan.FromSeconds(45);

    private readonly Channel<BankDeterministicEnrichmentWorkItem> _queue = Channel.CreateUnbounded<BankDeterministicEnrichmentWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _queuedKeys = new(StringComparer.Ordinal);

    public ValueTask QueueConnectionAsync(
        Guid userId,
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var key = BuildQueueKey(userId, connectionId);
        if (!_queuedKeys.TryAdd(key, 0))
        {
            return ValueTask.CompletedTask;
        }

        logger.LogInformation(
            "Queued deterministic enrichment connectionId={ConnectionId} userId={UserId} reason={Reason} queueDepth={QueueDepth}",
            connectionId,
            userId,
            reason,
            _queuedKeys.Count);

        return _queue.Writer.WriteAsync(
            new BankDeterministicEnrichmentWorkItem(userId, connectionId, reason),
            cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextSweepAtUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow >= nextSweepAtUtc)
            {
                await EnqueuePendingConnectionsAsync(stoppingToken);
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

            try
            {
                var result = await bankSyncService.RunDeterministicEnrichmentAsync(
                    workItem.UserId,
                    workItem.ConnectionId,
                    "background_queue",
                    stoppingToken);

                if (!result.Succeeded)
                {
                    logger.LogWarning(
                        "Deterministic enrichment batch failed connectionId={ConnectionId} userId={UserId} code={Code}",
                        workItem.ConnectionId,
                        workItem.UserId,
                        result.Error?.Code);
                    continue;
                }

                logger.LogInformation(
                    "Deterministic enrichment batch processed connectionId={ConnectionId} userId={UserId} inProgress={InProgress} completed={Completed} progressPercent={ProgressPercent} rowsEvaluated={RowsEvaluated} rowsRemaining={RowsRemaining}",
                    workItem.ConnectionId,
                    workItem.UserId,
                    result.Value.HistoricalEnrichmentInProgress,
                    result.Value.HistoricalEnrichmentCompleted,
                    result.Value.HistoricalEnrichmentProgressPercent,
                    result.Value.RowsEvaluated,
                    result.Value.RowsRemaining);

                if (result.Value.HistoricalEnrichmentInProgress)
                {
                    await QueueConnectionAsync(
                        workItem.UserId,
                        workItem.ConnectionId,
                        "continue_historical_backfill",
                        stoppingToken);
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

        var pendingConnections = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x =>
                x.NeedsHistoricalReclassification
                && x.Status != BankConnectionStatuses.DisconnectPending
                && x.Status != BankConnectionStatuses.DisconnectFailed
                && x.Status != BankConnectionStatuses.Revoked)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new { x.UserId, x.Id })
            .Take(32)
            .ToListAsync(cancellationToken);

        foreach (var connection in pendingConnections)
        {
            await QueueConnectionAsync(
                connection.UserId,
                connection.Id,
                "periodic_pending_scan",
                cancellationToken);
        }
    }

    private static string BuildQueueKey(Guid userId, Guid connectionId)
    {
        return $"{userId:N}:{connectionId:N}";
    }
}
