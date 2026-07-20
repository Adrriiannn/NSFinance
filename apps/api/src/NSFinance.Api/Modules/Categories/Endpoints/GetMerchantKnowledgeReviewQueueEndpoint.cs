using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Endpoints;

// Operator window into the growth loop (CAT-001): what the AI worker parked
// for review, what it promoted, and what is cooling down - with the full
// investigation summary each decision was based on. Internal-only: candidate
// rows carry statement descriptors across users.

public sealed record MerchantKnowledgeReviewQueueResponse(
    IReadOnlyDictionary<string, int> CountsByStatus,
    IReadOnlyList<MerchantKnowledgeReviewItemDto> Items);

public sealed record MerchantKnowledgeReviewItemDto(
    Guid Id,
    string NormalizedDescriptor,
    string RawDescriptorSample,
    string Status,
    int ObservedOccurrences,
    decimal ObservedSpendAbs,
    string ObservedDirection,
    int AttemptCount,
    DateTime? LastAttemptUtc,
    DateTime? NextEligibleUtc,
    string? LastOutcomeCode,
    int? ProposedTaxonomyDomainId,
    int? ProposedTaxonomyCategoryId,
    int? ProposedTaxonomySubcategoryId,
    string? ProposedCategoryName,
    double? ProposedConfidence,
    Guid? PromotedKnowledgeId,
    string? InvestigationSummaryJson,
    DateTime UpdatedUtc);

public static class GetMerchantKnowledgeReviewQueueEndpoint
{
    public static async Task<IResult> HandleAsync(
        AppDbContext dbContext,
        string? status,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? MerchantKnowledgeCandidateStatuses.NeedsReview
            : status.Trim().ToLowerInvariant();
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var counts = await dbContext.MerchantKnowledgeCandidates
            .AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var query = dbContext.MerchantKnowledgeCandidates.AsNoTracking();
        if (normalizedStatus != "all")
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var rows = await query
            .OrderByDescending(x => x.ObservedOccurrences)
            .ThenByDescending(x => x.ObservedSpendAbs)
            .Take(take)
            .ToListAsync(cancellationToken);

        var catalog = NSFinanceTaxonomyCatalog.Instance;
        var items = rows
            .Select(row => new MerchantKnowledgeReviewItemDto(
                row.Id,
                row.NormalizedDescriptor,
                row.RawDescriptorSample,
                row.Status,
                row.ObservedOccurrences,
                row.ObservedSpendAbs,
                row.ObservedDirection,
                row.AttemptCount,
                row.LastAttemptUtc,
                row.NextEligibleUtc,
                row.LastOutcomeCode,
                row.ProposedTaxonomyDomainId,
                row.ProposedTaxonomyCategoryId,
                row.ProposedTaxonomySubcategoryId,
                ResolveProposedName(catalog, row),
                row.ProposedConfidence,
                row.PromotedKnowledgeId,
                row.InvestigationSummaryJson,
                row.UpdatedUtc))
            .ToList();

        return Results.Ok(new MerchantKnowledgeReviewQueueResponse(counts, items));
    }

    private static string? ResolveProposedName(NSFinanceTaxonomyCatalog catalog, MerchantKnowledgeCandidate row)
    {
        if (row.ProposedTaxonomySubcategoryId is { } subId
            && catalog.SubcategoriesById.TryGetValue(subId, out var subcategory))
        {
            return subcategory.Name;
        }

        if (row.ProposedTaxonomyCategoryId is { } categoryId
            && catalog.CategoriesById.TryGetValue(categoryId, out var category))
        {
            return category.Name;
        }

        return null;
    }
}
