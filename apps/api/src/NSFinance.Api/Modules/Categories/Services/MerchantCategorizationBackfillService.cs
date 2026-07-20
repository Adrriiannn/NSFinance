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
    MerchantGrowthRunSummary? Growth = null);

// Deterministic merchant categorization backfill (CAT-001). Strictly additive:
// only rows with no taxonomy at all are considered, rows claimed by the
// relationship engine are never touched, and matches write the full
// domain/category/subcategory triple resolved through the validated taxonomy
// catalog. Every assignment logs its rule evidence without statement text.
public sealed class MerchantCategorizationBackfillService(
    AppDbContext dbContext,
    MerchantKnowledgeGrowthService growthService,
    IOptions<MerchantCategorizationOptions> options,
    ILogger<MerchantCategorizationBackfillService> logger)
{
    public bool IsEnabled => options.Value.BackfillOnGlobalSyncEnabled;

    public async Task<MerchantBackfillSummary> BackfillAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Clamp(options.Value.MaxRowsPerRun, 1, 2000);

        await EnsureSeedKnowledgeAsync(cancellationToken);

        var knowledge = await dbContext.MerchantKnowledge
            .AsNoTracking()
            .Where(x => x.IsActive && (x.UserId == null || x.UserId == userId))
            .ToListAsync(cancellationToken);

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

        void ApplyMatch(Transaction transaction, MerchantKnowledge match)
        {
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

        if (categorized > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var summary = new MerchantBackfillSummary(
            RowsExamined: candidates.Count,
            RowsCategorized: categorized,
            RowsUnmatched: candidates.Count - categorized,
            Growth: growthSummary);

        logger.LogInformation(
            "Merchant categorization backfill userId={UserId} rowsExamined={RowsExamined} rowsCategorized={RowsCategorized} rowsUnmatched={RowsUnmatched} characteristicsVersion={Version}",
            userId,
            summary.RowsExamined,
            summary.RowsCategorized,
            summary.RowsUnmatched,
            CategoryCharacteristicsCatalog.Version);

        return summary;
    }

    // Seeds the knowledge base once per characteristics version from the
    // catalog's bootstrap signals, so the system starts knowing what the
    // contract's worked examples knew. All later growth comes from AI
    // investigation and user corrections, never from code changes.
    private async Task EnsureSeedKnowledgeAsync(CancellationToken cancellationToken)
    {
        var version = CategoryCharacteristicsCatalog.Version;
        var seedExists = await dbContext.MerchantKnowledge.AnyAsync(
            x => x.Source == MerchantKnowledgeSources.Seed && x.CharacteristicsVersion == version,
            cancellationToken);

        if (seedExists)
        {
            return;
        }

        // Dedup against global rows only: a user's personal override must
        // never block the global seed for everyone else.
        var existingPatterns = await dbContext.MerchantKnowledge
            .Where(x => x.UserId == null)
            .Select(x => x.NormalizedPattern)
            .ToListAsync(cancellationToken);
        var known = new HashSet<string>(existingPatterns, StringComparer.Ordinal);
        var catalog = NSFinanceTaxonomyCatalog.Instance;
        var now = DateTime.UtcNow;

        foreach (var definition in CategoryCharacteristicsCatalog.Definitions)
        {
            int? domainId = null;
            int? categoryId = null;
            int? subcategoryId = null;

            if (definition.TaxonomySubcategoryId is { } subId
                && catalog.SubcategoriesById.TryGetValue(subId, out var subcategory))
            {
                domainId = subcategory.DomainId;
                categoryId = subcategory.CategoryId;
                subcategoryId = subcategory.Id;
            }
            else if (definition.TaxonomyCategoryId is { } catId
                && catalog.CategoriesById.TryGetValue(catId, out var category))
            {
                domainId = category.DomainId;
                categoryId = category.Id;
            }
            else
            {
                continue;
            }

            var direction = definition.DirectionExpectation switch
            {
                CharacteristicsDirection.Outflow => "outflow",
                CharacteristicsDirection.Inflow => "inflow",
                _ => "either"
            };

            foreach (var signal in definition.MerchantSignals)
            {
                var pattern = signal.Trim().ToUpperInvariant();
                if (pattern.Length < 2 || !known.Add(pattern))
                {
                    continue;
                }

                dbContext.MerchantKnowledge.Add(new MerchantKnowledge
                {
                    Id = Guid.NewGuid(),
                    NormalizedPattern = pattern,
                    DisplayName = signal.Trim(),
                    TaxonomyDomainId = domainId,
                    TaxonomyCategoryId = categoryId,
                    TaxonomySubcategoryId = subcategoryId,
                    DirectionExpectation = direction,
                    Source = MerchantKnowledgeSources.Seed,
                    Confidence = 1.0,
                    CharacteristicsVersion = version,
                    IsActive = true,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Merchant knowledge seeded characteristicsVersion={Version}",
            version);
    }

    private static MerchantKnowledge? MatchAgainstKnowledge(
        IReadOnlyList<MerchantKnowledge> knowledge,
        string rawDescription,
        decimal amount)
    {
        var normalized = DeterministicMerchantCategorizer.NormalizeStatementText(rawDescription);
        if (normalized.Length < 2)
        {
            return null;
        }

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
                || !normalized.Contains(entry.NormalizedPattern, StringComparison.Ordinal))
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
