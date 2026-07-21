using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class InsightCategoryBreakdownTests
{
    [Fact]
    public async Task Breakdown_GroupsSpendByCategory_AndReportsUncategorized()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext, "EUR");
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 8, 12, 0, 0, DateTimeKind.Utc);

        var tesco = CreateTransaction(seeded.AccountId, -33.75m, "EUR", thisMonth);
        tesco.TaxonomyDomainId = 130;
        tesco.TaxonomyCategoryId = 13010;

        var lidl = CreateTransaction(seeded.AccountId, -55.32m, "EUR", thisMonth.AddDays(1));
        lidl.TaxonomyDomainId = 130;
        lidl.TaxonomyCategoryId = 13010;

        var cinema = CreateTransaction(seeded.AccountId, -24m, "EUR", thisMonth.AddDays(2));
        cinema.TaxonomyDomainId = 210;
        cinema.TaxonomyCategoryId = 21010;

        var unknown = CreateTransaction(seeded.AccountId, -12m, "EUR", thisMonth.AddDays(3));
        var income = CreateTransaction(seeded.AccountId, 3200m, "EUR", thisMonth.AddDays(4));

        dbContext.Transactions.AddRange(tesco, lidl, cinema, unknown, income);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var result = await service.GetMonthlyBreakdownAsync(1, CancellationToken.None);

        var euroGroup = Assert.Single(result.CurrencyGroups);
        var period = Assert.Single(euroGroup.Periods);

        Assert.Equal(125.07m, period.TotalSpend);
        Assert.Equal(113.07m, period.CategorizedSpend);
        Assert.Equal(12m, period.UncategorizedSpend);
        Assert.Equal(1, period.UncategorizedTransactionCount);
        Assert.True(period.IsPartial);

        Assert.Equal(2, period.Categories.Count);
        var groceries = period.Categories[0];
        Assert.Equal(13010, groceries.TaxonomyCategoryId);
        Assert.Equal(89.07m, groceries.Spend);
        Assert.Equal(2, groceries.TransactionCount);
        Assert.False(string.IsNullOrWhiteSpace(groceries.CategoryName));

        // Sorted by spend descending: groceries above cinema.
        Assert.True(period.Categories[0].Spend > period.Categories[1].Spend);
    }

    [Fact]
    public async Task Breakdown_NeutralizedTransfers_NeverCountAsSpend()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext, "EUR");
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 6, 10, 0, 0, DateTimeKind.Utc);

        // A savings transfer as the deterministic engine stamps it: taxonomy
        // plus a matched savings relationship. Taxonomy alone deliberately
        // never neutralizes - the policy engine requires movement evidence.
        var savings = CreateTransaction(seeded.AccountId, -150m, "EUR", thisMonth);
        savings.TaxonomyDomainId = 180;
        savings.TaxonomyCategoryId = 18010;
        savings.TaxonomySubcategoryId = 180102;
        savings.DeterministicClassificationStatus = DeterministicClassificationStatus.ClassifiedMatchedRule;
        savings.DeterministicRelationshipType = "savings_transfer";

        var groceries = CreateTransaction(seeded.AccountId, -20m, "EUR", thisMonth.AddDays(1));
        groceries.TaxonomyDomainId = 130;
        groceries.TaxonomyCategoryId = 13010;

        dbContext.Transactions.AddRange(savings, groceries);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var result = await service.GetMonthlyBreakdownAsync(1, CancellationToken.None);

        var period = Assert.Single(Assert.Single(result.CurrencyGroups).Periods);
        Assert.Equal(20m, period.TotalSpend);
        var only = Assert.Single(period.Categories);
        Assert.Equal(13010, only.TaxonomyCategoryId);
    }

    [Fact]
    public async Task Breakdown_ScopesToUser_AndSeparatesCurrencies()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext, "EUR");
        var gbpAccount = await SeedAccountForUserAsync(dbContext, seeded.UserId, "GBP");
        var other = await SeedUserWithAccountAsync(dbContext, "EUR");
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 4, 9, 0, 0, DateTimeKind.Utc);

        var mine = CreateTransaction(seeded.AccountId, -10m, "EUR", thisMonth);
        mine.TaxonomyDomainId = 130;
        mine.TaxonomyCategoryId = 13010;
        var mineGbp = CreateTransaction(gbpAccount, -30m, "GBP", thisMonth);
        mineGbp.TaxonomyDomainId = 130;
        mineGbp.TaxonomyCategoryId = 13020;
        var theirs = CreateTransaction(other.AccountId, -500m, "EUR", thisMonth);
        theirs.TaxonomyDomainId = 130;
        theirs.TaxonomyCategoryId = 13010;

        dbContext.Transactions.AddRange(mine, mineGbp, theirs);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var result = await service.GetMonthlyBreakdownAsync(1, CancellationToken.None);

        Assert.Equal(2, result.CurrencyGroups.Count);
        var euro = result.CurrencyGroups.Single(x => x.Currency == "EUR");
        Assert.Equal(10m, Assert.Single(euro.Periods).TotalSpend);
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
            Description = "Synthetic breakdown row",
            EntryKind = TransactionEntryKinds.Ordinary,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
    }

    private static InsightCategoryBreakdownService CreateService(AppDbContext dbContext, Guid userId)
    {
        return new InsightCategoryBreakdownService(
            dbContext,
            new FixedCurrentUserProvider(userId),
            new ExpenseTaxonomyService());
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"insight-breakdown-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedUserWithAccountAsync(
        AppDbContext dbContext,
        string currency)
    {
        var userId = Guid.NewGuid();
        var email = $"breakdown-{userId:N}@example.test";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Breakdown Test User",
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
