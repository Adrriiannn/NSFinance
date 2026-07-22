using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

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
        Assert.Equal("merchant_knowledge", reloadedTesco.CategorizationRuleKey);
        Assert.Equal("TESCO", reloadedTesco.CategorizationSignal);
        Assert.NotNull(reloadedTesco.CategorizationCharacteristicsVersion);
        Assert.NotNull(reloadedTesco.CategorizedUtc);

        // Direction enforcement keeps the grocery seed off the inflow; the
        // Refunds category seed claims it instead.
        var reloadedRefund = await dbContext.Transactions.SingleAsync(x => x.Id == refund.Id);
        Assert.Equal(900, reloadedRefund.TaxonomyDomainId);
        Assert.Equal(90010, reloadedRefund.TaxonomyCategoryId);
        Assert.Null(reloadedRefund.TaxonomySubcategoryId);
        Assert.Equal("REFUND", reloadedRefund.CategorizationSignal);

        var reloadedExisting = await dbContext.Transactions.SingleAsync(x => x.Id == alreadyCategorized.Id);
        Assert.Equal(13020, reloadedExisting.TaxonomyCategoryId);

        var reloadedClaimed = await dbContext.Transactions.SingleAsync(x => x.Id == relationshipClaimed.Id);
        Assert.Null(reloadedClaimed.TaxonomyCategoryId);

        var reloadedBalanceOnly = await dbContext.Transactions.SingleAsync(x => x.Id == balanceOnly.Id);
        Assert.Null(reloadedBalanceOnly.TaxonomyCategoryId);

        Assert.Equal(3, summary.RowsExamined);
        Assert.Equal(2, summary.RowsCategorized);
        Assert.Equal(1, summary.RowsUnmatched);
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

    [Fact]
    public async Task Backfill_UsesGrownKnowledgeRows_WithoutCodeChanges()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        // A row learned by AI investigation, not present in any catalog signal.
        dbContext.MerchantKnowledge.Add(new MerchantKnowledge
        {
            Id = Guid.NewGuid(),
            NormalizedPattern = "NEWCAFE DUNDRUM",
            DisplayName = "NewCafe Dundrum",
            TaxonomyDomainId = 130,
            TaxonomyCategoryId = 13020,
            TaxonomySubcategoryId = null,
            DirectionExpectation = "outflow",
            Source = MerchantKnowledgeSources.AiInvestigation,
            Confidence = 0.92,
            CharacteristicsVersion = 1,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        });

        var cafeVisit = CreateTransaction(seeded.AccountId, "VDP-NEWCAFE DUNDRUM 12", -6.4m, now);
        dbContext.Transactions.Add(cafeVisit);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == cafeVisit.Id);
        Assert.Equal(13020, reloaded.TaxonomyCategoryId);
        Assert.Equal("merchant_knowledge", reloaded.CategorizationRuleKey);
        Assert.Equal("NEWCAFE DUNDRUM", reloaded.CategorizationSignal);
    }

    [Fact]
    public async Task Backfill_UserScopedCorrectionRow_BeatsGlobalKnowledge()
    {
        await using var dbContext = CreateDbContext();
        var corrector = await SeedUserWithAccountAsync(dbContext);
        var other = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        // The corrector taught the system that their TESCO spend is Dining
        // (say, the in-store cafe); everyone else keeps the global truth.
        dbContext.MerchantKnowledge.Add(new MerchantKnowledge
        {
            Id = Guid.NewGuid(),
            UserId = corrector.UserId,
            NormalizedPattern = "TESCO",
            DisplayName = "Tesco (my cafe)",
            TaxonomyDomainId = 130,
            TaxonomyCategoryId = 13020,
            DirectionExpectation = "outflow",
            Source = MerchantKnowledgeSources.UserCorrection,
            Confidence = 1.0,
            CharacteristicsVersion = 1,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        });

        var correctorTesco = CreateTransaction(corrector.AccountId, "VDC-TESCO STORES 3", -20m, now);
        var otherTesco = CreateTransaction(other.AccountId, "VDC-TESCO STORES 3", -20m, now);
        dbContext.Transactions.AddRange(correctorTesco, otherTesco);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(corrector.UserId, CancellationToken.None);
        await CreateService(dbContext).BackfillAsync(other.UserId, CancellationToken.None);

        var reloadedCorrector = await dbContext.Transactions.SingleAsync(x => x.Id == correctorTesco.Id);
        Assert.Equal(13020, reloadedCorrector.TaxonomyCategoryId);

        var reloadedOther = await dbContext.Transactions.SingleAsync(x => x.Id == otherTesco.Id);
        Assert.Equal(13010, reloadedOther.TaxonomyCategoryId);
    }

    [Fact]
    public async Task Backfill_SharedSavingsSignal_SeedsAsEitherDirection_AndMatchesInflows()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        // The savings-transfer pair shares signals across its outflow and
        // inflow definitions; the merged seed must match both directions.
        var arrival = CreateTransaction(seeded.AccountId, "*MOBI SAVINGS-109 *MOBI MAIN-026", 150m, now);
        var departure = CreateTransaction(seeded.AccountId, "*MOBI SAVINGS-109 *MOBI MAIN-026", -150m, now);
        dbContext.Transactions.AddRange(arrival, departure);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var savingsSeed = await dbContext.MerchantKnowledge
            .SingleAsync(x => x.NormalizedPattern == "MOBI SAVINGS");
        Assert.Equal("either", savingsSeed.DirectionExpectation);

        var reloadedArrival = await dbContext.Transactions.SingleAsync(x => x.Id == arrival.Id);
        Assert.Equal(180102, reloadedArrival.TaxonomySubcategoryId);

        var reloadedDeparture = await dbContext.Transactions.SingleAsync(x => x.Id == departure.Id);
        Assert.Equal(180102, reloadedDeparture.TaxonomySubcategoryId);
    }

    [Fact]
    public async Task Backfill_VersionBump_RetargetsMovedSeedRows_AndLeavesOtherSourcesAlone()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var past = DateTime.UtcNow.AddDays(-30);

        // A version-1-era seed row still mapping Starbucks to Dining, and an
        // AI-learned row that must never be rewritten by the catalog.
        dbContext.MerchantKnowledge.AddRange(
            new MerchantKnowledge
            {
                Id = Guid.NewGuid(),
                NormalizedPattern = "STARBUCKS",
                DisplayName = "STARBUCKS",
                TaxonomyDomainId = 130,
                TaxonomyCategoryId = 13020,
                TaxonomySubcategoryId = null,
                DirectionExpectation = "outflow",
                Source = MerchantKnowledgeSources.Seed,
                Confidence = 1.0,
                CharacteristicsVersion = 1,
                IsActive = true,
                CreatedUtc = past,
                UpdatedUtc = past
            },
            new MerchantKnowledge
            {
                Id = Guid.NewGuid(),
                NormalizedPattern = "MCDONALDS",
                DisplayName = "McDonald's",
                TaxonomyDomainId = 999,
                TaxonomyCategoryId = 99999,
                TaxonomySubcategoryId = null,
                DirectionExpectation = "outflow",
                Source = MerchantKnowledgeSources.AiInvestigation,
                Confidence = 0.9,
                CharacteristicsVersion = 1,
                IsActive = true,
                CreatedUtc = past,
                UpdatedUtc = past
            });
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var starbucks = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "STARBUCKS");
        Assert.Equal(130301, starbucks.TaxonomySubcategoryId);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, starbucks.CharacteristicsVersion);

        var mcdonalds = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "MCDONALDS");
        Assert.Equal(99999, mcdonalds.TaxonomyCategoryId);
        Assert.Equal(MerchantKnowledgeSources.AiInvestigation, mcdonalds.Source);
    }

    [Fact]
    public async Task Backfill_PassSixSignals_CategorizeEverydayIrishSpend()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        var coffee = CreateTransaction(seeded.AccountId, "VDP-CAFFE NERO 021", -4.1m, now);
        var pharmacy = CreateTransaction(seeded.AccountId, "VDC-BRADYS PHARMACY NAVAN", -18.5m, now);
        var toll = CreateTransaction(seeded.AccountId, "EFLOW.IE TOLL", -3.5m, now);
        dbContext.Transactions.AddRange(coffee, pharmacy, toll);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var reloadedCoffee = await dbContext.Transactions.SingleAsync(x => x.Id == coffee.Id);
        Assert.Equal(130301, reloadedCoffee.TaxonomySubcategoryId);

        var reloadedPharmacy = await dbContext.Transactions.SingleAsync(x => x.Id == pharmacy.Id);
        Assert.Equal(16040, reloadedPharmacy.TaxonomyCategoryId);

        var reloadedToll = await dbContext.Transactions.SingleAsync(x => x.Id == toll.Id);
        Assert.Equal(12050, reloadedToll.TaxonomyCategoryId);
    }

    [Fact]
    public async Task Backfill_VersionBump_ReopensNeedsReviewCandidates_ExactlyOnce()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var past = DateTime.UtcNow.AddHours(-2);

        dbContext.MerchantKnowledgeCandidates.Add(new MerchantKnowledgeCandidate
        {
            Id = Guid.NewGuid(),
            NormalizedDescriptor = "TEBEX",
            RawDescriptorSample = "TEBEX.ORG",
            Status = MerchantKnowledgeCandidateStatuses.NeedsReview,
            ObservedOccurrences = 2,
            ObservedSpendAbs = 60m,
            ObservedDirection = "outflow",
            AttemptCount = 3,
            LastOutcomeCode = "judgment_abstained",
            CreatedUtc = past,
            UpdatedUtc = past
        });
        await dbContext.SaveChangesAsync();

        // First run of a fresh version: seeds materialize and the parked
        // candidate re-opens for another judgment under the new catalog.
        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.Pending, candidate.Status);
        Assert.Equal("reopened_by_catalog_version", candidate.LastOutcomeCode);
        Assert.Null(candidate.NextEligibleUtc);

        // A second run in the same version must not touch it again.
        candidate.Status = MerchantKnowledgeCandidateStatuses.NeedsReview;
        candidate.LastOutcomeCode = "judgment_abstained";
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);
        var untouched = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.NeedsReview, untouched.Status);
    }

    [Fact]
    public async Task Backfill_SeedsKnowledgeOncePerCatalogVersion()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);
        var afterFirst = await dbContext.MerchantKnowledge.CountAsync();
        Assert.True(afterFirst > 0, "seeding must materialize the catalog signals");

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);
        var afterSecond = await dbContext.MerchantKnowledge.CountAsync();
        Assert.Equal(afterFirst, afterSecond);

        Assert.All(
            await dbContext.MerchantKnowledge.ToListAsync(),
            row => Assert.Equal(MerchantKnowledgeSources.Seed, row.Source));
    }

    private static MerchantCategorizationBackfillService CreateService(AppDbContext dbContext)
    {
        // Growth stays disabled here; the growth loop has its own test suite.
        var growthService = new MerchantKnowledgeGrowthService(
            dbContext,
            new ThrowingInvestigationService(),
            new MerchantAcceptancePolicy(),
            new ThrowingCategoryJudge(),
            Options.Create(new MerchantKnowledgeGrowthOptions { Enabled = false }),
            NullLogger<MerchantKnowledgeGrowthService>.Instance);

        return new MerchantCategorizationBackfillService(
            dbContext,
            growthService,
            Options.Create(new MerchantCategorizationOptions
            {
                BackfillOnGlobalSyncEnabled = true,
                MaxRowsPerRun = 500
            }),
            NullLogger<MerchantCategorizationBackfillService>.Instance);
    }

    private sealed class ThrowingInvestigationService : IMerchantInvestigationService
    {
        public Task<MerchantInvestigationResult> InvestigateAsync(
            MerchantInvestigationRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Investigation must not run when growth is disabled.");
        }
    }

    private sealed class ThrowingCategoryJudge : IMerchantCategoryJudge
    {
        public Task<MerchantCategoryJudgment> JudgeAsync(
            MerchantCategoryJudgmentInput input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Judgment must not run when growth is disabled.");
        }
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
