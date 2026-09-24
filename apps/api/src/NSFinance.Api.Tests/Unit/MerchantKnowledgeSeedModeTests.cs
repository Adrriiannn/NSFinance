using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;
using static NSFinance.Api.Tests.Unit.MerchantCategorizationBackfillTests;

namespace NSFinance.Api.Tests.Unit;

// The SeedApply modes step a new catalog version through the live system
// (CAT-001 v6 safety): Off changes nothing, DryRun only measures, Pilot
// shows the pending version to named users without persisting knowledge,
// Apply writes it once under the seed-run ledger, Revert undoes it exactly.
public sealed class MerchantKnowledgeSeedModeTests
{
    [Fact]
    public async Task Off_LeavesTheKnowledgeBaseUntouched_AndKeepsCategorizingFromLiveRows()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("TESCO", 130, 13010, null),
            PriorVersionSeed("NETFLIX", 280, 28010, null));
        dbContext.MerchantKnowledgeCandidates.Add(ParkedCandidate());
        var tesco = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 3", -20m, now);
        var netflix = CreateTransaction(seeded.AccountId, "NETFLIX.COM", -12.99m, now);
        var tuition = CreateTransaction(seeded.AccountId, "UCD TUITION", -3000m, now);
        dbContext.Transactions.AddRange(tesco, netflix, tuition);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, seedMode: MerchantKnowledgeSeedMode.Off)
            .BackfillAsync(seeded.UserId, CancellationToken.None);

        Assert.Equal(2, await dbContext.MerchantKnowledge.CountAsync());
        Assert.Empty(await dbContext.MerchantKnowledgeSeedRuns.ToListAsync());
        Assert.Equal(
            MerchantKnowledgeCandidateStatuses.NeedsReview,
            (await dbContext.MerchantKnowledgeCandidates.SingleAsync()).Status);

        Assert.Equal(13010, (await Reload(dbContext, tesco)).TaxonomyCategoryId);
        var reloadedNetflix = await Reload(dbContext, netflix);
        Assert.Equal(28010, reloadedNetflix.TaxonomyCategoryId);
        Assert.Null(reloadedNetflix.TaxonomySubcategoryId);
        Assert.Null((await Reload(dbContext, tuition)).TaxonomyCategoryId);
    }

    [Fact]
    public async Task DryRun_MeasuresWithoutWriting()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        dbContext.MerchantKnowledge.Add(PriorVersionSeed("NETFLIX", 280, 28010, null));
        dbContext.MerchantKnowledgeCandidates.Add(ParkedCandidate());
        var tuition = CreateTransaction(seeded.AccountId, "UCD TUITION", -3000m, DateTime.UtcNow);
        dbContext.Transactions.Add(tuition);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, seedMode: MerchantKnowledgeSeedMode.DryRun)
            .BackfillAsync(seeded.UserId, CancellationToken.None);

        var netflixSeed = await dbContext.MerchantKnowledge.SingleAsync();
        Assert.Null(netflixSeed.TaxonomySubcategoryId);
        Assert.Empty(await dbContext.MerchantKnowledgeSeedRuns.ToListAsync());
        Assert.Equal(
            MerchantKnowledgeCandidateStatuses.NeedsReview,
            (await dbContext.MerchantKnowledgeCandidates.SingleAsync()).Status);
        Assert.Null((await Reload(dbContext, tuition)).TaxonomyCategoryId);
    }

    [Fact]
    public async Task Pilot_ShowsThePendingVersionToPilotUsersOnly_WithoutPersistingKnowledge()
    {
        await using var dbContext = CreateDbContext();
        var pilot = await SeedUserWithAccountAsync(dbContext);
        var other = await SeedUserWithAccountAsync(dbContext);
        var now = DateTime.UtcNow;

        // Live v5-era rows: a category-level brand the pending version
        // refines, and a bare token the pending version retires.
        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("NETFLIX", 280, 28010, null),
            PriorVersionSeed("MACE", 130, 13010, 130102));
        dbContext.MerchantKnowledgeCandidates.Add(ParkedCandidate());

        var pilotNetflix = CreateTransaction(pilot.AccountId, "NETFLIX.COM", -12.99m, now);
        var pilotTuition = CreateTransaction(pilot.AccountId, "UCD TUITION", -3000m, now);
        var pilotPharma = CreateTransaction(pilot.AccountId, "PHARMACEUTICAL SOCIETY", -150m, now);
        var otherNetflix = CreateTransaction(other.AccountId, "NETFLIX.COM", -12.99m, now);
        var otherTuition = CreateTransaction(other.AccountId, "UCD TUITION", -3000m, now);
        var otherPharma = CreateTransaction(other.AccountId, "PHARMACEUTICAL SOCIETY", -150m, now);
        dbContext.Transactions.AddRange(pilotNetflix, pilotTuition, pilotPharma, otherNetflix, otherTuition, otherPharma);
        await dbContext.SaveChangesAsync();

        foreach (var userId in new[] { pilot.UserId, other.UserId })
        {
            await CreateService(dbContext, seedMode: MerchantKnowledgeSeedMode.Pilot, pilotUserIds: [pilot.UserId])
                .BackfillAsync(userId, CancellationToken.None);
        }

        // The pilot user sees the pending version: refined brand, new
        // signal, retired token - stamped with the pending version.
        var reloadedPilotNetflix = await Reload(dbContext, pilotNetflix);
        Assert.Equal(280101, reloadedPilotNetflix.TaxonomySubcategoryId);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, reloadedPilotNetflix.CategorizationCharacteristicsVersion);
        Assert.Equal(26010, (await Reload(dbContext, pilotTuition)).TaxonomyCategoryId);
        Assert.Null((await Reload(dbContext, pilotPharma)).TaxonomyCategoryId);

        // Everyone else keeps the live knowledge base, misfires included.
        Assert.Null((await Reload(dbContext, otherNetflix)).TaxonomySubcategoryId);
        Assert.Null((await Reload(dbContext, otherTuition)).TaxonomyCategoryId);
        Assert.Equal(130102, (await Reload(dbContext, otherPharma)).TaxonomySubcategoryId);

        // Nothing about the knowledge base itself changed.
        Assert.Equal(2, await dbContext.MerchantKnowledge.CountAsync());
        Assert.Null((await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "NETFLIX")).TaxonomySubcategoryId);
        Assert.True((await dbContext.MerchantKnowledge.SingleAsync(x => x.NormalizedPattern == "MACE")).IsActive);
        Assert.Empty(await dbContext.MerchantKnowledgeSeedRuns.ToListAsync());
        Assert.Equal(
            MerchantKnowledgeCandidateStatuses.NeedsReview,
            (await dbContext.MerchantKnowledgeCandidates.SingleAsync()).Status);
    }

    [Fact]
    public async Task Apply_ThenRevert_RestoresTheKnowledgeBaseExactly_AndDoesNotReapply()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var priorVersion = CategoryCharacteristicsCatalog.Version - 1;

        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("NETFLIX", 280, 28010, null),
            PriorVersionSeed("MAINTENANCE", 200, 20040, 200407));
        var parked = ParkedCandidate();
        var parkedUntil = parked.NextEligibleUtc;
        dbContext.MerchantKnowledgeCandidates.Add(parked);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, seedMode: MerchantKnowledgeSeedMode.Apply)
            .BackfillAsync(seeded.UserId, CancellationToken.None);

        var applied = await dbContext.MerchantKnowledgeSeedRuns.SingleAsync();
        Assert.Equal(MerchantKnowledgeSeedRunStatuses.Applied, applied.Status);
        Assert.Equal(MerchantKnowledgeSeedPlan.Current.Hash, applied.PlanHash);
        Assert.Equal(1, applied.RetargetedCount);
        Assert.Equal(1, applied.DeactivatedCount);
        Assert.Equal(1, applied.ReopenedCount);
        Assert.Equal(MerchantKnowledgeSeedPlan.Current.Entries.Count - 1, applied.InsertedCount);
        Assert.Equal(280101, (await Knowledge(dbContext, "NETFLIX")).TaxonomySubcategoryId);
        Assert.False((await Knowledge(dbContext, "MAINTENANCE")).IsActive);

        await CreateService(dbContext, seedMode: MerchantKnowledgeSeedMode.Revert)
            .BackfillAsync(seeded.UserId, CancellationToken.None);

        var reverted = await dbContext.MerchantKnowledgeSeedRuns.SingleAsync();
        Assert.Equal(MerchantKnowledgeSeedRunStatuses.Reverted, reverted.Status);
        Assert.NotNull(reverted.RevertedUtc);

        var netflix = await Knowledge(dbContext, "NETFLIX");
        Assert.Null(netflix.TaxonomySubcategoryId);
        Assert.Equal(28010, netflix.TaxonomyCategoryId);
        Assert.Equal(priorVersion, netflix.CharacteristicsVersion);
        Assert.True(netflix.IsActive);
        Assert.True((await Knowledge(dbContext, "MAINTENANCE")).IsActive);

        // Inserted rows stay on record, inactive, so the only active global
        // rows are the two that existed before the version was applied.
        Assert.False((await Knowledge(dbContext, "TUITION")).IsActive);
        Assert.Equal(2, await dbContext.MerchantKnowledge.CountAsync(x => x.IsActive));

        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.NeedsReview, candidate.Status);
        Assert.Equal("judgment_abstained", candidate.LastOutcomeCode);
        Assert.Equal(parkedUntil, candidate.NextEligibleUtc);

        // A reverted version is not re-applied by flipping back to Apply.
        await CreateService(dbContext, seedMode: MerchantKnowledgeSeedMode.Apply)
            .BackfillAsync(seeded.UserId, CancellationToken.None);
        Assert.Equal(2, await dbContext.MerchantKnowledge.CountAsync(x => x.IsActive));
        Assert.Equal(MerchantKnowledgeSeedRunStatuses.Reverted, (await dbContext.MerchantKnowledgeSeedRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Apply_LeavesDuplicateGlobalPatternsAlone_InsteadOfFailingTheBackfill()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);

        // Two global rows for one pattern: the pre-ledger race could leave
        // these behind. Seeding used to throw on them and stop every backfill.
        dbContext.MerchantKnowledge.AddRange(
            PriorVersionSeed("NETFLIX", 280, 28010, null),
            PriorVersionSeed("NETFLIX", 280, 28010, null));
        var tuition = CreateTransaction(seeded.AccountId, "UCD TUITION", -3000m, DateTime.UtcNow);
        dbContext.Transactions.Add(tuition);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        var run = await dbContext.MerchantKnowledgeSeedRuns.SingleAsync();
        Assert.Equal(1, run.SkippedDuplicateCount);
        Assert.All(
            await dbContext.MerchantKnowledge.Where(x => x.NormalizedPattern == "NETFLIX").ToListAsync(),
            row => Assert.Null(row.TaxonomySubcategoryId));
        Assert.Equal(26010, (await Reload(dbContext, tuition)).TaxonomyCategoryId);
    }

    [Fact]
    public async Task Apply_LosingAConcurrentPass_DiscardsItsChanges_AndStillCategorizes()
    {
        var databaseName = $"seed-race-{Guid.NewGuid()}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var winner = new ConcurrentWinnerInterceptor(databaseName, databaseRoot);
        await using var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .AddInterceptors(winner)
            .Options);
        var seeded = await SeedUserWithAccountAsync(dbContext);
        dbContext.MerchantKnowledge.Add(PriorVersionSeed("TESCO", 130, 13010, null));
        var tesco = CreateTransaction(seeded.AccountId, "VDC-TESCO STORES 3", -20m, DateTime.UtcNow);
        dbContext.Transactions.Add(tesco);
        await dbContext.SaveChangesAsync();

        var summary = await CreateService(dbContext).BackfillAsync(seeded.UserId, CancellationToken.None);

        Assert.True(winner.Fired);
        var run = await dbContext.MerchantKnowledgeSeedRuns.AsNoTracking().SingleAsync();
        Assert.Equal("winner", run.PlanHash);
        Assert.Equal(1, await dbContext.MerchantKnowledge.CountAsync());
        Assert.Equal(1, summary.RowsCategorized);
        Assert.Equal(13010, (await Reload(dbContext, tesco)).TaxonomyCategoryId);
    }

    private static MerchantKnowledgeCandidate ParkedCandidate()
    {
        var past = DateTime.UtcNow.AddHours(-2);
        return new MerchantKnowledgeCandidate
        {
            Id = Guid.NewGuid(),
            NormalizedDescriptor = "TEBEX",
            RawDescriptorSample = "TEBEX.ORG",
            Status = MerchantKnowledgeCandidateStatuses.NeedsReview,
            ObservedOccurrences = 2,
            ObservedSpendAbs = 60m,
            ObservedDirection = "outflow",
            AttemptCount = 3,
            NextEligibleUtc = past.AddDays(3),
            LastOutcomeCode = "judgment_abstained",
            CreatedUtc = past,
            UpdatedUtc = past
        };
    }

    private static Task<Transaction> Reload(AppDbContext dbContext, Transaction transaction)
    {
        return dbContext.Transactions.SingleAsync(x => x.Id == transaction.Id);
    }

    private static Task<MerchantKnowledge> Knowledge(AppDbContext dbContext, string pattern)
    {
        return dbContext.MerchantKnowledge.SingleAsync(x => x.UserId == null && x.NormalizedPattern == pattern);
    }

    // Simulates another sync applying the same version first: just before
    // this pass saves its ledger row, the winner's row lands and this pass's
    // save fails the way the unique version index fails it in PostgreSQL.
    private sealed class ConcurrentWinnerInterceptor(string databaseName, InMemoryDatabaseRoot databaseRoot)
        : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var losing = eventData.Context!.ChangeTracker.Entries<MerchantKnowledgeSeedRun>()
                .Any(e => e.State == EntityState.Added);
            if (!losing || Fired)
            {
                return result;
            }

            Fired = true;
            await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName, databaseRoot)
                .Options);
            other.MerchantKnowledgeSeedRuns.Add(new MerchantKnowledgeSeedRun
            {
                Id = Guid.NewGuid(),
                CharacteristicsVersion = CategoryCharacteristicsCatalog.Version,
                PlanHash = "winner",
                ChangesJson = "{}",
                AppliedUtc = DateTime.UtcNow
            });
            await other.SaveChangesAsync(cancellationToken);

            throw new DbUpdateException("duplicate key value violates unique constraint \"IX_MerchantKnowledgeSeedRuns_CharacteristicsVersion\"");
        }
    }
}
