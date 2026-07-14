using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class BankDisconnectBackgroundWorkerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QueueDisconnectCleanupAsync_CoalescesDuplicateConnectionRequests()
    {
        await using var serviceProvider = BuildServiceProvider();
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        await AddConnectionAsync(serviceProvider, Connection(
            userId,
            connectionId,
            BankConnectionStatuses.DisconnectPending,
            UtcNow.AddMinutes(-1)));
        var worker = CreateWorker(serviceProvider);

        await worker.QueueDisconnectCleanupAsync(userId, connectionId);
        await worker.QueueDisconnectCleanupAsync(userId, connectionId);

        var job = Assert.Single(await LoadJobsAsync(serviceProvider));
        Assert.Equal(connectionId, job.ConnectionId);
        Assert.Equal(BankingOperationTypes.DisconnectCleanup, job.OperationType);
        Assert.Equal(BankingOperationJobStatuses.Pending, job.Status);
    }

    [Fact]
    public async Task RecoverPendingDisconnectsAsync_IsBoundedToPendingStateAndIdempotent()
    {
        await using var serviceProvider = BuildServiceProvider();
        var userId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        var revokedId = Guid.NewGuid();
        await AddConnectionAsync(
            serviceProvider,
            Connection(userId, pendingId, BankConnectionStatuses.DisconnectPending, UtcNow.AddMinutes(-3)),
            Connection(userId, failedId, BankConnectionStatuses.DisconnectFailed, UtcNow.AddMinutes(-2)),
            Connection(userId, revokedId, BankConnectionStatuses.Revoked, UtcNow.AddMinutes(-1)));
        var worker = CreateWorker(serviceProvider);

        await worker.RecoverPendingDisconnectsAsync(CancellationToken.None);
        await worker.RecoverPendingDisconnectsAsync(CancellationToken.None);

        var job = Assert.Single(await LoadJobsAsync(serviceProvider));
        Assert.Equal(pendingId, job.ConnectionId);
    }

    [Fact]
    public void RecoveryQuery_TranslatesStatusOrderingAndBound()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);

        var sql = BankDisconnectBackgroundWorker.BuildRecoveryQuery(dbContext).ToQueryString();

        Assert.Contains("OpenBankingConnections", sql, StringComparison.Ordinal);
        Assert.Contains("disconnect_pending", sql, StringComparison.Ordinal);
        Assert.Contains("UpdatedUtc", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"bank-disconnect-worker-{Guid.NewGuid():N}";
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new TestTimeProvider(UtcNow));
        services.AddSingleton<IOptions<BankingSyncOptions>>(Options.Create(new BankingSyncOptions
        {
            DurableJobMaxAttempts = 5,
            DurableJobLeaseSeconds = 120,
            DurableJobPollMilliseconds = 500
        }));
        services.AddScoped<BankingOperationJobStore>();
        return services.BuildServiceProvider();
    }

    private static BankDisconnectBackgroundWorker CreateWorker(ServiceProvider serviceProvider)
    {
        return new BankDisconnectBackgroundWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IOptions<BankingSyncOptions>>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            NullLogger<BankDisconnectBackgroundWorker>.Instance);
    }

    private static async Task AddConnectionAsync(
        ServiceProvider serviceProvider,
        params OpenBankingConnection[] connections)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.OpenBankingConnections.AddRange(connections);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<BankingOperationJob>> LoadJobsAsync(
        ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .BankingOperationJobs
            .AsNoTracking()
            .OrderBy(job => job.ConnectionId)
            .ToListAsync();
    }

    private static OpenBankingConnection Connection(
        Guid userId,
        Guid connectionId,
        string status,
        DateTimeOffset updatedUtc)
    {
        return new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "live",
            Status = status,
            CreatedUtc = updatedUtc.UtcDateTime.AddDays(-1),
            UpdatedUtc = updatedUtc.UtcDateTime
        };
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
