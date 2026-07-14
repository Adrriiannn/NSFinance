using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankingOperationJobHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<BankingOperationJobHealthCheck> logger) : IHealthCheck
{
    private static readonly TimeSpan OverdueAfter = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var jobStore = scope.ServiceProvider.GetRequiredService<BankingOperationJobStore>();
            var snapshot = await jobStore.GetHealthSnapshotAsync(OverdueAfter, cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["pending"] = snapshot.PendingCount,
                ["processing"] = snapshot.ProcessingCount,
                ["retry"] = snapshot.RetryCount,
                ["failed"] = snapshot.FailedCount,
                ["expiredLeases"] = snapshot.ExpiredLeaseCount,
                ["overdue"] = snapshot.OverdueCount,
                ["oldestDueUtc"] = snapshot.OldestDueUtc?.ToString("O") ?? "none"
            };

            if (snapshot.FailedCount > 0
                || snapshot.ExpiredLeaseCount > 0
                || snapshot.OverdueCount > 0)
            {
                logger.LogWarning(
                    "Banking job health degraded pending={Pending} processing={Processing} retry={Retry} failed={Failed} expiredLeases={ExpiredLeases} overdue={Overdue} oldestDueUtc={OldestDueUtc}",
                    snapshot.PendingCount,
                    snapshot.ProcessingCount,
                    snapshot.RetryCount,
                    snapshot.FailedCount,
                    snapshot.ExpiredLeaseCount,
                    snapshot.OverdueCount,
                    snapshot.OldestDueUtc);
                return HealthCheckResult.Degraded("Banking operation jobs require attention.", data: data);
            }

            return HealthCheckResult.Healthy("Banking operation jobs are current.", data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Banking operation job health query failed.");
            return HealthCheckResult.Unhealthy("Banking operation job health is unavailable.");
        }
    }
}
