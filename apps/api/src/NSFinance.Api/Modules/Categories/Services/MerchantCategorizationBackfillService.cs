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
    int RowsUnmatched);

// Deterministic merchant categorization backfill (CAT-001). Strictly additive:
// only rows with no taxonomy at all are considered, rows claimed by the
// relationship engine are never touched, and matches write the full
// domain/category/subcategory triple resolved through the validated taxonomy
// catalog. Every assignment logs its rule evidence without statement text.
public sealed class MerchantCategorizationBackfillService(
    AppDbContext dbContext,
    IOptions<MerchantCategorizationOptions> options,
    ILogger<MerchantCategorizationBackfillService> logger)
{
    public bool IsEnabled => options.Value.BackfillOnGlobalSyncEnabled;

    public async Task<MerchantBackfillSummary> BackfillAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Clamp(options.Value.MaxRowsPerRun, 1, 2000);

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

        var catalog = NSFinanceTaxonomyCatalog.Instance;
        var categorized = 0;

        foreach (var transaction in candidates)
        {
            var match = DeterministicMerchantCategorizer.Match(
                transaction.Description,
                transaction.Amount);

            if (match is null)
            {
                continue;
            }

            if (match.TaxonomySubcategoryId is { } subcategoryId
                && catalog.SubcategoriesById.TryGetValue(subcategoryId, out var subcategory))
            {
                transaction.TaxonomyDomainId = subcategory.DomainId;
                transaction.TaxonomyCategoryId = subcategory.CategoryId;
                transaction.TaxonomySubcategoryId = subcategory.Id;
            }
            else if (match.TaxonomyCategoryId is { } categoryId
                && catalog.CategoriesById.TryGetValue(categoryId, out var category))
            {
                transaction.TaxonomyDomainId = category.DomainId;
                transaction.TaxonomyCategoryId = category.Id;
                transaction.TaxonomySubcategoryId = null;
            }
            else
            {
                continue;
            }

            transaction.CategorizationRuleKey = "merchant_signal";
            transaction.CategorizationSignal = match.MatchedSignal;
            transaction.CategorizationCharacteristicsVersion = match.CharacteristicsVersion;
            transaction.CategorizedUtc = DateTime.UtcNow;

            categorized += 1;
            logger.LogInformation(
                "Merchant categorization assigned transactionId={TransactionId} ruleKey=merchant_signal signal={Signal} characteristicsVersion={Version} domainId={DomainId} categoryId={CategoryId} subcategoryId={SubcategoryId}",
                transaction.Id,
                match.MatchedSignal,
                match.CharacteristicsVersion,
                transaction.TaxonomyDomainId,
                transaction.TaxonomyCategoryId,
                transaction.TaxonomySubcategoryId);
        }

        if (categorized > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var summary = new MerchantBackfillSummary(
            RowsExamined: candidates.Count,
            RowsCategorized: categorized,
            RowsUnmatched: candidates.Count - categorized);

        logger.LogInformation(
            "Merchant categorization backfill userId={UserId} rowsExamined={RowsExamined} rowsCategorized={RowsCategorized} rowsUnmatched={RowsUnmatched} characteristicsVersion={Version}",
            userId,
            summary.RowsExamined,
            summary.RowsCategorized,
            summary.RowsUnmatched,
            CategoryCharacteristicsCatalog.Version);

        return summary;
    }
}
