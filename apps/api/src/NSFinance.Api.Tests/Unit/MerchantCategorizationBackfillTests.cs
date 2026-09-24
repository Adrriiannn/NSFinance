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
    public async Task Backfill_StampsUsageAccounting_OnMatchedKnowledge()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;
        dbContext.Transactions.AddRange(
            CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 3", -33.75m, now),
            CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 9", -12.10m, now));
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var tescoKnowledge = await dbContext.MerchantKnowledge
            .SingleAsync(k => k.UserId == null && k.NormalizedPattern == "TESCO");
        Assert.Equal(2, tescoKnowledge.MatchCount);
        Assert.NotNull(tescoKnowledge.LastMatchedUtc);

        // A second run has nothing left to categorize: usage stays put.
        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);
        var reloaded = await dbContext.MerchantKnowledge
            .SingleAsync(k => k.UserId == null && k.NormalizedPattern == "TESCO");
        Assert.Equal(2, reloaded.MatchCount);
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

    // Catalog version-bump behavior (CAT-001 v6 impact analysis). Each test
    // stages the knowledge base as the previous version left it, then runs
    // the first backfill under the current version.

    [Fact]
    public async Task VersionBump_RetargetsMovedBrands_InsertsNewSignals_AndNeverReclassifiesCategorizedRows()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;
        var earlier = now.AddDays(-10);
        var priorVersion = CategoryCharacteristicsCatalog.Version - 1;

        // Brand signals as the previous version seeded them: category level,
        // before the rebalance moved them down to their subcategories.
        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("NETFLIX", 280, 28010, null),
            PriorVersionSeed("PINERGY", 140, 14010, null));

        var categorizedNetflix = CreateTransaction(seeded.AccountId, "NETFLIX.COM", -12.99m, earlier);
        categorizedNetflix.TaxonomyDomainId = 280;
        categorizedNetflix.TaxonomyCategoryId = 28010;
        categorizedNetflix.CategorizationRuleKey = "merchant_knowledge";
        categorizedNetflix.CategorizationSignal = "NETFLIX";
        categorizedNetflix.CategorizationCharacteristicsVersion = priorVersion;
        categorizedNetflix.CategorizedUtc = earlier;

        var newNetflix = CreateTransaction(seeded.AccountId, "NETFLIX.COM", -12.99m, now);
        var pinergy = CreateTransaction(seeded.AccountId, "PINERGY 0871", -30m, now);
        var tuition = CreateTransaction(seeded.AccountId, "UCD TUITION", -3000m, now);
        dbContext.Transactions.AddRange(categorizedNetflix, newNetflix, pinergy, tuition);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var netflixSeed = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "NETFLIX");
        Assert.Equal(28010, netflixSeed.TaxonomyCategoryId);
        Assert.Equal(280101, netflixSeed.TaxonomySubcategoryId);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, netflixSeed.CharacteristicsVersion);

        var pinergySeed = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "PINERGY");
        Assert.Equal(140102, pinergySeed.TaxonomySubcategoryId);

        var tuitionSeed = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "TUITION");
        Assert.Equal(MerchantKnowledgeSources.Seed, tuitionSeed.Source);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, tuitionSeed.CharacteristicsVersion);
        Assert.True(await dbContext.MerchantKnowledge.AnyAsync(x => x.NormalizedPattern == "EXAM FEE"));

        // The bump never reclassifies: the row categorized under the
        // previous version keeps its category-level triple and evidence.
        var reloadedCategorized = await dbContext.Transactions.SingleAsync(x => x.Id == categorizedNetflix.Id);
        Assert.Equal(28010, reloadedCategorized.TaxonomyCategoryId);
        Assert.Null(reloadedCategorized.TaxonomySubcategoryId);
        Assert.Equal(priorVersion, reloadedCategorized.CategorizationCharacteristicsVersion);
        Assert.Equal(earlier, reloadedCategorized.CategorizedUtc);

        // Only uncategorized rows pick up the retargeted and new knowledge.
        var reloadedNewNetflix = await dbContext.Transactions.SingleAsync(x => x.Id == newNetflix.Id);
        Assert.Equal(280101, reloadedNewNetflix.TaxonomySubcategoryId);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, reloadedNewNetflix.CategorizationCharacteristicsVersion);

        var reloadedPinergy = await dbContext.Transactions.SingleAsync(x => x.Id == pinergy.Id);
        Assert.Equal(140102, reloadedPinergy.TaxonomySubcategoryId);

        var reloadedTuition = await dbContext.Transactions.SingleAsync(x => x.Id == tuition.Id);
        Assert.Equal(260, reloadedTuition.TaxonomyDomainId);
        Assert.Equal(26010, reloadedTuition.TaxonomyCategoryId);
        Assert.Equal("TUITION", reloadedTuition.CategorizationSignal);
    }

    [Fact]
    public async Task VersionBump_NeverRewritesManualChoices_OrUserScopedKnowledge()
    {
        await using var dbContext = CreateDbContext();
        var owner = await SeedUserWithAccountAsync(dbContext);
        var other = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;
        var earlier = now.AddDays(-10);
        var priorVersion = CategoryCharacteristicsCatalog.Version - 1;

        // The owner files Spotify as a business software expense.
        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("SPOTIFY", 280, 28010, null),
            new MerchantKnowledge
            {
                Id = Guid.NewGuid(),
                UserId = owner.UserId,
                NormalizedPattern = "SPOTIFY",
                DisplayName = "Spotify (business)",
                TaxonomyDomainId = 290,
                TaxonomyCategoryId = 29030,
                DirectionExpectation = "outflow",
                Source = MerchantKnowledgeSources.UserCorrection,
                Confidence = 1.0,
                CharacteristicsVersion = priorVersion,
                IsActive = true,
                CreatedUtc = earlier,
                UpdatedUtc = earlier
            });

        var manual = CreateTransaction(owner.AccountId, "SPOTIFY 4471", -10.99m, earlier);
        manual.TaxonomyDomainId = 290;
        manual.TaxonomyCategoryId = 29030;
        manual.CategorizationRuleKey = "user_correction";
        manual.CategorizationCharacteristicsVersion = priorVersion;
        manual.CategorizedUtc = earlier;

        var ownerNew = CreateTransaction(owner.AccountId, "SPOTIFY 4471", -10.99m, now);
        var otherNew = CreateTransaction(other.AccountId, "SPOTIFY 4471", -10.99m, now);
        dbContext.Transactions.AddRange(manual, ownerNew, otherNew);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(owner.UserId, CancellationToken.None);
        await CreateService(dbContext).BackfillAsync(other.UserId, CancellationToken.None);

        var globalSeed = await dbContext.MerchantKnowledge.SingleAsync(x => x.UserId == null && x.NormalizedPattern == "SPOTIFY");
        Assert.Equal(280102, globalSeed.TaxonomySubcategoryId);

        var userRow = await dbContext.MerchantKnowledge.SingleAsync(x => x.UserId == owner.UserId);
        Assert.Equal(29030, userRow.TaxonomyCategoryId);
        Assert.Equal(MerchantKnowledgeSources.UserCorrection, userRow.Source);
        Assert.Equal(priorVersion, userRow.CharacteristicsVersion);

        var reloadedManual = await dbContext.Transactions.SingleAsync(x => x.Id == manual.Id);
        Assert.Equal(29030, reloadedManual.TaxonomyCategoryId);
        Assert.Equal("user_correction", reloadedManual.CategorizationRuleKey);
        Assert.Equal(earlier, reloadedManual.CategorizedUtc);

        // The owner's own rule still wins; everyone else gets the refined seed.
        var reloadedOwnerNew = await dbContext.Transactions.SingleAsync(x => x.Id == ownerNew.Id);
        Assert.Equal(29030, reloadedOwnerNew.TaxonomyCategoryId);

        var reloadedOtherNew = await dbContext.Transactions.SingleAsync(x => x.Id == otherNew.Id);
        Assert.Equal(280102, reloadedOtherNew.TaxonomySubcategoryId);
    }

    [Fact]
    public async Task VersionBump_Assignments_AreSelectableForExactRollback_WithoutCatchingUserTaughtRows()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("NETFLIX", 280, 28010, null),
            PriorVersionSeed("TESCO", 130, 13010, null));

        var corrected = CreateTransaction(seeded.AccountId, "NETFLIX", -12.99m, now);
        var sibling = CreateTransaction(seeded.AccountId, "NETFLIX", -12.99m, now.AddDays(-1));
        var tuition = CreateTransaction(seeded.AccountId, "UCD TUITION", -3000m, now);
        var tesco = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 3", -20m, now);
        dbContext.Transactions.AddRange(corrected, sibling, tuition, tesco);
        await dbContext.SaveChangesAsync();

        var applyUtc = DateTime.UtcNow;
        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        // Unchanged seed rows keep their old version, so business-as-usual
        // matches after the bump are not attributed to it.
        var reloadedTesco = await dbContext.Transactions.SingleAsync(x => x.Id == tesco.Id);
        Assert.Equal(13010, reloadedTesco.TaxonomyCategoryId);
        Assert.Equal(CategoryCharacteristicsCatalog.Version - 1, reloadedTesco.CategorizationCharacteristicsVersion);

        // After the bump the user recategorizes one Netflix row and asks to
        // always use it; LearnMerchant retargets the sibling with the same
        // rule key and version stamp a bump assignment carries.
        corrected.TaxonomyDomainId = 290;
        corrected.TaxonomyCategoryId = 29030;
        corrected.TaxonomySubcategoryId = null;
        corrected.CategorizationRuleKey = "user_correction";
        await new MerchantCorrectionLearningService(dbContext, NullLogger<MerchantCorrectionLearningService>.Instance)
            .LearnFromCorrectionAsync(seeded.UserId, corrected, 290, 29030, null, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var stampedByVersion = await dbContext.Transactions
            .Where(x =>
                x.CategorizationRuleKey == "merchant_knowledge"
                && x.CategorizationCharacteristicsVersion == CategoryCharacteristicsCatalog.Version
                && x.CategorizedUtc >= applyUtc)
            .ToListAsync();

        // Version and time alone would also revert the user-taught sibling.
        Assert.Equal(
            new[] { sibling.Id, tuition.Id }.OrderBy(x => x),
            stampedByVersion.Select(x => x.Id).OrderBy(x => x));

        // The rollback selector must exclude patterns the user has taught.
        var userTaught = await dbContext.MerchantKnowledge
            .Where(x => x.UserId == seeded.UserId)
            .Select(x => x.NormalizedPattern)
            .ToListAsync();
        var rollbackSet = stampedByVersion
            .Where(x => !userTaught.Contains(x.CategorizationSignal!))
            .Select(x => x.Id)
            .ToList();
        Assert.Equal(new[] { tuition.Id }, rollbackSet);

        // Every bump assignment started from an all-null row, so resetting
        // the triple and evidence restores the exact pre-bump state.
        var reloadedTuition = await dbContext.Transactions.SingleAsync(x => x.Id == tuition.Id);
        Assert.Equal(26010, reloadedTuition.TaxonomyCategoryId);
        Assert.Equal("TUITION", reloadedTuition.CategorizationSignal);
    }

    [Fact]
    public async Task VersionBump_SeedRowsDroppedFromTheCatalog_AreDeactivated_AndStopMatching()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        // Dropping a signal from the catalog is how a bad live seed is
        // retired: the row is kept for evidence but stops matching. The
        // v5-era bare MAINTENANCE seed is the real case.
        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("ZZQ LEGACY PATTERN", 130, 13020, null),
            PriorVersionSeed("MAINTENANCE", 200, 20040, 200407));
        var legacy = CreateTransaction(seeded.AccountId, "ZZQ LEGACY PATTERN 12", -9m, now);
        var garden = CreateTransaction(seeded.AccountId, "GARDEN MAINTENANCE LTD", -80m, now);
        dbContext.Transactions.AddRange(legacy, garden);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var orphan = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "ZZQ LEGACY PATTERN");
        Assert.False(orphan.IsActive);
        Assert.Equal(13020, orphan.TaxonomyCategoryId);
        Assert.Equal(CategoryCharacteristicsCatalog.Version - 1, orphan.CharacteristicsVersion);

        var maintenance = await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "MAINTENANCE");
        Assert.False(maintenance.IsActive);

        Assert.Null((await dbContext.Transactions.SingleAsync(x => x.Id == legacy.Id)).TaxonomyCategoryId);
        Assert.Null((await dbContext.Transactions.SingleAsync(x => x.Id == garden.Id)).TaxonomyCategoryId);

        var run = await dbContext.MerchantKnowledgeSeedRuns.SingleAsync();
        Assert.Equal(2, run.DeactivatedCount);
    }

    [Fact]
    public async Task VersionBump_WithNoPlanChanges_RecordsTheVersion_AndReopensCandidatesOnce()
    {
        // Before the seed-run ledger, "already seeded" was inferred from a
        // seed row carrying the current version, so a bump that inserted and
        // retargeted nothing re-ran seeding and re-opened parked candidates
        // on every sync. The ledger records the version even when the plan
        // changes nothing.
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var past = DateTime.UtcNow.AddHours(-2);

        // The identical plan seeded under the previous version, pre-ledger.
        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);
        foreach (var seed in await dbContext.MerchantKnowledge.ToListAsync())
        {
            seed.CharacteristicsVersion = CategoryCharacteristicsCatalog.Version - 1;
        }

        dbContext.MerchantKnowledgeSeedRuns.RemoveRange(await dbContext.MerchantKnowledgeSeedRuns.ToListAsync());

        var candidate = new MerchantKnowledgeCandidate
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
        };
        dbContext.MerchantKnowledgeCandidates.Add(candidate);
        await dbContext.SaveChangesAsync();
        var seedCount = await dbContext.MerchantKnowledge.CountAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var reopened = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.Pending, reopened.Status);
        Assert.Equal("reopened_by_catalog_version", reopened.LastOutcomeCode);

        var run = await dbContext.MerchantKnowledgeSeedRuns.SingleAsync();
        Assert.Equal(CategoryCharacteristicsCatalog.Version, run.CharacteristicsVersion);
        Assert.Equal(0, run.InsertedCount);
        Assert.Equal(0, run.RetargetedCount);
        Assert.Equal(1, run.ReopenedCount);

        // Parked again by a later judgment: the next sync leaves it alone.
        reopened.Status = MerchantKnowledgeCandidateStatuses.NeedsReview;
        reopened.LastOutcomeCode = "judgment_abstained";
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var untouched = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.NeedsReview, untouched.Status);
        Assert.Equal(seedCount, await dbContext.MerchantKnowledge.CountAsync());
        Assert.Single(await dbContext.MerchantKnowledgeSeedRuns.ToListAsync());
    }

    [Fact]
    public async Task VersionBump_ReachesOnlyTheNewestUncategorizedWindow_PerRun()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        // Newer rows no signal will ever match fill the per-run window, so
        // an older row the bump could categorize is never examined.
        dbContext.Transactions.AddRange(
            CreateTransaction(seeded.AccountId, "QQX 4471", -5m, now),
            CreateTransaction(seeded.AccountId, "ZZQ 9920", -5m, now.AddDays(-1)));
        var olderTuition = CreateTransaction(seeded.AccountId, "UCD TUITION", -3000m, now.AddDays(-30));
        dbContext.Transactions.Add(olderTuition);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, maxRowsPerRun: 2);
        var first = await service.BackfillAsync(seeded.UserId, CancellationToken.None);
        var second = await service.BackfillAsync(seeded.UserId, CancellationToken.None);

        Assert.Equal(2, first.RowsExamined);
        Assert.Equal(0, first.RowsCategorized);
        Assert.Equal(0, second.RowsCategorized);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == olderTuition.Id);
        Assert.Null(reloaded.TaxonomyCategoryId);
    }

    internal static MerchantKnowledge PriorVersionSeed(
        string pattern,
        int domainId,
        int categoryId,
        int? subcategoryId)
    {
        var past = DateTime.UtcNow.AddDays(-30);
        return new MerchantKnowledge
        {
            Id = Guid.NewGuid(),
            NormalizedPattern = pattern,
            DisplayName = pattern,
            TaxonomyDomainId = domainId,
            TaxonomyCategoryId = categoryId,
            TaxonomySubcategoryId = subcategoryId,
            DirectionExpectation = "outflow",
            Source = MerchantKnowledgeSources.Seed,
            Confidence = 1.0,
            CharacteristicsVersion = CategoryCharacteristicsCatalog.Version - 1,
            IsActive = true,
            CreatedUtc = past,
            UpdatedUtc = past
        };
    }

    internal static MerchantCategorizationBackfillService CreateService(
        AppDbContext dbContext,
        int maxRowsPerRun = 500,
        MerchantKnowledgeSeedMode seedMode = MerchantKnowledgeSeedMode.Apply,
        IEnumerable<Guid>? pilotUserIds = null)
    {
        // Growth stays disabled here; the growth loop has its own test suite.
        var growthService = new MerchantKnowledgeGrowthService(
            dbContext,
            new ThrowingInvestigationService(),
            new MerchantAcceptancePolicy(),
            new ThrowingCategoryJudge(),
            Options.Create(new MerchantKnowledgeGrowthOptions { Enabled = false }),
            NullLogger<MerchantKnowledgeGrowthService>.Instance);

        // The reference lane stays disabled here; it has its own test suite.
        var referenceLane = new ReferenceLaneAssignmentService(
            dbContext,
            new ThrowingReferenceJudge(),
            Options.Create(new ReferenceLaneOptions { Enabled = false }),
            NullLogger<ReferenceLaneAssignmentService>.Instance);

        var seedService = new MerchantKnowledgeSeedService(
            dbContext,
            Options.Create(new MerchantKnowledgeSeedOptions
            {
                Mode = seedMode,
                PilotUserIds = pilotUserIds?.ToList() ?? []
            }),
            NullLogger<MerchantKnowledgeSeedService>.Instance);

        return new MerchantCategorizationBackfillService(
            dbContext,
            seedService,
            growthService,
            referenceLane,
            new MerchantKnowledgeCurationService(
                dbContext,
                Options.Create(new MerchantCurationOptions()),
                NullLogger<MerchantKnowledgeCurationService>.Instance),
            Options.Create(new MerchantCategorizationOptions
            {
                BackfillOnGlobalSyncEnabled = true,
                MaxRowsPerRun = maxRowsPerRun
            }),
            NullLogger<MerchantCategorizationBackfillService>.Instance);
    }

    private sealed class ThrowingReferenceJudge : IReferenceRowJudge
    {
        public Task<MerchantCategoryJudgment> JudgeAsync(
            ReferenceRowJudgmentInput input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Reference judgment must not run when the lane is disabled.");
        }
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

    internal static Transaction CreateTransaction(
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

    internal static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"merchant-backfill-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    internal static async Task<(Guid UserId, Guid AccountId)> SeedUserWithAccountAsync(AppDbContext dbContext)
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
