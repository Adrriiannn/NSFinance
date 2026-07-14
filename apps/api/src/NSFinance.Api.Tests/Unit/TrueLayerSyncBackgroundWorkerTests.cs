using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class TrueLayerSyncBackgroundWorkerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QueueInitialSyncAsync_CoalescesDuplicateConnectionRequests()
    {
        await using var serviceProvider = BuildServiceProvider();
        var worker = CreateWorker(serviceProvider);
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        await worker.QueueInitialSyncAsync(userId, connectionId);
        await worker.QueueInitialSyncAsync(userId, connectionId);

        Assert.Equal(1, worker.PendingQueueCount);
        Assert.True(worker.IsQueued(userId, connectionId));
    }

    [Fact]
    public async Task RecoverPendingSyncsAsync_QueuesPersistedInitialAndStaleInterruptedWork()
    {
        await using var serviceProvider = BuildServiceProvider();
        var userId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        var wrongProviderId = Guid.NewGuid();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OpenBankingConnections.AddRange(
                Connection(userId, pendingId, BankingProviders.TrueLayer, BankConnectionStatuses.ConnectedPendingSync, UtcNow.AddMinutes(-1)),
                Connection(userId, staleId, BankingProviders.TrueLayer, BankConnectionStatuses.SyncPending, UtcNow.AddMinutes(-11)),
                Connection(userId, freshId, BankingProviders.TrueLayer, BankConnectionStatuses.SyncPending, UtcNow.AddMinutes(-9)),
                Connection(userId, wrongProviderId, "other", BankConnectionStatuses.ConnectedPendingSync, UtcNow.AddMinutes(-20)),
                Connection(userId, Guid.NewGuid(), BankingProviders.TrueLayer, BankConnectionStatuses.Synced, UtcNow.AddMinutes(-20)));
            await dbContext.SaveChangesAsync();
        }
        var worker = CreateWorker(serviceProvider);

        await worker.RecoverPendingSyncsAsync(CancellationToken.None);

        Assert.Equal(2, worker.PendingQueueCount);
        Assert.True(worker.IsQueued(userId, pendingId));
        Assert.True(worker.IsQueued(userId, staleId));
        Assert.False(worker.IsQueued(userId, freshId));
        Assert.False(worker.IsQueued(userId, wrongProviderId));
    }

    [Fact]
    public async Task RecoverPendingSyncsAsync_IsIdempotentAcrossRepeatedSweeps()
    {
        await using var serviceProvider = BuildServiceProvider();
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OpenBankingConnections.Add(Connection(
                userId,
                connectionId,
                BankingProviders.TrueLayer,
                BankConnectionStatuses.ConnectedPendingSync,
                UtcNow.AddMinutes(-1)));
            await dbContext.SaveChangesAsync();
        }
        var worker = CreateWorker(serviceProvider);

        await worker.RecoverPendingSyncsAsync(CancellationToken.None);
        await worker.RecoverPendingSyncsAsync(CancellationToken.None);

        Assert.Equal(1, worker.PendingQueueCount);
        Assert.True(worker.IsQueued(userId, connectionId));
    }

    [Fact]
    public void RecoveryQuery_TranslatesProviderStatusStalenessOrderingAndBound()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);

        var sql = TrueLayerSyncBackgroundWorker.BuildRecoveryQuery(
            dbContext,
            UtcNow.UtcDateTime.AddMinutes(-10)).ToQueryString();

        Assert.Contains("OpenBankingConnections", sql, StringComparison.Ordinal);
        Assert.Contains("ProviderName", sql, StringComparison.Ordinal);
        Assert.Contains("connected_pending_sync", sql, StringComparison.Ordinal);
        Assert.Contains("sync_pending", sql, StringComparison.Ordinal);
        Assert.Contains("UpdatedUtc", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"true-layer-sync-worker-{Guid.NewGuid():N}";
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static TrueLayerSyncBackgroundWorker CreateWorker(ServiceProvider serviceProvider)
    {
        return new TrueLayerSyncBackgroundWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BankingSyncOptions { StaleSyncPendingRecoveryMinutes = 10 }),
            new TestTimeProvider(UtcNow),
            NullLogger<TrueLayerSyncBackgroundWorker>.Instance);
    }

    private static OpenBankingConnection Connection(
        Guid userId,
        Guid connectionId,
        string provider,
        string status,
        DateTimeOffset updatedUtc)
    {
        return new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = provider,
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
