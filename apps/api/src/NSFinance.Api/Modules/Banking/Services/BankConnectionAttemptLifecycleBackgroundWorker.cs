using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankConnectionAttemptLifecycleBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BankConnectionAttemptOptions> options,
    ILogger<BankConnectionAttemptLifecycleBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSweepIterationAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = Math.Clamp(options.Value.SweepIntervalSeconds, 15, 600);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunSweepIterationAsync(stoppingToken);
        }
    }

    private async Task RunSweepIterationAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var attemptService = scope.ServiceProvider.GetRequiredService<BankConnectionAttemptService>();

        try
        {
            var batchSize = Math.Clamp(options.Value.ExpiryBatchSize, 1, 256);
            var staleProcessingMinutes = Math.Clamp(options.Value.StaleProcessingExpiryMinutes, 15, 24 * 7);
            var result = await attemptService.SweepLifecycleAsync(
                batchSize,
                TimeSpan.FromMinutes(staleProcessingMinutes),
                cancellationToken);

            if (result.ExpiredCount > 0 || result.SupersededCount > 0)
            {
                logger.LogInformation(
                    "Bank connection attempt lifecycle sweep changed state expiredCount={ExpiredCount} supersededCount={SupersededCount}",
                    result.ExpiredCount,
                    result.SupersededCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Bank connection attempt lifecycle sweep failed; worker will continue.");
        }
    }
}
