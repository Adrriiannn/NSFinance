namespace NSFinance.Api.Modules.Banking.Services;

internal static class BankSyncExecutionLeaseHeartbeat
{
    public static async Task MaintainAsync(
        IServiceScopeFactory scopeFactory,
        BankSyncExecutionLease lease,
        TimeSpan leaseDuration,
        CancellationTokenSource workCancellation,
        ILogger logger,
        CancellationToken requestCancellation)
    {
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(10, leaseDuration.TotalSeconds / 3));
        try
        {
            while (!workCancellation.IsCancellationRequested && !requestCancellation.IsCancellationRequested)
            {
                await Task.Delay(heartbeatInterval, workCancellation.Token);
                await using var scope = scopeFactory.CreateAsyncScope();
                var leaseStore = scope.ServiceProvider.GetRequiredService<BankSyncExecutionLeaseStore>();
                var renewed = await leaseStore.RenewAsync(lease, workCancellation.Token);
                if (renewed)
                {
                    continue;
                }

                logger.LogError("Bank sync execution lease was lost; cancelling the active sync.");
                workCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (
            workCancellation.IsCancellationRequested || requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Bank sync execution lease heartbeat failed; cancelling the active sync.");
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
