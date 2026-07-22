using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

// Phase-two curation, first check: correction-conflict detection over the
// global knowledge base.
public sealed class MerchantKnowledgeCurationTests
{
    [Fact]
    public async Task Conflict_TwoUsersDisagree_OpensOneIssueWithAggregateEvidence()
    {
        await using var dbContext = CreateDbContext();
        var global = AddGlobalRow(dbContext, "COFFEE CORNER", 130, 13030, null);
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        await dbContext.SaveChangesAsync();

        var summary = await CreateService(dbContext).DetectCorrectionConflictsAsync(CancellationToken.None);

        Assert.Equal(1, summary.IssuesOpened);
        var issue = await dbContext.MerchantKnowledgeCurationIssues.SingleAsync();
        Assert.Equal(global.Id, issue.KnowledgeId);
        Assert.Equal(MerchantKnowledgeCurationIssueTypes.CorrectionConflict, issue.IssueType);
        Assert.Equal(MerchantKnowledgeCurationIssueStatuses.Open, issue.Status);
        Assert.Contains("\"disagreeingUsers\":2", issue.EvidenceJson);
        Assert.Contains("13010", issue.EvidenceJson);
        // Aggregates only: no user identifiers in evidence.
        var corrections = await dbContext.MerchantKnowledge.Where(k => k.UserId != null).ToListAsync();
        foreach (var correction in corrections)
        {
            Assert.DoesNotContain(correction.UserId!.Value.ToString(), issue.EvidenceJson);
        }
    }

    [Fact]
    public async Task Conflict_SingleUserDisagrees_StaysBelowThreshold()
    {
        await using var dbContext = CreateDbContext();
        AddGlobalRow(dbContext, "COFFEE CORNER", 130, 13030, null);
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        await dbContext.SaveChangesAsync();

        var summary = await CreateService(dbContext).DetectCorrectionConflictsAsync(CancellationToken.None);

        Assert.Equal(0, summary.IssuesOpened);
        Assert.Empty(await dbContext.MerchantKnowledgeCurationIssues.ToListAsync());
    }

    [Fact]
    public async Task Conflict_AgreeingCorrections_NeverCount()
    {
        await using var dbContext = CreateDbContext();
        AddGlobalRow(dbContext, "COFFEE CORNER", 130, 13030, null);
        // Same taxonomy as global: personalization without disagreement.
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13030, null);
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13030, null);
        await dbContext.SaveChangesAsync();

        var summary = await CreateService(dbContext).DetectCorrectionConflictsAsync(CancellationToken.None);

        Assert.Equal(0, summary.IssuesOpened);
    }

    [Fact]
    public async Task Conflict_RepeatRun_ConfirmsInsteadOfDuplicating()
    {
        await using var dbContext = CreateDbContext();
        AddGlobalRow(dbContext, "COFFEE CORNER", 130, 13030, null);
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await service.DetectCorrectionConflictsAsync(CancellationToken.None);
        var second = await service.DetectCorrectionConflictsAsync(CancellationToken.None);

        Assert.Equal(0, second.IssuesOpened);
        Assert.Equal(1, second.IssuesConfirmed);
        Assert.Single(await dbContext.MerchantKnowledgeCurationIssues.ToListAsync());
    }

    [Fact]
    public async Task Conflict_DisagreementWithdrawn_AutoResolves()
    {
        await using var dbContext = CreateDbContext();
        AddGlobalRow(dbContext, "COFFEE CORNER", 130, 13030, null);
        var c1 = AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        var c2 = AddCorrection(dbContext, "COFFEE CORNER", 130, 13010, null);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await service.DetectCorrectionConflictsAsync(CancellationToken.None);

        c1.IsActive = false;
        c2.IsActive = false;
        await dbContext.SaveChangesAsync();

        var summary = await service.DetectCorrectionConflictsAsync(CancellationToken.None);

        Assert.Equal(1, summary.IssuesResolved);
        var issue = await dbContext.MerchantKnowledgeCurationIssues.SingleAsync();
        Assert.Equal(MerchantKnowledgeCurationIssueStatuses.Resolved, issue.Status);
    }

    private static MerchantKnowledgeCurationService CreateService(AppDbContext dbContext)
    {
        return new MerchantKnowledgeCurationService(
            dbContext,
            Options.Create(new MerchantCurationOptions
            {
                ConflictDetectionEnabled = true,
                ConflictUserThreshold = 2
            }),
            NullLogger<MerchantKnowledgeCurationService>.Instance);
    }

    private static MerchantKnowledge AddGlobalRow(
        AppDbContext dbContext, string pattern, int domainId, int categoryId, int? subcategoryId)
    {
        var row = new MerchantKnowledge
        {
            Id = Guid.NewGuid(),
            UserId = null,
            NormalizedPattern = pattern,
            DisplayName = pattern,
            TaxonomyDomainId = domainId,
            TaxonomyCategoryId = categoryId,
            TaxonomySubcategoryId = subcategoryId,
            Source = MerchantKnowledgeSources.AiInvestigation,
            Confidence = 0.9,
            CharacteristicsVersion = 1,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        dbContext.MerchantKnowledge.Add(row);
        return row;
    }

    private static MerchantKnowledge AddCorrection(
        AppDbContext dbContext, string pattern, int domainId, int categoryId, int? subcategoryId)
    {
        var row = new MerchantKnowledge
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            NormalizedPattern = pattern,
            DisplayName = pattern,
            TaxonomyDomainId = domainId,
            TaxonomyCategoryId = categoryId,
            TaxonomySubcategoryId = subcategoryId,
            Source = MerchantKnowledgeSources.UserCorrection,
            Confidence = 1.0,
            CharacteristicsVersion = 1,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        dbContext.MerchantKnowledge.Add(row);
        return row;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"curation-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }
}
