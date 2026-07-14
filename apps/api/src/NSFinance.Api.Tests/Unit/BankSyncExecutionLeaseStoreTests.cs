using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class BankSyncExecutionLeaseStoreTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryAcquireAsync_AllowsOnlyTheOwnerAndOneCurrentLease()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext);
        var otherUserId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, ownerId);
        var store = CreateStore(dbContext, new MutableTimeProvider(UtcNow));

        var first = await store.TryAcquireAsync(ownerId, connectionId, CancellationToken.None);
        var duplicate = await store.TryAcquireAsync(ownerId, connectionId, CancellationToken.None);
        var crossUser = await store.TryAcquireAsync(otherUserId, connectionId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.Null(crossUser);
        var connection = await dbContext.OpenBankingConnections.SingleAsync();
        Assert.Equal(first.LeaseId, connection.SyncLeaseId);
        Assert.Equal(UtcNow.AddSeconds(60).UtcDateTime, connection.SyncLeaseExpiresUtc);
    }

    [Fact]
    public async Task ExpiredLease_CanBeReclaimedAndOnlyItsCurrentOwnerCanReleaseIt()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext);
        var connectionId = await SeedConnectionAsync(dbContext, ownerId);
        var timeProvider = new MutableTimeProvider(UtcNow);
        var store = CreateStore(dbContext, timeProvider);

        var first = Assert.IsType<BankSyncExecutionLease>(
            await store.TryAcquireAsync(ownerId, connectionId, CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        var reclaimed = Assert.IsType<BankSyncExecutionLease>(
            await store.TryAcquireAsync(ownerId, connectionId, CancellationToken.None));

        Assert.NotEqual(first.LeaseId, reclaimed.LeaseId);
        Assert.False(await store.ReleaseAsync(first, CancellationToken.None));
        Assert.True(await store.RenewAsync(reclaimed, CancellationToken.None));
        Assert.True(await store.ReleaseAsync(reclaimed, CancellationToken.None));
        Assert.NotNull(await store.TryAcquireAsync(ownerId, connectionId, CancellationToken.None));
    }

    [Fact]
    public void ClaimQuery_TranslatesOwnershipAndExpiryGuard()
    {
        using var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=nsfinance_query_translation;Username=test;Password=test")
            .Options);
        var store = CreateStore(dbContext, new MutableTimeProvider(UtcNow));

        var sql = store.BuildClaimQuery(Guid.NewGuid(), Guid.NewGuid(), UtcNow.UtcDateTime)
            .ToQueryString();

        Assert.Contains("OpenBankingConnections", sql, StringComparison.Ordinal);
        Assert.Contains("UserId", sql, StringComparison.Ordinal);
        Assert.Contains("SyncLeaseId", sql, StringComparison.Ordinal);
        Assert.Contains("SyncLeaseExpiresUtc", sql, StringComparison.Ordinal);
    }

    private static BankSyncExecutionLeaseStore CreateStore(
        AppDbContext dbContext,
        TimeProvider timeProvider)
    {
        return new BankSyncExecutionLeaseStore(
            dbContext,
            timeProvider,
            Options.Create(new BankingSyncOptions
            {
                SyncExecutionLeaseSeconds = 60
            }));
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"bank-sync-execution-lease-tests-{Guid.NewGuid():N}")
            .Options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext)
    {
        var userId = Guid.NewGuid();
        var email = $"bank-sync-lease-{userId:N}@local";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Bank Sync Lease User",
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
            Status = BankConnectionStatuses.Connected,
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        return connectionId;
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
