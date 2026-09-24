using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;
using static NSFinance.Api.Tests.Unit.MerchantCategorizationBackfillTests;

namespace NSFinance.Api.Tests.Unit;

// Invariants over the catalog's seed plan (CAT-001 v6 safety): the plan is
// pinned per catalog version, its boundary spaces survive seeding, and known
// benign descriptors never land on the node a substring misfire used to
// pick.
public sealed class MerchantKnowledgeSeedPlanTests
{
    // One entry per shipped catalog version. Changing any seed signal, node,
    // or direction changes the hash: bump CategoryCharacteristicsCatalog.Version
    // and add the new hash here. Never edit the hash of a version that has
    // been deployed - production seeds each version exactly once.
    private static readonly Dictionary<int, string> PinnedPlanHashes = new()
    {
        [6] = "b7959e643529d728d523d47a94fdce456598dbb88dc76533a4ddbe71a9d598ba"
    };

    [Fact]
    public void SeedPlanHash_IsPinnedForTheCurrentCatalogVersion()
    {
        var version = CategoryCharacteristicsCatalog.Version;
        Assert.True(
            PinnedPlanHashes.ContainsKey(version),
            $"Catalog version {version} has no pinned seed-plan hash; record {MerchantKnowledgeSeedPlan.Current.Hash}.");
        Assert.True(
            PinnedPlanHashes[version] == MerchantKnowledgeSeedPlan.Current.Hash,
            $"The seed plan changed under catalog version {version}. Bump CategoryCharacteristicsCatalog.Version and pin {MerchantKnowledgeSeedPlan.Current.Hash}.");
        Assert.Equal(PinnedPlanHashes.Count, PinnedPlanHashes.Values.Distinct().Count());
    }

    [Fact]
    public void SeedPlan_HasNoConflictingClaims()
    {
        Assert.Empty(MerchantKnowledgeSeedPlan.Current.Conflicts);
    }

    [Fact]
    public void SeedPlan_KeepsDeliberateBoundarySpaces()
    {
        var patterns = MerchantKnowledgeSeedPlan.Current.Entries
            .Select(e => e.Pattern)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(" BAR ", patterns);
        Assert.Contains("PUB ", patterns);
        Assert.Contains(" MACE ", patterns);
        Assert.DoesNotContain("BAR", patterns);
        Assert.DoesNotContain("PUB", patterns);
        Assert.DoesNotContain("MACE", patterns);

        var bar = MerchantKnowledgeSeedPlan.Current.Entries.Single(e => e.Pattern == " BAR ");
        Assert.Equal("BAR", bar.DisplayName);
    }

    [Theory]
    [InlineData("VDP-REPUBLIC OF WORK", 13030)]
    [InlineData("BARNARDOS CHARITY SHOP", 13030)]
    [InlineData("BARGAIN TOWN", 13030)]
    [InlineData("BARCLAYCARD PAYMENT", 13030)]
    [InlineData("FRONTIER COMMUNICATIONS", 12060)]
    [InlineData("PHARMACEUTICAL SOCIETY", 13010)]
    [InlineData("SAMSUNG GALAXY STORE", 13010)]
    [InlineData("DRUGSTORE", 100508)]
    [InlineData("GARDEN MAINTENANCE LTD", 200407)]
    [InlineData("CAR MAINTENANCE CENTRE", 200407)]
    [InlineData("SPACE NK", 190107)]
    [InlineData("SPAGHETTI HOUSE", 190107)]
    [InlineData("EUROSPAR", 190107)]
    [InlineData("HUMMUS KITCHEN", 170204)]
    [InlineData("SIXTY SIX", 120804)]
    [InlineData("FX COMMISSION", 300304)]
    [InlineData("ADMISSION TICKET", 300304)]
    [InlineData("SUBMISSION FEE", 300304)]
    [InlineData("USAGE CHARGE", 290301)]
    [InlineData("SAUSAGE ROLL CO", 290301)]
    [InlineData("MASSAGE THERAPY CLINIC", 290301)]
    [InlineData("MESSAGE UK LTD", 290301)]
    [InlineData("MINECRAFT", 210302)]
    [InlineData("CRAFT BEER CO", 210302)]
    [InlineData("BETSYS CAFE", 230602)]
    [InlineData("SEASON TICKET", 210501)]
    [InlineData("HOTEL RETREAT SPA", 300302)]
    public async Task CollisionCorpus_BenignDescriptors_NeverLandOnTheMisfireNode(string descriptor, int misfireNodeId)
    {
        var reloaded = await CategorizeOnFreshSeedAsync(descriptor);

        var landedNode = misfireNodeId >= 100000 ? reloaded.TaxonomySubcategoryId : reloaded.TaxonomyCategoryId;
        Assert.NotEqual(misfireNodeId, landedNode);
    }

    [Theory]
    [InlineData("VDC-O'NEILLS BAR DUBLIN", 13030)]
    [InlineData("BAR 1661", 13030)]
    [InlineData("THE LOCAL PUB", 13030)]
    [InlineData("MACE DRUMCONDRA", 130102)]
    [InlineData("EASON O'CONNELL ST", 210501)]
    [InlineData("ETSY.COM", 230602)]
    [InlineData("SIXT RENT A CAR", 120804)]
    [InlineData("CHILD MAINTENANCE PAYMENT", 200407)]
    [InlineData("SAGE UK LTD", 290301)]
    public async Task BoundaryPatterns_StillMatchTheWholeWord_AtEitherEdge(string descriptor, int expectedNodeId)
    {
        var reloaded = await CategorizeOnFreshSeedAsync(descriptor);

        var landedNode = expectedNodeId >= 100000 ? reloaded.TaxonomySubcategoryId : reloaded.TaxonomyCategoryId;
        Assert.Equal(expectedNodeId, landedNode);
    }

    private static async Task<Transaction> CategorizeOnFreshSeedAsync(string descriptor)
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var transaction = CreateTransaction(seeded.AccountId, descriptor, -25m, DateTime.UtcNow);
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        return await dbContext.Transactions.AsNoTracking().SingleAsync(x => x.Id == transaction.Id);
    }
}
