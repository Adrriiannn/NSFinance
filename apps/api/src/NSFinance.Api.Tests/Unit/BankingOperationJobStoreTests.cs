using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class BankingOperationJobStoreTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnqueueAsync_CoalescesActiveJobAndRejectsAnotherUser()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext);
        var otherUserId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, ownerId);
        var timeProvider = new MutableTimeProvider(UtcNow);
        var store = CreateStore(dbContext, timeProvider);

        var first = await store.EnqueueAsync(
            ownerId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        var duplicate = await store.EnqueueAsync(
            ownerId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        var crossUser = await store.EnqueueAsync(
            otherUserId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);

        Assert.True(first);
        Assert.True(duplicate);
        Assert.False(crossUser);
        var job = Assert.Single(dbContext.BankingOperationJobs);
        Assert.Equal(BankingOperationJobStatuses.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(3, job.MaxAttempts);
    }

    [Fact]
    public async Task EnqueueAsync_RepairsActiveJobOwnershipMetadataFromTheConnectionOwner()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext);
        var staleUserId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, ownerId);
        var store = CreateStore(dbContext, new MutableTimeProvider(UtcNow));
        await store.EnqueueAsync(
            ownerId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        var job = Assert.Single(dbContext.BankingOperationJobs);
        job.UserId = staleUserId;
        job.MaxAttempts = 1;
        await dbContext.SaveChangesAsync();

        var enqueued = await store.EnqueueAsync(
            ownerId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);

        Assert.True(enqueued);
        Assert.Equal(ownerId, job.UserId);
        Assert.Equal(3, job.MaxAttempts);
    }

    [Fact]
    public async Task TryClaimAsync_AllowsOneActiveLeaseAndReclaimsItOnlyAfterExpiry()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, userId);
        var timeProvider = new MutableTimeProvider(UtcNow);
        var store = CreateStore(dbContext, timeProvider);
        await store.EnqueueAsync(
            userId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        var jobId = Assert.Single(dbContext.BankingOperationJobs).Id;

        var first = await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        var duplicate = await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        var reclaimed = await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.NotNull(reclaimed);
        Assert.NotEqual(first.LeaseId, reclaimed.LeaseId);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task MarkFailedAsync_UsesBoundedBackoffThenBecomesTerminal()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, userId);
        var timeProvider = new MutableTimeProvider(UtcNow);
        var store = CreateStore(dbContext, timeProvider);
        await store.EnqueueAsync(
            userId,
            connectionId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None);
        var jobId = Assert.Single(dbContext.BankingOperationJobs).Id;

        var firstLease = (await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None))!;
        var firstFailure = await store.MarkFailedAsync(
            jobId,
            firstLease.LeaseId,
            "provider_unavailable",
            true,
            CancellationToken.None);

        Assert.True(firstFailure.WillRetry);
        Assert.Equal(UtcNow.UtcDateTime.AddSeconds(15), firstFailure.NextAttemptUtc);
        Assert.Empty(await store.ListDueJobIdsAsync(
            BankingOperationTypes.InitialSync,
            10,
            CancellationToken.None));

        timeProvider.Advance(TimeSpan.FromSeconds(15));
        var secondLease = (await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None))!;
        var secondFailure = await store.MarkFailedAsync(
            jobId,
            secondLease.LeaseId,
            "provider_unavailable",
            true,
            CancellationToken.None);
        Assert.True(secondFailure.WillRetry);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime.AddSeconds(30), secondFailure.NextAttemptUtc);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var thirdLease = (await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.InitialSync,
            CancellationToken.None))!;
        var terminal = await store.MarkFailedAsync(
            jobId,
            thirdLease.LeaseId,
            "provider_unavailable",
            true,
            CancellationToken.None);

        Assert.False(terminal.WillRetry);
        var job = await dbContext.BankingOperationJobs.SingleAsync();
        Assert.Equal(BankingOperationJobStatuses.Failed, job.Status);
        Assert.Equal(3, job.AttemptCount);
        Assert.Equal("provider_unavailable", job.LastFailureCode);
        Assert.NotNull(job.FailedUtc);
    }

    [Fact]
    public async Task MarkSucceededAsync_RequiresTheCurrentLease()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, userId);
        var timeProvider = new MutableTimeProvider(UtcNow);
        var store = CreateStore(dbContext, timeProvider);
        await store.EnqueueAsync(
            userId,
            connectionId,
            BankingOperationTypes.DisconnectCleanup,
            CancellationToken.None);
        var jobId = Assert.Single(dbContext.BankingOperationJobs).Id;
        var lease = (await store.TryClaimAsync(
            jobId,
            BankingOperationTypes.DisconnectCleanup,
            CancellationToken.None))!;

        var stale = await store.MarkSucceededAsync(jobId, "different-lease", CancellationToken.None);
        var current = await store.MarkSucceededAsync(jobId, lease.LeaseId, CancellationToken.None);

        Assert.False(stale);
        Assert.True(current);
        var job = await dbContext.BankingOperationJobs.SingleAsync();
        Assert.Equal(BankingOperationJobStatuses.Completed, job.Status);
        Assert.Null(job.LeaseId);
        Assert.NotNull(job.CompletedUtc);
    }

    [Fact]
    public void DueQuery_TranslatesOwnershipStatusLeaseOrderingAndBound()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);
        var store = CreateStore(dbContext, new MutableTimeProvider(UtcNow));

        var sql = store.BuildDueQuery(BankingOperationTypes.InitialSync, UtcNow.UtcDateTime)
            .Take(12)
            .ToQueryString();

        Assert.Contains("BankingOperationJobs", sql, StringComparison.Ordinal);
        Assert.Contains("OpenBankingConnections", sql, StringComparison.Ordinal);
        Assert.Contains("LeaseExpiresUtc", sql, StringComparison.Ordinal);
        Assert.Contains("NextAttemptUtc", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_ReportsOnlyAggregateOperationalState()
    {
        await using var dbContext = CreateDbContext();
        dbContext.BankingOperationJobs.AddRange(
            Job(BankingOperationJobStatuses.Pending, UtcNow.AddMinutes(-10).UtcDateTime),
            Job(
                BankingOperationJobStatuses.Processing,
                UtcNow.UtcDateTime,
                UtcNow.AddMinutes(-1).UtcDateTime),
            Job(BankingOperationJobStatuses.Retry, UtcNow.AddMinutes(1).UtcDateTime),
            Job(BankingOperationJobStatuses.Failed, UtcNow.UtcDateTime));
        await dbContext.SaveChangesAsync();
        var store = CreateStore(dbContext, new MutableTimeProvider(UtcNow));

        var snapshot = await store.GetHealthSnapshotAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(1, snapshot.PendingCount);
        Assert.Equal(1, snapshot.ProcessingCount);
        Assert.Equal(1, snapshot.RetryCount);
        Assert.Equal(1, snapshot.FailedCount);
        Assert.Equal(1, snapshot.ExpiredLeaseCount);
        Assert.Equal(1, snapshot.OverdueCount);
        Assert.Equal(UtcNow.AddMinutes(-10).UtcDateTime, snapshot.OldestDueUtc);
    }

    [Fact]
    public void HealthQuery_TranslatesAggregateStatusAgeAndLeaseChecks()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);
        var store = CreateStore(dbContext, new MutableTimeProvider(UtcNow));

        var sql = store.BuildHealthQuery(
                UtcNow.UtcDateTime,
                UtcNow.AddMinutes(-5).UtcDateTime)
            .ToQueryString();

        Assert.Contains("BankingOperationJobs", sql, StringComparison.Ordinal);
        Assert.Contains("LeaseExpiresUtc", sql, StringComparison.Ordinal);
        Assert.Contains("NextAttemptUtc", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static BankingOperationJobStore CreateStore(
        AppDbContext dbContext,
        TimeProvider timeProvider)
    {
        return new BankingOperationJobStore(
            dbContext,
            timeProvider,
            Options.Create(new BankingSyncOptions
            {
                DurableJobMaxAttempts = 3,
                DurableJobLeaseSeconds = 60
            }),
            NullLogger<BankingOperationJobStore>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"banking-operation-job-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext)
    {
        var userId = Guid.NewGuid();
        var email = $"banking-job-{userId:N}@local";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Banking Job User",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-IE",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });
        await dbContext.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> SeedConnectionAsync(AppDbContext dbContext, Guid userId)
    {
        var connectionId = Guid.NewGuid();
        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "live",
            Status = BankConnectionStatuses.ConnectedPendingSync,
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        return connectionId;
    }

    private static BankingOperationJob Job(
        string status,
        DateTime nextAttemptUtc,
        DateTime? leaseExpiresUtc = null)
    {
        return new BankingOperationJob
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            OperationType = BankingOperationTypes.InitialSync,
            Status = status,
            AttemptCount = status == BankingOperationJobStatuses.Pending ? 0 : 1,
            MaxAttempts = 3,
            NextAttemptUtc = nextAttemptUtc,
            LeaseId = status == BankingOperationJobStatuses.Processing ? Guid.NewGuid().ToString("N") : null,
            LeaseExpiresUtc = leaseExpiresUtc,
            CreatedUtc = UtcNow.AddHours(-1).UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime
        };
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
