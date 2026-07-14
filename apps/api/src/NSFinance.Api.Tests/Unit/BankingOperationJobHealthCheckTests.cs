using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class BankingOperationJobHealthCheckTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckHealthAsync_EmptyQueueIsHealthyAndAggregateOnly()
    {
        await using var serviceProvider = BuildServiceProvider();
        var check = CreateCheck(serviceProvider);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0, result.Data["pending"]);
        Assert.DoesNotContain("user", result.Data.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", result.Data.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_FailedOrOverdueWorkIsDegradedWithoutIdentifiers()
    {
        await using var serviceProvider = BuildServiceProvider();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.BankingOperationJobs.AddRange(
                Job(BankingOperationJobStatuses.Failed, UtcNow.UtcDateTime),
                Job(BankingOperationJobStatuses.Pending, UtcNow.AddMinutes(-6).UtcDateTime));
            await dbContext.SaveChangesAsync();
        }
        var check = CreateCheck(serviceProvider);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1, result.Data["failed"]);
        Assert.Equal(1, result.Data["overdue"]);
        Assert.DoesNotContain(result.Data.Values, value =>
            value?.ToString()?.Contains("-", StringComparison.Ordinal) == true
            && Guid.TryParse(value.ToString(), out _));
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"banking-job-health-{Guid.NewGuid():N}";
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new TestTimeProvider(UtcNow));
        services.AddSingleton<IOptions<BankingSyncOptions>>(Options.Create(new BankingSyncOptions()));
        services.AddScoped<BankingOperationJobStore>();
        return services.BuildServiceProvider();
    }

    private static BankingOperationJobHealthCheck CreateCheck(ServiceProvider serviceProvider)
    {
        return new BankingOperationJobHealthCheck(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BankingOperationJobHealthCheck>.Instance);
    }

    private static BankingOperationJob Job(string status, DateTime nextAttemptUtc)
    {
        return new BankingOperationJob
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            OperationType = BankingOperationTypes.InitialSync,
            Status = status,
            AttemptCount = 1,
            MaxAttempts = 5,
            NextAttemptUtc = nextAttemptUtc,
            CreatedUtc = UtcNow.AddHours(-1).UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime,
            FailedUtc = status == BankingOperationJobStatuses.Failed ? UtcNow.UtcDateTime : null
        };
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
