using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Insights.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class InsightPeriodsTests
{
    [Fact]
    public async Task MonthlyPeriods_GroupByCurrencyAndMonth_WithHonestTotals()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext, "EUR");
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 10, 12, 0, 0, DateTimeKind.Utc);
        var previousMonth = currentMonth.AddMonths(-1);

        dbContext.Transactions.AddRange(
            CreateTransaction(seeded.AccountId, -40m, "EUR", previousMonth),
            CreateTransaction(seeded.AccountId, -15.5m, "EUR", previousMonth.AddDays(2)),
            CreateTransaction(seeded.AccountId, 1200m, "EUR", previousMonth.AddDays(1)),
            CreateTransaction(seeded.AccountId, -25m, "EUR", currentMonth));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var result = await service.GetMonthlyPeriodsAsync(3, CancellationToken.None);

        var euroGroup = Assert.Single(result.CurrencyGroups);
        Assert.Equal("EUR", euroGroup.Currency);
        Assert.Equal(3, euroGroup.Periods.Count);

        var previous = euroGroup.Periods[^2];
        Assert.Equal(1200m, previous.Income);
        Assert.Equal(55.5m, previous.Spend);
        Assert.Equal(1144.5m, previous.Net);
        Assert.Equal(3, previous.CountedTransactionCount);
        Assert.False(previous.IsPartial);

        var current = euroGroup.Periods[^1];
        Assert.Equal(25m, current.Spend);
        Assert.True(current.IsPartial);
    }

    [Fact]
    public async Task MonthlyPeriods_ExcludeBalanceOnlyAndOtherUsers()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext, "EUR");
        var other = await SeedUserWithAccountAsync(dbContext, "EUR");
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 5, 9, 0, 0, DateTimeKind.Utc);

        var balanceOnly = CreateTransaction(seeded.AccountId, -999m, "EUR", thisMonth);
        balanceOnly.AnalyticsTreatment = TransactionAnalyticsTreatments.BalanceOnly;

        dbContext.Transactions.AddRange(
            CreateTransaction(seeded.AccountId, -10m, "EUR", thisMonth),
            balanceOnly,
            CreateTransaction(other.AccountId, -500m, "EUR", thisMonth));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var result = await service.GetMonthlyPeriodsAsync(1, CancellationToken.None);

        var euroGroup = Assert.Single(result.CurrencyGroups);
        var period = Assert.Single(euroGroup.Periods);
        Assert.Equal(10m, period.Spend);
        Assert.Equal(0m, period.Income);
        Assert.Equal(1, period.CountedTransactionCount);
    }

    [Fact]
    public async Task MonthlyPeriods_SeparateCurrenciesAndClampMonths()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext, "EUR");
        var gbpAccount = await SeedAccountForUserAsync(dbContext, seeded.UserId, "GBP");
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 3, 8, 0, 0, DateTimeKind.Utc);

        dbContext.Transactions.AddRange(
            CreateTransaction(seeded.AccountId, -20m, "EUR", thisMonth),
            CreateTransaction(gbpAccount, -30m, "GBP", thisMonth));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var result = await service.GetMonthlyPeriodsAsync(999, CancellationToken.None);

        Assert.Equal(InsightPeriodsService.MaxMonths, result.MonthsRequested);
        Assert.Equal(2, result.CurrencyGroups.Count);
        Assert.Equal(new[] { "EUR", "GBP" }, result.CurrencyGroups.Select(x => x.Currency).ToArray());
        Assert.All(result.CurrencyGroups, group => Assert.Equal(InsightPeriodsService.MaxMonths, group.Periods.Count));
    }

    private static Transaction CreateTransaction(
        Guid accountId,
        decimal amount,
        string currency,
        DateTime bookedAtUtc)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = currency,
            Description = "Synthetic insight row",
            EntryKind = TransactionEntryKinds.Ordinary,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
    }

    private static InsightPeriodsService CreateService(AppDbContext dbContext, Guid userId)
    {
        return new InsightPeriodsService(dbContext, new FixedCurrentUserProvider(userId));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"insight-periods-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedUserWithAccountAsync(
        AppDbContext dbContext,
        string currency)
    {
        var userId = Guid.NewGuid();
        var email = $"insights-{userId:N}@example.test";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Insights Test User",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = DateTime.UtcNow
        });

        var accountId = await SeedAccountForUserAsync(dbContext, userId, currency);
        return (userId, accountId);
    }

    private static async Task<Guid> SeedAccountForUserAsync(
        AppDbContext dbContext,
        Guid userId,
        string currency)
    {
        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = $"Account {currency}",
            Type = "current",
            Currency = currency,
            Source = FinancialAccountSources.ProviderProjected,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return accountId;
    }

    private sealed class FixedCurrentUserProvider(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid value)
        {
            value = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid value)
        {
            value = Guid.Empty;
            return false;
        }
    }
}
