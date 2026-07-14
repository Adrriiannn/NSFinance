using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class AccountBalanceReadServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetBalancesAsync_ProviderSnapshots_SelectsLatestStableSnapshot()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, "EUR");
        var linkedAccountId = await SeedLinkedAccountAsync(dbContext, seeded.UserId, seeded.AccountId, "EUR");
        var capturedUtc = UtcNow.UtcDateTime.AddHours(-1);
        dbContext.BankBalanceSnapshots.AddRange(
            CreateSnapshot(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                linkedAccountId,
                100m,
                90m,
                20m,
                "EUR",
                capturedUtc),
            CreateSnapshot(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                linkedAccountId,
                200m,
                180m,
                30m,
                "EUR",
                capturedUtc));
        await dbContext.SaveChangesAsync();

        var balances = await CreateService(dbContext, seeded.UserId).GetBalancesAsync(
            [seeded.AccountId],
            CancellationToken.None);

        var balance = balances[seeded.AccountId];
        Assert.Equal(200m, balance.Current);
        Assert.Equal(180m, balance.Available);
        Assert.Equal(30m, balance.Overdraft);
        Assert.Equal("EUR", balance.Currency);
        Assert.Equal("provider_snapshot", balance.Source);
        Assert.Equal("fresh", balance.Freshness);
        Assert.Equal(capturedUtc, balance.AsOfUtc);
        Assert.Empty(balance.Exclusions);
    }

    [Fact]
    public async Task GetBalancesAsync_LinkedAccountWithoutSnapshot_DoesNotUseLedgerFallback()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, "EUR");
        await SeedLinkedAccountAsync(dbContext, seeded.UserId, seeded.AccountId, "EUR");
        dbContext.Transactions.Add(CreateTransaction(seeded.AccountId, 999m, "EUR", UtcNow.UtcDateTime));
        await dbContext.SaveChangesAsync();

        var balances = await CreateService(dbContext, seeded.UserId).GetBalancesAsync(
            [seeded.AccountId],
            CancellationToken.None);

        var balance = balances[seeded.AccountId];
        Assert.Equal("unavailable", balance.Source);
        Assert.Null(balance.Current);
        Assert.Null(balance.Available);
        Assert.Equal("unknown", balance.Freshness);
        Assert.Contains("provider_snapshot_missing", balance.Exclusions);
    }

    [Fact]
    public async Task GetBalancesAsync_ManualAccount_UsesOnlyAccountCurrencyLedger()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, "EUR");
        var older = UtcNow.UtcDateTime.AddDays(-2);
        var latest = UtcNow.UtcDateTime.AddHours(-2);
        dbContext.Transactions.AddRange(
            CreateTransaction(seeded.AccountId, 100m, "EUR", older),
            CreateTransaction(seeded.AccountId, -25m, "EUR", latest),
            CreateTransaction(seeded.AccountId, 500m, "USD", UtcNow.UtcDateTime.AddHours(-1)));
        await dbContext.SaveChangesAsync();

        var balances = await CreateService(dbContext, seeded.UserId).GetBalancesAsync(
            [seeded.AccountId],
            CancellationToken.None);

        var balance = balances[seeded.AccountId];
        Assert.Equal(75m, balance.Current);
        Assert.Equal("manual_ledger", balance.Source);
        Assert.Equal("current", balance.Freshness);
        Assert.Equal(latest, balance.AsOfUtc);
        Assert.Contains("non_account_currency_transactions_excluded", balance.Exclusions);
    }

    [Theory]
    [InlineData(-25, "stale", null)]
    [InlineData(1, "unknown", "future_capture_timestamp")]
    public async Task GetBalancesAsync_ProviderTimestamp_ControlsFreshness(
        int capturedHourOffset,
        string expectedFreshness,
        string? expectedExclusion)
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, "EUR");
        var linkedAccountId = await SeedLinkedAccountAsync(dbContext, seeded.UserId, seeded.AccountId, "EUR");
        dbContext.BankBalanceSnapshots.Add(CreateSnapshot(
            Guid.NewGuid(),
            linkedAccountId,
            10m,
            8m,
            null,
            "EUR",
            UtcNow.UtcDateTime.AddHours(capturedHourOffset)));
        await dbContext.SaveChangesAsync();

        var balance = (await CreateService(dbContext, seeded.UserId).GetBalancesAsync(
            [seeded.AccountId],
            CancellationToken.None))[seeded.AccountId];

        Assert.Equal(expectedFreshness, balance.Freshness);
        if (expectedExclusion is null)
        {
            Assert.DoesNotContain("future_capture_timestamp", balance.Exclusions);
        }
        else
        {
            Assert.Contains(expectedExclusion, balance.Exclusions);
        }
    }

    [Fact]
    public async Task GetBalancesAsync_RequestedOtherUserAccount_IsNotReturned()
    {
        await using var dbContext = CreateDbContext();
        var primary = await SeedAccountAsync(dbContext, "EUR");
        var otherUser = await SeedAccountAsync(dbContext, "EUR");

        var balances = await CreateService(dbContext, primary.UserId).GetBalancesAsync(
            [primary.AccountId, otherUser.AccountId],
            CancellationToken.None);

        Assert.Single(balances);
        Assert.True(balances.ContainsKey(primary.AccountId));
        Assert.False(balances.ContainsKey(otherUser.AccountId));
    }

    [Fact]
    public void BuildPortfolioBalance_GroupsCurrenciesWithoutMixedTotal()
    {
        var portfolio = AccountBalanceReadService.BuildPortfolioBalance(
        [
            CreateBalance(10m, "EUR", "provider_snapshot"),
            CreateBalance(20m, "EUR", "manual_ledger"),
            CreateBalance(5m, "USD", "provider_snapshot"),
            CreateBalance(null, "EUR", "unavailable")
        ]);

        Assert.Equal(3, portfolio.IncludedAccountCount);
        Assert.Equal(1, portfolio.ExcludedAccountCount);
        Assert.True(portfolio.HasMultipleCurrencies);
        Assert.Collection(
            portfolio.ByCurrency,
            eur =>
            {
                Assert.Equal("EUR", eur.Currency);
                Assert.Equal(30m, eur.Amount);
                Assert.Equal(2, eur.AccountCount);
                Assert.Equal("current", eur.Basis);
            },
            usd =>
            {
                Assert.Equal("USD", usd.Currency);
                Assert.Equal(5m, usd.Amount);
                Assert.Equal(1, usd.AccountCount);
            });
    }

    [Fact]
    public void BuildBalanceQuery_NpgsqlProvider_TranslatesSnapshotAndLedgerProjection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);
        var accountId = Guid.NewGuid();
        var service = CreateService(dbContext, Guid.NewGuid());

        var sql = service.BuildBalanceQuery([accountId]).ToQueryString();

        Assert.Contains("BankBalanceSnapshots", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CapturedUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountService_GetAccounts_AttachesStructuredBalance()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, "EUR");
        dbContext.Transactions.Add(CreateTransaction(
            seeded.AccountId,
            42m,
            "EUR",
            UtcNow.UtcDateTime.AddHours(-1)));
        await dbContext.SaveChangesAsync();
        var currentUser = new TestCurrentUserProvider(seeded.UserId);
        var balanceService = new AccountBalanceReadService(
            dbContext,
            currentUser,
            new TestTimeProvider(UtcNow));
        var accountService = new AccountService(
            dbContext,
            currentUser,
            balanceService,
            NullLogger<AccountService>.Instance);

        var account = Assert.Single(await accountService.GetAccountsAsync(CancellationToken.None));

        Assert.NotNull(account.Balance);
        Assert.Equal(42m, account.Balance.Current);
        Assert.Equal("manual_ledger", account.Balance.Source);
    }

    [Fact]
    public async Task DashboardService_MixedCurrencies_ReturnsGroupedPortfolioBalance()
    {
        await using var dbContext = CreateDbContext();
        var euro = await SeedAccountAsync(dbContext, "EUR");
        var usd = await SeedAccountAsync(dbContext, "USD", euro.UserId);
        dbContext.Transactions.AddRange(
            CreateTransaction(euro.AccountId, 30m, "EUR", UtcNow.UtcDateTime.AddHours(-1)),
            CreateTransaction(usd.AccountId, 20m, "USD", UtcNow.UtcDateTime.AddHours(-1)));
        await dbContext.SaveChangesAsync();
        var currentUser = new TestCurrentUserProvider(euro.UserId);
        var balanceService = new AccountBalanceReadService(
            dbContext,
            currentUser,
            new TestTimeProvider(UtcNow));
        var dashboardService = new DashboardService(
            dbContext,
            currentUser,
            balanceService,
            new ExpenseTaxonomyService());

        var result = await dashboardService.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(50m, result.TotalBalance);
        Assert.NotNull(result.PortfolioBalance);
        Assert.True(result.PortfolioBalance.HasMultipleCurrencies);
        Assert.Collection(
            result.PortfolioBalance.ByCurrency,
            eur =>
            {
                Assert.Equal("EUR", eur.Currency);
                Assert.Equal(30m, eur.Amount);
            },
            dollars =>
            {
                Assert.Equal("USD", dollars.Currency);
                Assert.Equal(20m, dollars.Amount);
            });
        Assert.All(result.AccountPreview, account => Assert.NotNull(account.Balance));
    }

    private static AccountBalanceReadService CreateService(AppDbContext dbContext, Guid userId)
    {
        return new AccountBalanceReadService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new TestTimeProvider(UtcNow));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"account-balance-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedAccountAsync(
        AppDbContext dbContext,
        string currency,
        Guid? existingUserId = null)
    {
        var userId = existingUserId ?? Guid.NewGuid();
        var accountId = Guid.NewGuid();
        if (!existingUserId.HasValue)
        {
            var email = $"balance-{userId:N}@local";
            dbContext.Users.Add(new User
            {
                Id = userId,
                PrimaryEmail = email,
                NormalizedEmail = email,
                DisplayName = "Balance Tester",
                Status = "active",
                OnboardingStatus = "profile_created",
                Role = "user",
                CreatedUtc = UtcNow.UtcDateTime,
                UpdatedUtc = UtcNow.UtcDateTime,
                EmailVerified = true,
                Timezone = "UTC",
                Locale = "en-IE",
                PreferredCurrency = currency,
                PlanTier = "standard"
            });
        }
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Test account",
            Type = "Current",
            Currency = currency,
            CreatedUtc = UtcNow.UtcDateTime.AddDays(-10)
        });
        await dbContext.SaveChangesAsync();
        return (userId, accountId);
    }

    private static async Task<Guid> SeedLinkedAccountAsync(
        AppDbContext dbContext,
        Guid userId,
        Guid financialAccountId,
        string currency)
    {
        var connectionId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();
        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = "TrueLayer",
            ProviderEnvironment = "live",
            Status = "connected",
            CreatedUtc = UtcNow.UtcDateTime.AddDays(-2),
            UpdatedUtc = UtcNow.UtcDateTime
        });
        dbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = $"provider-{linkedAccountId:N}",
            DisplayName = "Linked account",
            Currency = currency,
            FinancialAccountId = financialAccountId,
            CreatedUtc = UtcNow.UtcDateTime.AddDays(-2),
            UpdatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        return linkedAccountId;
    }

    private static BankBalanceSnapshot CreateSnapshot(
        Guid id,
        Guid linkedAccountId,
        decimal? current,
        decimal? available,
        decimal? overdraft,
        string currency,
        DateTime capturedUtc)
    {
        return new BankBalanceSnapshot
        {
            Id = id,
            LinkedBankAccountId = linkedAccountId,
            Current = current,
            Available = available,
            Overdraft = overdraft,
            Currency = currency,
            CapturedUtc = capturedUtc,
            RawPayloadJson = "{}"
        };
    }

    private static Transaction CreateTransaction(
        Guid accountId,
        decimal amount,
        string currency,
        DateTime createdUtc)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = currency,
            Description = "Balance entry",
            BookedAtUtc = createdUtc,
            CreatedUtc = createdUtc
        };
    }

    private static AccountBalanceDto CreateBalance(decimal? current, string currency, string source)
    {
        return new AccountBalanceDto(
            current,
            null,
            null,
            currency,
            source,
            UtcNow.UtcDateTime,
            source == "unavailable" ? "unknown" : "fresh",
            []);
    }

    private sealed class TestCurrentUserProvider(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid resolvedUserId)
        {
            resolvedUserId = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = Guid.Empty;
            return false;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
