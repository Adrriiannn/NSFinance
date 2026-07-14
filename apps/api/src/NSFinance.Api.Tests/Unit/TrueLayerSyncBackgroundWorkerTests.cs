using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
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

        await worker.QueueInitialSyncAsync(userId, connectionId);
        await worker.QueueInitialSyncAsync(userId, connectionId);

        var jobs = await LoadJobsAsync(serviceProvider);
        var job = Assert.Single(jobs);
        Assert.Equal(connectionId, job.ConnectionId);
        Assert.Equal(BankingOperationJobStatuses.Pending, job.Status);
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

        var jobs = await LoadJobsAsync(serviceProvider);
        Assert.Equal(2, jobs.Count);
        Assert.Contains(jobs, job => job.ConnectionId == pendingId);
        Assert.Contains(jobs, job => job.ConnectionId == staleId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == freshId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == wrongProviderId);
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

        var job = Assert.Single(await LoadJobsAsync(serviceProvider));
        Assert.Equal(connectionId, job.ConnectionId);
        Assert.Equal(BankingOperationJobStatuses.Pending, job.Status);
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

    [Fact]
    public async Task RecoverScheduledSyncsAsync_QueuesOnlyDueTokenizedActiveConnections()
    {
        await using var serviceProvider = BuildServiceProvider();
        var userId = Guid.NewGuid();
        var dueSyncedId = Guid.NewGuid();
        var dueNeverSyncedId = Guid.NewGuid();
        var dueFailedId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        var revokedTokenId = Guid.NewGuid();
        var missingTokenId = Guid.NewGuid();
        var missingInitialBackfillId = Guid.NewGuid();
        var reauthRequiredId = Guid.NewGuid();
        var initialSyncId = Guid.NewGuid();
        var otherProviderId = Guid.NewGuid();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OpenBankingConnections.AddRange(
                ConnectionWithToken(
                    userId,
                    dueSyncedId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Synced,
                    UtcNow.AddHours(-13),
                    lastSuccessfulSyncUtc: UtcNow.AddHours(-13)),
                ConnectionWithToken(
                    userId,
                    dueNeverSyncedId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Connected,
                    UtcNow.AddHours(-13)),
                ConnectionWithToken(
                    userId,
                    dueFailedId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Failed,
                    UtcNow.AddHours(-13),
                    lastSyncAttemptedUtc: UtcNow.AddHours(-13)),
                ConnectionWithToken(
                    userId,
                    recentId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Synced,
                    UtcNow.AddHours(-2),
                    lastSuccessfulSyncUtc: UtcNow.AddHours(-2)),
                ConnectionWithToken(
                    userId,
                    revokedTokenId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Synced,
                    UtcNow.AddHours(-13),
                    tokenRevoked: true),
                Connection(
                    userId,
                    missingTokenId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Synced,
                    UtcNow.AddHours(-13)),
                ConnectionWithToken(
                    userId,
                    missingInitialBackfillId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.Synced,
                    UtcNow.AddHours(-13),
                    initialBackfillCompleted: false),
                ConnectionWithToken(
                    userId,
                    reauthRequiredId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.ReauthRequired,
                    UtcNow.AddHours(-13)),
                ConnectionWithToken(
                    userId,
                    initialSyncId,
                    BankingProviders.TrueLayer,
                    BankConnectionStatuses.ConnectedPendingSync,
                    UtcNow.AddHours(-13)),
                ConnectionWithToken(
                    userId,
                    otherProviderId,
                    "other",
                    BankConnectionStatuses.Synced,
                    UtcNow.AddHours(-13)));
            await dbContext.SaveChangesAsync();
        }
        var worker = CreateWorker(serviceProvider);

        await worker.RecoverScheduledSyncsAsync(CancellationToken.None);
        await worker.RecoverScheduledSyncsAsync(CancellationToken.None);

        var jobs = await LoadJobsAsync(serviceProvider);
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, job => Assert.Equal(BankingOperationTypes.ScheduledSync, job.OperationType));
        Assert.Contains(jobs, job => job.ConnectionId == dueSyncedId);
        Assert.Contains(jobs, job => job.ConnectionId == dueNeverSyncedId);
        Assert.Contains(jobs, job => job.ConnectionId == dueFailedId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == recentId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == revokedTokenId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == missingTokenId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == missingInitialBackfillId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == reauthRequiredId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == initialSyncId);
        Assert.DoesNotContain(jobs, job => job.ConnectionId == otherProviderId);
    }

    [Fact]
    public async Task RecoverScheduledSyncsAsync_WhenDisabled_DoesNotQueueWork()
    {
        await using var serviceProvider = BuildServiceProvider(new BankingSyncOptions
        {
            UnattendedSyncEnabled = false,
            UnattendedSyncIntervalMinutes = 720,
            UnattendedSyncSweepMinutes = 15
        });
        var userId = Guid.NewGuid();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OpenBankingConnections.Add(ConnectionWithToken(
                userId,
                Guid.NewGuid(),
                BankingProviders.TrueLayer,
                BankConnectionStatuses.Synced,
                UtcNow.AddHours(-13)));
            await dbContext.SaveChangesAsync();
        }
        var worker = CreateWorker(serviceProvider);

        await worker.RecoverScheduledSyncsAsync(CancellationToken.None);

        Assert.Empty(await LoadJobsAsync(serviceProvider));
    }

    [Fact]
    public void ScheduledSyncQuery_TranslatesTokenStatusDueTimeOrderingAndBound()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);

        var sql = TrueLayerSyncBackgroundWorker.BuildScheduledSyncQuery(
            dbContext,
            UtcNow.UtcDateTime.AddHours(-12)).ToQueryString();

        Assert.Contains("OpenBankingConnections", sql, StringComparison.Ordinal);
        Assert.Contains("BankConnectionTokens", sql, StringComparison.Ordinal);
        Assert.Contains("EncryptedRefreshToken", sql, StringComparison.Ordinal);
        Assert.Contains("IsRevoked", sql, StringComparison.Ordinal);
        Assert.Contains("LastSyncAttemptedUtc", sql, StringComparison.Ordinal);
        Assert.Contains("LastSuccessfulSyncUtc", sql, StringComparison.Ordinal);
        Assert.Contains("InitialBackfillCompletedUtc", sql, StringComparison.Ordinal);
        Assert.Contains("synced", sql, StringComparison.Ordinal);
        Assert.Contains("failed", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScheduledSyncRetryPolicy_RetriesOnlyLeaseContention()
    {
        var providerUnavailable = new ServiceError(
            "Provider unavailable.",
            "truelayer_accounts_fetch_failed",
            StatusCodes.Status503ServiceUnavailable);
        var rateLimited = new ServiceError(
            "Provider rate limit reached.",
            "provider_too_many_requests",
            StatusCodes.Status429TooManyRequests);
        var leaseContention = new ServiceError(
            "Another sync owns the lease.",
            "bank_sync_in_progress",
            StatusCodes.Status409Conflict);

        Assert.False(TrueLayerSyncBackgroundWorker.ShouldRetryJob(
            BankingOperationTypes.ScheduledSync,
            providerUnavailable));
        Assert.False(TrueLayerSyncBackgroundWorker.ShouldRetryJob(
            BankingOperationTypes.ScheduledSync,
            rateLimited));
        Assert.True(TrueLayerSyncBackgroundWorker.ShouldRetryJob(
            BankingOperationTypes.ScheduledSync,
            leaseContention));
        Assert.True(TrueLayerSyncBackgroundWorker.ShouldRetryJob(
            BankingOperationTypes.InitialSync,
            providerUnavailable));
    }

    [Theory]
    [InlineData("initial_sync", true, "initial_sync_success")]
    [InlineData("scheduled_sync", false, "scheduled_sync_failed")]
    [InlineData("auto_sync", true, "auto_sync_success")]
    [InlineData("manual_sync", false, "manual_sync_failed")]
    public void SyncAuditEventName_PreservesTriggerProvenance(
        string trigger,
        bool succeeded,
        string expected)
    {
        Assert.Equal(expected, BankSyncService.BuildSyncAuditEventName(trigger, succeeded));
    }

    [Theory]
    [InlineData("bank_sync_in_progress")]
    [InlineData("bank_sync_lease_lost")]
    public void LeaseContention_IsRetryable(string code)
    {
        var error = new ServiceError("Retry safely.", code, StatusCodes.Status409Conflict);

        Assert.True(TrueLayerSyncBackgroundWorker.IsRetryable(error));
    }

    private static ServiceProvider BuildServiceProvider(BankingSyncOptions? configuredOptions = null)
    {
        var services = new ServiceCollection();
        var databaseName = $"true-layer-sync-worker-{Guid.NewGuid():N}";
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new TestTimeProvider(UtcNow));
        services.AddSingleton<IOptions<BankingSyncOptions>>(Options.Create(configuredOptions ?? new BankingSyncOptions
        {
            StaleSyncPendingRecoveryMinutes = 10,
            DurableJobMaxAttempts = 5,
            DurableJobLeaseSeconds = 120,
            DurableJobPollMilliseconds = 500,
            UnattendedSyncEnabled = true,
            UnattendedSyncIntervalMinutes = 720,
            UnattendedSyncSweepMinutes = 15
        }));
        services.AddScoped<BankingOperationJobStore>();
        return services.BuildServiceProvider();
    }

    private static TrueLayerSyncBackgroundWorker CreateWorker(ServiceProvider serviceProvider)
    {
        return new TrueLayerSyncBackgroundWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IOptions<BankingSyncOptions>>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            NullLogger<TrueLayerSyncBackgroundWorker>.Instance);
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

    private static OpenBankingConnection ConnectionWithToken(
        Guid userId,
        Guid connectionId,
        string provider,
        string status,
        DateTimeOffset updatedUtc,
        DateTimeOffset? lastSuccessfulSyncUtc = null,
        DateTimeOffset? lastSyncAttemptedUtc = null,
        bool tokenRevoked = false,
        bool initialBackfillCompleted = true)
    {
        var connection = Connection(userId, connectionId, provider, status, updatedUtc);
        connection.LastSuccessfulSyncUtc = lastSuccessfulSyncUtc?.UtcDateTime;
        connection.LastSyncAttemptedUtc = lastSyncAttemptedUtc?.UtcDateTime;
        connection.InitialBackfillCompletedUtc = initialBackfillCompleted
            ? updatedUtc.UtcDateTime.AddHours(-1)
            : null;
        connection.Token = new BankConnectionToken
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            EncryptedRefreshToken = "protected-refresh-token",
            AccessTokenExpiresUtc = updatedUtc.UtcDateTime.AddHours(1),
            TokenObtainedUtc = updatedUtc.UtcDateTime,
            IsRevoked = tokenRevoked,
            RevokedUtc = tokenRevoked ? updatedUtc.UtcDateTime : null
        };
        return connection;
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
