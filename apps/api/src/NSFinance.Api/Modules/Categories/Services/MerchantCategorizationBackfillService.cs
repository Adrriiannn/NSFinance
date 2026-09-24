using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

public sealed class MerchantCategorizationOptions
{
    public const string SectionName = "Categorization";

    // Reversible flag for the CAT-001 deterministic pass: when enabled, a
    // user-triggered global sync backfills merchant categories onto that
    // user's uncategorized ordinary transactions.
    public bool BackfillOnGlobalSyncEnabled { get; set; }

    public int MaxRowsPerRun { get; set; } = 500;
}

public sealed record MerchantBackfillSummary(
    int RowsExamined,
    int RowsCategorized,
    int RowsUnmatched,
    MerchantGrowthRunSummary? Growth = null,
    ReferenceLaneRunSummary? ReferenceLane = null);

// Deterministic merchant categorization backfill (CAT-001). Strictly additive:
// only rows with no taxonomy at all are considered, rows claimed by the
// relationship engine are never touched, and matches write the full
// domain/category/subcategory triple resolved through the validated taxonomy
// catalog. Every assignment logs its rule evidence without statement text.
public sealed class MerchantCategorizationBackfillService(
    AppDbContext dbContext,
    MerchantKnowledgeSeedService seedService,
    MerchantKnowledgeGrowthService growthService,
    ReferenceLaneAssignmentService referenceLaneService,
    MerchantKnowledgeCurationService curationService,
    IOptions<MerchantCategorizationOptions> options,
    ILogger<MerchantCategorizationBackfillService> logger)
{
    public bool IsEnabled => options.Value.BackfillOnGlobalSyncEnabled;

    public async Task<MerchantBackfillSummary> BackfillAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Clamp(options.Value.MaxRowsPerRun, 1, 2000);

        // Seeds the knowledge base from the catalog as far as the seed mode
        // allows; a pilot user gets the pending version as an overlay.
        var overlay = await seedService.PrepareAsync(userId, cancellationToken);

        var liveKnowledge = await dbContext.MerchantKnowledge
            .AsNoTracking()
            .Where(x => x.IsActive && (x.UserId == null || x.UserId == userId))
            .ToListAsync(cancellationToken);
        var knowledge = overlay is null ? liveKnowledge : overlay.ApplyTo(liveKnowledge);

        var candidates = await dbContext.Transactions
            .Where(x =>
                x.FinancialAccount != null
                && x.FinancialAccount.UserId == userId
                && x.TaxonomyDomainId == null
                && x.TaxonomyCategoryId == null
                && x.TaxonomySubcategoryId == null
                && x.DeterministicRelationshipType == null
                && x.AnalyticsTreatment == TransactionAnalyticsTreatments.Ordinary)
            .OrderByDescending(x => x.BookedAtUtc)
            .Take(maxRows)
            .ToListAsync(cancellationToken);

        var categorized = 0;
        var unmatched = new List<Transaction>();
        // Usage accounting (phase-two curation): which knowledge rows earned
        // their keep this run.
        var matchedCounts = new Dictionary<Guid, int>();

        void ApplyMatch(Transaction transaction, MerchantKnowledge match)
        {
            matchedCounts[match.Id] = matchedCounts.GetValueOrDefault(match.Id) + 1;
            transaction.TaxonomyDomainId = match.TaxonomyDomainId;
            transaction.TaxonomyCategoryId = match.TaxonomyCategoryId;
            transaction.TaxonomySubcategoryId = match.TaxonomySubcategoryId;
            transaction.CategorizationRuleKey = "merchant_knowledge";
            transaction.CategorizationSignal = match.NormalizedPattern;
            transaction.CategorizationCharacteristicsVersion = match.CharacteristicsVersion;
            transaction.CategorizedUtc = DateTime.UtcNow;

            categorized += 1;
            logger.LogInformation(
                "Merchant categorization assigned transactionId={TransactionId} ruleKey=merchant_knowledge pattern={Pattern} source={Source} characteristicsVersion={Version} domainId={DomainId} categoryId={CategoryId} subcategoryId={SubcategoryId}",
                transaction.Id,
                match.NormalizedPattern,
                match.Source,
                match.CharacteristicsVersion,
                transaction.TaxonomyDomainId,
                transaction.TaxonomyCategoryId,
                transaction.TaxonomySubcategoryId);
        }

        foreach (var transaction in candidates)
        {
            var match = MatchAgainstKnowledge(knowledge, transaction.Description, transaction.Amount);
            if (match is null)
            {
                unmatched.Add(transaction);
                continue;
            }

            ApplyMatch(transaction, match);
        }

        // The growth loop: unknown descriptors go through AI investigation,
        // integrity checks, and category judgment; promotions land in
        // MerchantKnowledge and are applied to this run's rows immediately.
        MerchantGrowthRunSummary? growthSummary = null;
        if (growthService.IsEnabled && unmatched.Count > 0)
        {
            growthSummary = await growthService.GrowAsync(unmatched, cancellationToken);
            if (growthSummary.Promoted > 0)
            {
                var grownKnowledge = await dbContext.MerchantKnowledge
                    .AsNoTracking()
                    .Where(x => x.IsActive && x.Source == MerchantKnowledgeSources.AiInvestigation)
                    .ToListAsync(cancellationToken);

                foreach (var transaction in unmatched.Where(x => x.TaxonomyCategoryId == null))
                {
                    var match = MatchAgainstKnowledge(grownKnowledge, transaction.Description, transaction.Amount);
                    if (match is not null)
                    {
                        ApplyMatch(transaction, match);
                    }
                }
            }
        }

        // The reference lane: rows still uncategorized after the merchant
        // passes that ride P2P rails get a per-row constrained judgment.
        // The lane persists its own writes and ledger rows.
        ReferenceLaneRunSummary? referenceLaneSummary = null;
        var stillUncategorized = unmatched.Where(x => x.TaxonomyCategoryId == null).ToList();
        if (referenceLaneService.IsEnabled && stillUncategorized.Count > 0)
        {
            referenceLaneSummary = await referenceLaneService.AssignAsync(userId, stillUncategorized, cancellationToken);
        }

        if (matchedCounts.Count > 0)
        {
            var matchedIds = matchedCounts.Keys.ToList();
            var usageUtc = DateTime.UtcNow;
            var matchedRows = await dbContext.MerchantKnowledge
                .Where(k => matchedIds.Contains(k.Id))
                .ToListAsync(cancellationToken);
            foreach (var row in matchedRows)
            {
                row.MatchCount += matchedCounts[row.Id];
                row.LastMatchedUtc = usageUtc;
            }
        }

        if (categorized > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Phase-two curation checks ride the same run, each behind its own
        // kill-switch.
        if (curationService.IsConflictDetectionEnabled)
        {
            await curationService.DetectCorrectionConflictsAsync(cancellationToken);
        }

        var summary = new MerchantBackfillSummary(
            RowsExamined: candidates.Count,
            RowsCategorized: categorized + (referenceLaneSummary?.Assigned ?? 0),
            RowsUnmatched: candidates.Count - categorized - (referenceLaneSummary?.Assigned ?? 0),
            Growth: growthSummary,
            ReferenceLane: referenceLaneSummary);

        logger.LogInformation(
            "Merchant categorization backfill userId={UserId} rowsExamined={RowsExamined} rowsCategorized={RowsCategorized} rowsUnmatched={RowsUnmatched} characteristicsVersion={Version}",
            userId,
            summary.RowsExamined,
            summary.RowsCategorized,
            summary.RowsUnmatched,
            CategoryCharacteristicsCatalog.Version);

        return summary;
    }

    private static MerchantKnowledge? MatchAgainstKnowledge(
        IReadOnlyList<MerchantKnowledge> knowledge,
        string rawDescription,
        decimal amount)
    {
        if (DeterministicMerchantCategorizer.NormalizeStatementText(rawDescription).Length < 2)
        {
            return null;
        }

        // Padded so a boundary pattern like " BAR " matches the word BAR at
        // either edge; unpadded patterns match exactly as before.
        var haystack = MerchantKnowledgeSeedPlan.MatchHaystack(rawDescription);
        MerchantKnowledge? best = null;

        foreach (var entry in knowledge)
        {
            var directionSatisfied = entry.DirectionExpectation switch
            {
                "outflow" => amount < 0,
                "inflow" => amount > 0,
                _ => true
            };

            if (!directionSatisfied
                || !haystack.Contains(entry.NormalizedPattern, StringComparison.Ordinal))
            {
                continue;
            }

            // A user's own correction always beats global knowledge; within
            // the same scope the longest pattern wins.
            if (best is null
                || (entry.UserId is not null && best.UserId is null)
                || (entry.UserId is null == best.UserId is null
                    && entry.NormalizedPattern.Length > best.NormalizedPattern.Length))
            {
                best = entry;
            }
        }

        return best;
    }
}
