using Microsoft.Extensions.Options;

namespace NSFinance.Worker;

public class Worker(ILogger<Worker> logger, IOptions<WorkerOptions> options) : BackgroundService
{
    private readonly int _pollIntervalSeconds = Math.Max(options.Value.PollIntervalSeconds, 5);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "NSFinance worker started. Placeholder jobs registered: Imports, Sync, Insights. Poll interval: {PollIntervalSeconds}s",
            _pollIntervalSeconds);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker heartbeat at {TimestampUtc}", DateTime.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
        }
    }
}
