using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Categories.Endpoints;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class MerchantKnowledgeReviewQueueTests
{
    [Fact]
    public async Task ReviewQueue_DefaultsToNeedsReview_WithCountsAndProposedNames()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;

        dbContext.MerchantKnowledgeCandidates.AddRange(
            new MerchantKnowledgeCandidate
            {
                Id = Guid.NewGuid(),
                NormalizedDescriptor = "MYSTERY CAFE",
                RawDescriptorSample = "VDP-MYSTERY CAFE",
                Status = MerchantKnowledgeCandidateStatuses.NeedsReview,
                ObservedOccurrences = 4,
                ObservedSpendAbs = 31m,
                ObservedDirection = "outflow",
                LastOutcomeCode = "mixed_use",
                ProposedTaxonomyDomainId = 130,
                ProposedTaxonomyCategoryId = 13020,
                ProposedConfidence = 0.6,
                InvestigationSummaryJson = "{\"acceptance\":{}}",
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new MerchantKnowledgeCandidate
            {
                Id = Guid.NewGuid(),
                NormalizedDescriptor = "PROMOTED VENDOR",
                RawDescriptorSample = "PROMOTED VENDOR",
                Status = MerchantKnowledgeCandidateStatuses.Promoted,
                ObservedOccurrences = 9,
                ObservedSpendAbs = 90m,
                ObservedDirection = "outflow",
                CreatedUtc = now,
                UpdatedUtc = now
            });
        await dbContext.SaveChangesAsync();

        var result = await GetMerchantKnowledgeReviewQueueEndpoint.HandleAsync(
            dbContext,
            status: null,
            limit: null,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<MerchantKnowledgeReviewQueueResponse>>(result);
        var response = ok.Value!;

        Assert.Equal(1, response.CountsByStatus[MerchantKnowledgeCandidateStatuses.NeedsReview]);
        Assert.Equal(1, response.CountsByStatus[MerchantKnowledgeCandidateStatuses.Promoted]);

        var item = Assert.Single(response.Items);
        Assert.Equal("MYSTERY CAFE", item.NormalizedDescriptor);
        Assert.Equal("Dining Out", item.ProposedCategoryName);
        Assert.Equal("mixed_use", item.LastOutcomeCode);
        Assert.NotNull(item.InvestigationSummaryJson);
    }

    [Fact]
    public async Task ReviewQueue_AllStatus_ReturnsEverything_OrderedByOccurrences()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= 3; i++)
        {
            dbContext.MerchantKnowledgeCandidates.Add(new MerchantKnowledgeCandidate
            {
                Id = Guid.NewGuid(),
                NormalizedDescriptor = $"VENDOR {i}",
                RawDescriptorSample = $"VENDOR {i}",
                Status = i == 2
                    ? MerchantKnowledgeCandidateStatuses.Pending
                    : MerchantKnowledgeCandidateStatuses.NeedsReview,
                ObservedOccurrences = i,
                ObservedSpendAbs = i * 10m,
                ObservedDirection = "outflow",
                CreatedUtc = now,
                UpdatedUtc = now
            });
        }

        await dbContext.SaveChangesAsync();

        var result = await GetMerchantKnowledgeReviewQueueEndpoint.HandleAsync(
            dbContext,
            status: "all",
            limit: 2,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<MerchantKnowledgeReviewQueueResponse>>(result);
        var response = ok.Value!;

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("VENDOR 3", response.Items[0].NormalizedDescriptor);
        Assert.Equal("VENDOR 2", response.Items[1].NormalizedDescriptor);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"knowledge-review-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }
}
