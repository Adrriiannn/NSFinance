namespace NSFinance.Api.Modules.Banking.Services;

internal static class BankingOperationLeaseHeartbeat
{
    public static async Task MaintainAsync(
        IServiceScopeFactory scopeFactory,
        BankingOperationJobLease lease,
        TimeSpan leaseDuration,
        CancellationTokenSource workCancellation,
        ILogger logger,
        string operationName,
        CancellationToken stoppingToken)
    {
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(10, leaseDuration.TotalSeconds / 3));
        try
        {
            while (!workCancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(heartbeatInterval, workCancellation.Token);
                await using var scope = scopeFactory.CreateAsyncScope();
                var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
                var renewed = await jobStore.RenewLeaseAsync(
                    lease.JobId,
                    lease.LeaseId,
                    workCancellation.Token);
                if (renewed)
                {
                    continue;
                }

                logger.LogError(
                    "{OperationName} lease was lost; cancelling the active attempt.",
                    operationName);
                workCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (workCancellation.IsCancellationRequested || stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "{OperationName} lease heartbeat failed; cancelling the active attempt.",
                operationName);
            workCancellation.Cancel();
        }
    }

    public static async Task AwaitShutdownAsync(Task heartbeat)
    {
        try
        {
            await heartbeat;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
