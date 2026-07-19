using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class MerchantCategorizationBackfillTests
{
    [Fact]
    public async Task Backfill_CategorizesOnlyNullTaxonomyOrdinaryRows()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        var tesco = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 3", -33.75m, now);
        var refund = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 3 REFUND", 12.5m, now);
        var unknown = CreateTransaction(seeded.AccountId, "SOME UNKNOWN MERCHANT", -10m, now);

        var alreadyCategorized = CreateTransaction(seeded.AccountId, "VDC-LIDL IRELAND L", -20m, now);
        alreadyCategorized.TaxonomyDomainId = 130;
        alreadyCategorized.TaxonomyCategoryId = 13020;

        var relationshipClaimed = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 2", -15m, now);
        relationshipClaimed.DeterministicRelationshipType = "internal_transfer";

        var balanceOnly = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 4", -9m, now);
        balanceOnly.AnalyticsTreatment = TransactionAnalyticsTreatments.BalanceOnly;

        dbContext.Transactions.AddRange(tesco, refund, unknown, alreadyCategorized, relationshipClaimed, balanceOnly);
        await dbContext.SaveChangesAsync();

        var summary = await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var reloadedTesco = await dbContext.Transactions.SingleAsync(x => x.Id == tesco.Id);
        Assert.Equal(130, reloadedTesco.TaxonomyDomainId);
        Assert.Equal(13010, reloadedTesco.TaxonomyCategoryId);
        Assert.Null(reloadedTesco.TaxonomySubcategoryId);

        // The inflow refund must stay uncategorized (direction enforcement).
        var reloadedRefund = await dbContext.Transactions.SingleAsync(x => x.Id == refund.Id);
        Assert.Null(reloadedRefund.TaxonomyCategoryId);

        var reloadedExisting = await dbContext.Transactions.SingleAsync(x => x.Id == alreadyCategorized.Id);
        Assert.Equal(13020, reloadedExisting.TaxonomyCategoryId);

        var reloadedClaimed = await dbContext.Transactions.SingleAsync(x => x.Id == relationshipClaimed.Id);
        Assert.Null(reloadedClaimed.TaxonomyCategoryId);

        var reloadedBalanceOnly = await dbContext.Transactions.SingleAsync(x => x.Id == balanceOnly.Id);
        Assert.Null(reloadedBalanceOnly.TaxonomyCategoryId);

        Assert.Equal(3, summary.RowsExamined);
        Assert.Equal(1, summary.RowsCategorized);
        Assert.Equal(2, summary.RowsUnmatched);
    }

    [Fact]
    public async Task Backfill_ResolvesSubcategoryTriplesAndScopesToUser()
    {
        await using var dbContext = CreateDbContext();
        var primary = await SeedUserWithAccountAsync(dbContext);
        var other = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        var salary = CreateTransaction(primary.AccountId, "ACME LTD PAYROLL JUL", 3200m, now);
        var otherUsersTesco = CreateTransaction(other.AccountId, "VDC-TESCO STORES 3", -30m, now);
        dbContext.Transactions.AddRange(salary, otherUsersTesco);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(primary.UserId, CancellationToken.None);

        var reloadedSalary = await dbContext.Transactions.SingleAsync(x => x.Id == salary.Id);
        Assert.Equal(910, reloadedSalary.TaxonomyDomainId);
        Assert.Equal(91010, reloadedSalary.TaxonomyCategoryId);
        Assert.Equal(910101, reloadedSalary.TaxonomySubcategoryId);

        var reloadedOther = await dbContext.Transactions.SingleAsync(x => x.Id == otherUsersTesco.Id);
        Assert.Null(reloadedOther.TaxonomyCategoryId);
    }

    private static MerchantCategorizationBackfillService CreateService(AppDbContext dbContext)
    {
        return new MerchantCategorizationBackfillService(
            dbContext,
            Options.Create(new MerchantCategorizationOptions
            {
                BackfillOnGlobalSyncEnabled = true,
                MaxRowsPerRun = 500
            }),
            NullLogger<MerchantCategorizationBackfillService>.Instance);
    }

    private static Transaction CreateTransaction(
        Guid accountId,
        string description,
        decimal amount,
        DateTime bookedAtUtc)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = "EUR",
            Description = description,
            EntryKind = TransactionEntryKinds.Ordinary,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"merchant-backfill-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedUserWithAccountAsync(AppDbContext dbContext)
    {
        var userId = Guid.NewGuid();
        var email = $"backfill-{userId:N}@example.test";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Backfill Test User",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = DateTime.UtcNow
        });

        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Backfill Account",
            Type = "current",
            Currency = "EUR",
            Source = FinancialAccountSources.ProviderProjected,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return (userId, accountId);
    }
}
