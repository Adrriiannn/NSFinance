using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Categories.Services;

public sealed class MerchantCurationOptions
{
    public const string SectionName = "Categorization:Curation";

    // Kill-switch for the correction-conflict check. Ships dark, like every
    // lane before it. Further curation checks arrive one at a time, each
    // with its own flag.
    public bool ConflictDetectionEnabled { get; set; }

    // How many distinct users must contradict a global row before it is
    // considered suspect.
    public int ConflictUserThreshold { get; set; } = 2;
}

public sealed record MerchantCurationRunSummary(
    int GlobalRowsChecked,
    int IssuesOpened,
    int IssuesConfirmed,
    int IssuesResolved);

// Phase-two curation, first check: correction-conflict detection. When
// several different users each correct the same global pattern away from its
// assigned category, the global row is suspect - the users know something the
// dictionary does not. The check opens exactly one issue per suspect row,
// refreshes its aggregate evidence on every run, and auto-resolves it when
// the disagreement falls back below the threshold. Evidence stores counts
// and proposed categories only, never user identifiers.
public sealed class MerchantKnowledgeCurationService(
    AppDbContext dbContext,
    IOptions<MerchantCurationOptions> options,
    ILogger<MerchantKnowledgeCurationService> logger)
{
    public bool IsConflictDetectionEnabled => options.Value.ConflictDetectionEnabled;

    public async Task<MerchantCurationRunSummary> DetectCorrectionConflictsAsync(
        CancellationToken cancellationToken)
    {
        var threshold = Math.Max(1, options.Value.ConflictUserThreshold);
        var nowUtc = DateTime.UtcNow;

        var globalRows = await dbContext.MerchantKnowledge
            .AsNoTracking()
            .Where(k => k.UserId == null && k.IsActive)
            .ToListAsync(cancellationToken);

        var corrections = await dbContext.MerchantKnowledge
            .AsNoTracking()
            .Where(k => k.UserId != null && k.IsActive && k.Source == MerchantKnowledgeSources.UserCorrection)
            .ToListAsync(cancellationToken);

        var correctionsByPattern = corrections
            .GroupBy(c => c.NormalizedPattern, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var openIssues = await dbContext.MerchantKnowledgeCurationIssues
            .Where(i => i.IssueType == MerchantKnowledgeCurationIssueTypes.CorrectionConflict
                && i.Status == MerchantKnowledgeCurationIssueStatuses.Open)
            .ToListAsync(cancellationToken);
        var openIssuesByKnowledgeId = openIssues.ToDictionary(i => i.KnowledgeId);

        var opened = 0;
        var confirmed = 0;
        var resolved = 0;
        var suspectKnowledgeIds = new HashSet<Guid>();

        foreach (var global in globalRows)
        {
            if (!correctionsByPattern.TryGetValue(global.NormalizedPattern, out var sameKey))
            {
                continue;
            }

            var disagreeing = sameKey
                .Where(c => c.TaxonomyDomainId != global.TaxonomyDomainId
                    || c.TaxonomyCategoryId != global.TaxonomyCategoryId
                    || c.TaxonomySubcategoryId != global.TaxonomySubcategoryId)
                .ToList();

            var disagreeingUsers = disagreeing
                .Select(c => c.UserId!.Value)
                .Distinct()
                .Count();

            if (disagreeingUsers < threshold)
            {
                continue;
            }

            suspectKnowledgeIds.Add(global.Id);

            var evidenceJson = JsonSerializer.Serialize(new
            {
                disagreeingUsers,
                assigned = new
                {
                    domainId = global.TaxonomyDomainId,
                    categoryId = global.TaxonomyCategoryId,
                    subcategoryId = global.TaxonomySubcategoryId
                },
                proposed = disagreeing
                    .GroupBy(c => (c.TaxonomyDomainId, c.TaxonomyCategoryId, c.TaxonomySubcategoryId))
                    .Select(g => new
                    {
                        domainId = g.Key.TaxonomyDomainId,
                        categoryId = g.Key.TaxonomyCategoryId,
                        subcategoryId = g.Key.TaxonomySubcategoryId,
                        users = g.Select(c => c.UserId!.Value).Distinct().Count()
                    })
                    .OrderByDescending(x => x.users)
            });

            if (openIssuesByKnowledgeId.TryGetValue(global.Id, out var existing))
            {
                existing.EvidenceJson = evidenceJson;
                existing.UpdatedUtc = nowUtc;
                confirmed += 1;
                continue;
            }

            dbContext.MerchantKnowledgeCurationIssues.Add(new MerchantKnowledgeCurationIssue
            {
                Id = Guid.NewGuid(),
                KnowledgeId = global.Id,
                IssueType = MerchantKnowledgeCurationIssueTypes.CorrectionConflict,
                Status = MerchantKnowledgeCurationIssueStatuses.Open,
                EvidenceJson = evidenceJson,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });
            opened += 1;
            logger.LogInformation(
                "Curation conflict opened knowledgeId={KnowledgeId} disagreeingUsers={DisagreeingUsers}",
                global.Id,
                disagreeingUsers);
        }

        // Disagreement fell below the threshold (corrections withdrawn, row
        // retargeted, or row deactivated): close the issue.
        foreach (var issue in openIssues.Where(i => !suspectKnowledgeIds.Contains(i.KnowledgeId)))
        {
            issue.Status = MerchantKnowledgeCurationIssueStatuses.Resolved;
            issue.UpdatedUtc = nowUtc;
            resolved += 1;
            logger.LogInformation("Curation conflict resolved knowledgeId={KnowledgeId}", issue.KnowledgeId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var summary = new MerchantCurationRunSummary(
            GlobalRowsChecked: globalRows.Count,
            IssuesOpened: opened,
            IssuesConfirmed: confirmed,
            IssuesResolved: resolved);

        logger.LogInformation(
            "Curation conflict run globalRowsChecked={GlobalRowsChecked} issuesOpened={IssuesOpened} issuesConfirmed={IssuesConfirmed} issuesResolved={IssuesResolved}",
            summary.GlobalRowsChecked,
            summary.IssuesOpened,
            summary.IssuesConfirmed,
            summary.IssuesResolved);

        return summary;
    }
}
