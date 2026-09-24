using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

public enum MerchantKnowledgeSeedMode
{
    Off,
    DryRun,
    Pilot,
    Apply,
    Revert
}

public sealed class MerchantKnowledgeSeedOptions
{
    public const string SectionName = "Categorization:SeedApply";

    // Off by default: deploying a new catalog version changes nothing until
    // an operator steps it through DryRun, Pilot, and Apply. Revert undoes an
    // applied version's knowledge changes from its ledger record.
    public MerchantKnowledgeSeedMode Mode { get; set; } = MerchantKnowledgeSeedMode.Off;

    // Users whose syncs match against a pending version's seed changes while
    // the mode is Pilot. Everyone else keeps the live knowledge base.
    public List<Guid> PilotUserIds { get; set; } = [];
}

// What a pilot user's backfill matches against: the live rows, minus the
// rows a pending version would retarget or deactivate, plus the rows it
// would write. Nothing here is persisted.
public sealed record MerchantKnowledgeSeedOverlay(
    IReadOnlySet<Guid> HiddenRowIds,
    IReadOnlyList<MerchantKnowledge> Rows)
{
    public List<MerchantKnowledge> ApplyTo(IEnumerable<MerchantKnowledge> knowledge)
    {
        return knowledge.Where(k => !HiddenRowIds.Contains(k.Id)).Concat(Rows).ToList();
    }
}

// Brings MerchantKnowledge up to the characteristics catalog's seed plan,
// exactly once per catalog version and only as far as the configured mode
// allows (CAT-001 v6 safety). The seed-run ledger decides whether a version
// is done and records every change for an exact knowledge revert.
// AI-researched and correction-derived rows are never rewritten by the
// catalog.
public sealed class MerchantKnowledgeSeedService(
    AppDbContext dbContext,
    IOptions<MerchantKnowledgeSeedOptions> options,
    ILogger<MerchantKnowledgeSeedService> logger)
{
    public const string ReopenedOutcomeCode = "reopened_by_catalog_version";

    private static readonly JsonSerializerOptions ChangesJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MerchantKnowledgeSeedOverlay?> PrepareAsync(Guid userId, CancellationToken cancellationToken)
    {
        var version = CategoryCharacteristicsCatalog.Version;
        var plan = MerchantKnowledgeSeedPlan.Current;
        var mode = options.Value.Mode;

        var run = await dbContext.MerchantKnowledgeSeedRuns
            .SingleOrDefaultAsync(x => x.CharacteristicsVersion == version, cancellationToken);

        if (run is not null)
        {
            if (!string.Equals(run.PlanHash, plan.Hash, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Merchant knowledge seed plan changed without a catalog version bump characteristicsVersion={Version} recordedPlanHash={RecordedHash} currentPlanHash={CurrentHash}",
                    version,
                    run.PlanHash,
                    plan.Hash);
            }

            if (mode == MerchantKnowledgeSeedMode.Revert
                && run.Status == MerchantKnowledgeSeedRunStatuses.Applied)
            {
                await RevertAsync(run, cancellationToken);
            }
            else if (mode == MerchantKnowledgeSeedMode.Apply
                && run.Status == MerchantKnowledgeSeedRunStatuses.Reverted)
            {
                logger.LogInformation(
                    "Merchant knowledge seed for characteristicsVersion={Version} was reverted; re-applying requires a new catalog version",
                    version);
            }

            return null;
        }

        switch (mode)
        {
            case MerchantKnowledgeSeedMode.DryRun:
            {
                var delta = await ComputeDeltaAsync(plan, tracked: false, cancellationToken);
                LogDelta(mode, version, plan, delta);
                return null;
            }

            case MerchantKnowledgeSeedMode.Pilot:
            {
                var delta = await ComputeDeltaAsync(plan, tracked: false, cancellationToken);
                LogDelta(mode, version, plan, delta);
                return options.Value.PilotUserIds.Contains(userId)
                    ? BuildOverlay(delta, version)
                    : null;
            }

            case MerchantKnowledgeSeedMode.Apply:
                await ApplyAsync(plan, version, cancellationToken);
                return null;

            default:
                logger.LogInformation(
                    "Merchant knowledge seed pending characteristicsVersion={Version} planHash={PlanHash} mode={Mode}",
                    version,
                    plan.Hash,
                    mode);
                return null;
        }
    }

    private async Task ApplyAsync(MerchantKnowledgeSeedPlan plan, int version, CancellationToken cancellationToken)
    {
        var delta = await ComputeDeltaAsync(plan, tracked: true, cancellationToken);
        var now = DateTime.UtcNow;
        var changes = new SeedRunChanges();

        foreach (var entry in delta.Inserts)
        {
            var row = NewSeedRow(entry, version, now);
            dbContext.MerchantKnowledge.Add(row);
            changes.Inserted.Add(new SeedRowRef(row.Id, row.NormalizedPattern));
        }

        foreach (var (row, target) in delta.Retargets)
        {
            changes.Retargeted.Add(new SeedRowPrior(
                row.Id,
                row.NormalizedPattern,
                row.DisplayName,
                row.TaxonomyDomainId,
                row.TaxonomyCategoryId,
                row.TaxonomySubcategoryId,
                row.DirectionExpectation,
                row.CharacteristicsVersion,
                row.IsActive));

            row.DisplayName = target.DisplayName;
            row.TaxonomyDomainId = target.DomainId;
            row.TaxonomyCategoryId = target.CategoryId;
            row.TaxonomySubcategoryId = target.SubcategoryId;
            row.DirectionExpectation = target.Direction;
            row.CharacteristicsVersion = version;
            row.IsActive = true;
            row.UpdatedUtc = now;
        }

        // A seed the catalog no longer carries stops matching; it is kept,
        // inactive, so evidence on earlier assignments still resolves.
        foreach (var row in delta.Deactivations)
        {
            changes.Deactivated.Add(new SeedRowRef(row.Id, row.NormalizedPattern));
            row.IsActive = false;
            row.UpdatedUtc = now;
        }

        // A new catalog version can supply the definition a parked candidate
        // was waiting for - re-open the review queue for another judgment,
        // exactly once per version.
        var reopened = await dbContext.MerchantKnowledgeCandidates
            .Where(x => x.Status == MerchantKnowledgeCandidateStatuses.NeedsReview)
            .ToListAsync(cancellationToken);
        foreach (var candidate in reopened)
        {
            changes.Reopened.Add(new CandidatePrior(
                candidate.Id,
                candidate.Status,
                candidate.LastOutcomeCode,
                candidate.NextEligibleUtc));

            candidate.Status = MerchantKnowledgeCandidateStatuses.Pending;
            candidate.NextEligibleUtc = null;
            candidate.LastOutcomeCode = ReopenedOutcomeCode;
            candidate.UpdatedUtc = now;
        }

        var run = new MerchantKnowledgeSeedRun
        {
            Id = Guid.NewGuid(),
            CharacteristicsVersion = version,
            PlanHash = plan.Hash,
            Status = MerchantKnowledgeSeedRunStatuses.Applied,
            InsertedCount = changes.Inserted.Count,
            RetargetedCount = changes.Retargeted.Count,
            DeactivatedCount = changes.Deactivated.Count,
            ReopenedCount = changes.Reopened.Count,
            SkippedDuplicateCount = delta.SkippedDuplicatePatterns.Count,
            ChangesJson = JsonSerializer.Serialize(changes, ChangesJsonOptions),
            AppliedUtc = now
        };
        dbContext.MerchantKnowledgeSeedRuns.Add(run);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Another sync applied this version first: the unique version
            // rejected this pass as a whole, so discard its tracked changes
            // and carry on with the knowledge the winner wrote.
            dbContext.ChangeTracker.Clear();
            var recordedByOtherPass = await dbContext.MerchantKnowledgeSeedRuns
                .AsNoTracking()
                .AnyAsync(x => x.CharacteristicsVersion == version, cancellationToken);
            if (!recordedByOtherPass)
            {
                throw;
            }

            logger.LogWarning(
                exception,
                "Merchant knowledge seed for characteristicsVersion={Version} was applied by a concurrent pass; this pass's changes were discarded",
                version);
            return;
        }

        foreach (var conflict in plan.Conflicts)
        {
            logger.LogWarning(
                "Merchant knowledge seed conflict pattern={Pattern} keeps categoryId={KeptCategoryId} over categoryId={DroppedCategoryId}",
                conflict.Pattern,
                conflict.KeptCategoryId,
                conflict.DroppedCategoryId);
        }

        logger.LogInformation(
            "Merchant knowledge seeded characteristicsVersion={Version} planHash={PlanHash} inserted={Inserted} retargeted={Retargeted} deactivated={Deactivated} reopenedCandidates={Reopened} skippedDuplicatePatterns={Skipped}",
            version,
            plan.Hash,
            run.InsertedCount,
            run.RetargetedCount,
            run.DeactivatedCount,
            run.ReopenedCount,
            run.SkippedDuplicateCount);
    }

    private async Task RevertAsync(MerchantKnowledgeSeedRun run, CancellationToken cancellationToken)
    {
        var changes = JsonSerializer.Deserialize<SeedRunChanges>(run.ChangesJson, ChangesJsonOptions)
            ?? new SeedRunChanges();
        var now = DateTime.UtcNow;

        var rowIds = changes.Inserted.Select(x => x.Id)
            .Concat(changes.Retargeted.Select(x => x.Id))
            .Concat(changes.Deactivated.Select(x => x.Id))
            .ToList();
        var rows = await dbContext.MerchantKnowledge
            .Where(x => rowIds.Contains(x.Id) && x.Source == MerchantKnowledgeSources.Seed)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var inserted in changes.Inserted)
        {
            if (rows.TryGetValue(inserted.Id, out var row))
            {
                row.IsActive = false;
                row.UpdatedUtc = now;
            }
        }

        foreach (var prior in changes.Retargeted)
        {
            if (rows.TryGetValue(prior.Id, out var row))
            {
                row.DisplayName = prior.DisplayName;
                row.TaxonomyDomainId = prior.DomainId;
                row.TaxonomyCategoryId = prior.CategoryId;
                row.TaxonomySubcategoryId = prior.SubcategoryId;
                row.DirectionExpectation = prior.Direction;
                row.CharacteristicsVersion = prior.CharacteristicsVersion;
                row.IsActive = prior.IsActive;
                row.UpdatedUtc = now;
            }
        }

        foreach (var deactivated in changes.Deactivated)
        {
            if (rows.TryGetValue(deactivated.Id, out var row))
            {
                row.IsActive = true;
                row.UpdatedUtc = now;
            }
        }

        // Only candidates still sitting where the version put them go back
        // to review; anything judged since keeps its newer outcome.
        var candidateIds = changes.Reopened.Select(x => x.Id).ToList();
        var candidates = await dbContext.MerchantKnowledgeCandidates
            .Where(x => candidateIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var reparked = 0;
        foreach (var prior in changes.Reopened)
        {
            if (candidates.TryGetValue(prior.Id, out var candidate)
                && candidate.Status == MerchantKnowledgeCandidateStatuses.Pending
                && candidate.LastOutcomeCode == ReopenedOutcomeCode)
            {
                candidate.Status = prior.Status;
                candidate.LastOutcomeCode = prior.LastOutcomeCode;
                candidate.NextEligibleUtc = prior.NextEligibleUtc;
                candidate.UpdatedUtc = now;
                reparked += 1;
            }
        }

        run.Status = MerchantKnowledgeSeedRunStatuses.Reverted;
        run.RevertedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Merchant knowledge seed reverted characteristicsVersion={Version} deactivatedInserted={Inserted} restoredRetargeted={Retargeted} reactivated={Reactivated} reparkedCandidates={Reparked}",
            run.CharacteristicsVersion,
            changes.Inserted.Count,
            changes.Retargeted.Count,
            changes.Deactivated.Count,
            reparked);
    }

    private async Task<SeedDelta> ComputeDeltaAsync(
        MerchantKnowledgeSeedPlan plan,
        bool tracked,
        CancellationToken cancellationToken)
    {
        // Global rows only: a user's personal override must never block or
        // be rewritten by the global seed.
        var query = dbContext.MerchantKnowledge.Where(x => x.UserId == null);
        var globalRows = tracked
            ? await query.ToListAsync(cancellationToken)
            : await query.AsNoTracking().ToListAsync(cancellationToken);

        var byPattern = globalRows
            .GroupBy(x => x.NormalizedPattern, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var planPatterns = plan.Entries.Select(e => e.Pattern).ToHashSet(StringComparer.Ordinal);

        var delta = new SeedDelta();
        foreach (var entry in plan.Entries)
        {
            if (!byPattern.TryGetValue(entry.Pattern, out var rows))
            {
                delta.Inserts.Add(entry);
                continue;
            }

            // Duplicate global rows for one pattern predate the ledger; which
            // one is authoritative is an operator decision, so leave both.
            if (rows.Count > 1)
            {
                delta.SkippedDuplicatePatterns.Add(entry.Pattern);
                continue;
            }

            var existing = rows[0];
            if (existing.Source != MerchantKnowledgeSources.Seed)
            {
                continue;
            }

            if (!existing.IsActive
                || existing.TaxonomyDomainId != entry.DomainId
                || existing.TaxonomyCategoryId != entry.CategoryId
                || existing.TaxonomySubcategoryId != entry.SubcategoryId
                || existing.DirectionExpectation != entry.Direction)
            {
                delta.Retargets.Add((existing, entry));
            }
        }

        delta.Deactivations.AddRange(globalRows.Where(x =>
            x.Source == MerchantKnowledgeSources.Seed
            && x.IsActive
            && !planPatterns.Contains(x.NormalizedPattern)));

        return delta;
    }

    private static MerchantKnowledgeSeedOverlay BuildOverlay(SeedDelta delta, int version)
    {
        var now = DateTime.UtcNow;
        var hidden = new HashSet<Guid>();
        var rows = new List<MerchantKnowledge>();

        foreach (var entry in delta.Inserts)
        {
            rows.Add(NewSeedRow(entry, version, now));
        }

        foreach (var (row, target) in delta.Retargets)
        {
            hidden.Add(row.Id);
            var replacement = NewSeedRow(target, version, now);
            replacement.Id = row.Id;
            rows.Add(replacement);
        }

        foreach (var row in delta.Deactivations)
        {
            hidden.Add(row.Id);
        }

        return new MerchantKnowledgeSeedOverlay(hidden, rows);
    }

    private static MerchantKnowledge NewSeedRow(MerchantKnowledgeSeedEntry entry, int version, DateTime now)
    {
        return new MerchantKnowledge
        {
            Id = Guid.NewGuid(),
            NormalizedPattern = entry.Pattern,
            DisplayName = entry.DisplayName,
            TaxonomyDomainId = entry.DomainId,
            TaxonomyCategoryId = entry.CategoryId,
            TaxonomySubcategoryId = entry.SubcategoryId,
            DirectionExpectation = entry.Direction,
            Source = MerchantKnowledgeSources.Seed,
            Confidence = 1.0,
            CharacteristicsVersion = version,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private void LogDelta(
        MerchantKnowledgeSeedMode mode,
        int version,
        MerchantKnowledgeSeedPlan plan,
        SeedDelta delta)
    {
        logger.LogInformation(
            "Merchant knowledge seed preview mode={Mode} characteristicsVersion={Version} planHash={PlanHash} inserts={Inserts} retargets={Retargets} deactivations={Deactivations} skippedDuplicatePatterns={Skipped} planConflicts={Conflicts}",
            mode,
            version,
            plan.Hash,
            delta.Inserts.Count,
            delta.Retargets.Count,
            delta.Deactivations.Count,
            delta.SkippedDuplicatePatterns.Count,
            plan.Conflicts.Count);
    }

    private sealed class SeedDelta
    {
        public List<MerchantKnowledgeSeedEntry> Inserts { get; } = [];
        public List<(MerchantKnowledge Row, MerchantKnowledgeSeedEntry Target)> Retargets { get; } = [];
        public List<MerchantKnowledge> Deactivations { get; } = [];
        public List<string> SkippedDuplicatePatterns { get; } = [];
    }

    private sealed class SeedRunChanges
    {
        public List<SeedRowRef> Inserted { get; set; } = [];
        public List<SeedRowPrior> Retargeted { get; set; } = [];
        public List<SeedRowRef> Deactivated { get; set; } = [];
        public List<CandidatePrior> Reopened { get; set; } = [];
    }

    private sealed record SeedRowRef(Guid Id, string Pattern);

    private sealed record SeedRowPrior(
        Guid Id,
        string Pattern,
        string DisplayName,
        int? DomainId,
        int? CategoryId,
        int? SubcategoryId,
        string Direction,
        int CharacteristicsVersion,
        bool IsActive);

    private sealed record CandidatePrior(
        Guid Id,
        string Status,
        string? LastOutcomeCode,
        DateTime? NextEligibleUtc);
}
