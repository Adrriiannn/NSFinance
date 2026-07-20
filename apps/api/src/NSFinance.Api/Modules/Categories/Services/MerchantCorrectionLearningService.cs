using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

// The user-correction side of the growing knowledge base (CAT-001): an
// explicit merchant-scope correction upserts a user-scoped MerchantKnowledge
// row (which outranks global rows for that user) and retargets the user's
// other automatic assignments for the same merchant. Other users' data and
// the global verified rows are never touched.
public sealed class MerchantCorrectionLearningService(
    AppDbContext dbContext,
    ILogger<MerchantCorrectionLearningService> logger)
{
    public sealed record CorrectionLearnResult(bool Learned, int RowsRetargeted, string? SkipReason);

    public async Task<CorrectionLearnResult> LearnFromCorrectionAsync(
        Guid userId,
        Transaction correctedTransaction,
        int domainId,
        int categoryId,
        int? subcategoryId,
        CancellationToken cancellationToken)
    {
        var pattern = DeterministicMerchantCategorizer.NormalizeStatementText(correctedTransaction.Description);
        if (pattern.Length < 2)
        {
            return new CorrectionLearnResult(false, 0, "descriptor_too_short");
        }

        if (pattern.Length > 200)
        {
            pattern = pattern[..200];
        }

        var direction = correctedTransaction.Amount switch
        {
            < 0 => "outflow",
            > 0 => "inflow",
            _ => "either"
        };

        var nowUtc = DateTime.UtcNow;
        var existing = await dbContext.MerchantKnowledge
            .SingleOrDefaultAsync(x => x.UserId == userId && x.NormalizedPattern == pattern, cancellationToken);

        if (existing is null)
        {
            existing = new MerchantKnowledge
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                NormalizedPattern = pattern,
                DisplayName = pattern,
                CreatedUtc = nowUtc
            };
            dbContext.MerchantKnowledge.Add(existing);
        }

        existing.TaxonomyDomainId = domainId;
        existing.TaxonomyCategoryId = categoryId;
        existing.TaxonomySubcategoryId = subcategoryId;
        existing.DirectionExpectation = direction;
        existing.Source = MerchantKnowledgeSources.UserCorrection;
        existing.Confidence = 1.0;
        existing.CharacteristicsVersion = CategoryCharacteristicsCatalog.Version;
        existing.IsActive = true;
        existing.UpdatedUtc = nowUtc;

        // Retarget the user's other rows for this merchant: automatic
        // assignments and uncategorized unclaimed rows follow the correction;
        // other manual corrections are protected and stay untouched.
        var siblings = await dbContext.Transactions
            .Where(x =>
                x.FinancialAccount != null
                && x.FinancialAccount.UserId == userId
                && x.Id != correctedTransaction.Id
                && x.DeterministicRelationshipType == null
                && x.AnalyticsTreatment == TransactionAnalyticsTreatments.Ordinary
                && (x.CategorizationRuleKey == null || x.CategorizationRuleKey == "merchant_knowledge"))
            .ToListAsync(cancellationToken);

        var retargeted = 0;
        foreach (var sibling in siblings)
        {
            var directionSatisfied = direction switch
            {
                "outflow" => sibling.Amount < 0,
                "inflow" => sibling.Amount > 0,
                _ => true
            };

            if (!directionSatisfied)
            {
                continue;
            }

            var normalized = DeterministicMerchantCategorizer.NormalizeStatementText(sibling.Description);
            if (!normalized.Contains(pattern, StringComparison.Ordinal))
            {
                continue;
            }

            sibling.TaxonomyDomainId = domainId;
            sibling.TaxonomyCategoryId = categoryId;
            sibling.TaxonomySubcategoryId = subcategoryId;
            sibling.CategorizationRuleKey = "merchant_knowledge";
            sibling.CategorizationSignal = pattern;
            sibling.CategorizationCharacteristicsVersion = CategoryCharacteristicsCatalog.Version;
            sibling.CategorizedUtc = nowUtc;
            retargeted += 1;
        }

        logger.LogInformation(
            "Merchant correction learned userId={UserId} pattern={Pattern} domainId={DomainId} categoryId={CategoryId} subcategoryId={SubcategoryId} rowsRetargeted={RowsRetargeted}",
            userId,
            pattern,
            domainId,
            categoryId,
            subcategoryId,
            retargeted);

        return new CorrectionLearnResult(true, retargeted, null);
    }
}
