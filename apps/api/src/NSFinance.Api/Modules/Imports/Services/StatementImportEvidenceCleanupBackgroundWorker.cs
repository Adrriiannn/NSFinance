namespace NSFinance.Api.Modules.Imports.Services;

public sealed class StatementImportEvidenceCleanupBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<StatementImportEvidenceCleanupBackgroundWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cleanupService = scope.ServiceProvider
                    .GetRequiredService<StatementImportEvidenceCleanupService>();
                var purgedCount = await cleanupService.PurgeExpiredAsync(stoppingToken);
                if (purgedCount > 0)
                {
                    logger.LogInformation(
                        "Purged expired statement import evidence for {PurgedRowCount} rows.",
                        purgedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Statement import evidence cleanup did not complete; it will retry on the next interval.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
