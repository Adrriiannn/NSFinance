using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public sealed class ExpensePlanCommunityService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    IAuditService auditService)
{
    public async Task<IReadOnlyList<ExpensePlanPublicationCardDto>> GetCommunityPlansAsync(
        BrowseExpensePlanPublicationsRequest request,
        CancellationToken cancellationToken)
    {
        await RescanPublishedPlansAsync(cancellationToken);

        var publications = await dbContext.ExpensePlanPublications
            .AsNoTracking()
            .Where(x => ExpensePlanPublicationStatuses.PubliclyVisible.Contains(x.PublicationStatus))
            .ToListAsync(cancellationToken);

        var likedIds = await dbContext.ExpensePlanPublicationLikes
            .AsNoTracking()
            .Where(x => x.UserId == currentUserProvider.UserId)
            .Select(x => x.PublicationId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var filtered = publications
            .Where(publication => MatchesBrowse(publication, request))
            .Select(publication =>
            {
                publication.TrendingScore = BuildTrendingScore(publication, now);
                return publication;
            })
            .ToList();

        return SortPublications(filtered, request.Sort, now)
            .Take(Math.Clamp(request.Take ?? 40, 1, 80))
            .Select(publication => ToCardDto(publication, likedIds.Contains(publication.Id), publication.CreatorUserId == currentUserProvider.UserId))
            .ToList();
    }

    public async Task<ExpensePlanPublicationDetailDto?> GetPublicationByIdAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        await RescanPublicationIfNeededAsync(publicationId, cancellationToken);

        var publication = await dbContext.ExpensePlanPublications
            .AsNoTracking()
            .Include(x => x.ModerationEvents)
            .Include(x => x.Reports)
            .SingleOrDefaultAsync(x => x.Id == publicationId, cancellationToken);

        if (publication is null)
        {
            return null;
        }

        var canManage = publication.CreatorUserId == currentUserProvider.UserId;
        if (!canManage && !ExpensePlanPublicationStatuses.PubliclyVisible.Contains(publication.PublicationStatus))
        {
            return null;
        }

        var likedByCurrentUser = await dbContext.ExpensePlanPublicationLikes
            .AsNoTracking()
            .AnyAsync(x => x.PublicationId == publicationId && x.UserId == currentUserProvider.UserId, cancellationToken);

        return ToDetailDto(publication, likedByCurrentUser, canManage);
    }

    public async Task<ExpensePlanPublicationDetailDto> PublishPlanAsync(
        PublishExpensePlanRequest request,
        CancellationToken cancellationToken)
    {
        var sourcePlanId = request.SourcePlanId ?? Guid.Empty;
        var sourcePlan = await dbContext.ExpensePlans
            .Include(x => x.LineItems.OrderBy(item => item.SortOrder))
            .SingleOrDefaultAsync(
                x => x.Id == sourcePlanId && x.UserId == currentUserProvider.UserId,
                cancellationToken)
            ?? throw new InvalidOperationException("Choose a private plan you own before publishing.");

        if (sourcePlan.Status is ExpensePlanStatuses.Archived or ExpensePlanStatuses.Cancelled)
        {
            throw new InvalidOperationException("Archived or cancelled plans cannot be published.");
        }

        var existing = await dbContext.ExpensePlanPublications
            .SingleOrDefaultAsync(x => x.SourcePlanId == sourcePlan.Id && x.CreatorUserId == currentUserProvider.UserId && x.PublicationStatus != ExpensePlanPublicationStatuses.Removed, cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException("This plan already has a publication. Update or unpublish the existing public version instead.");
        }

        var utcNow = DateTime.UtcNow;
        var tags = NormalizeTags(request.Tags);
        var moderation = ExpensePlanPublicationModerationService.Scan(request.PublicTitle, request.PublicDescription, tags);
        var publication = new ExpensePlanPublication
        {
            Id = Guid.NewGuid(),
            CreatorUserId = currentUserProvider.UserId,
            SourcePlanId = sourcePlan.Id,
            SourcePlanVersion = sourcePlan.PlanVersion,
            CreatorDisplayNameSnapshot = sourcePlan.CreatorDisplayNameSnapshot,
            CreatorTagSnapshot = sourcePlan.CreatorTagSnapshot,
            PublicTitle = request.PublicTitle.Trim(),
            PublicDescription = NormalizeOptionalText(request.PublicDescription),
            TagsJson = JsonSerializer.Serialize(tags),
            PublicationStatus = ResolvePublicationStatusForModeration(moderation, false),
            ModerationStatus = moderation.ModerationStatus,
            ModerationSummary = moderation.Summary,
            PlanSnapshotJson = JsonSerializer.Serialize(BuildSnapshot(sourcePlan)),
            PlanType = sourcePlan.PlanType,
            CurrencyCode = sourcePlan.CurrencyCode,
            ExpectedSpendTotal = sourcePlan.ExpectedSpendTotal,
            IsTemplate = sourcePlan.IsTemplate,
            IsRecurring = sourcePlan.IsRecurring,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            PublishedAtUtc = moderation.ModerationStatus == ExpensePlanModerationStatuses.Approved ? utcNow : null,
            LastModeratedAtUtc = utcNow,
            LastRescannedAtUtc = utcNow
        };
        publication.TrendingScore = BuildTrendingScore(publication, utcNow);

        dbContext.ExpensePlanPublications.Add(publication);
        dbContext.ExpensePlanPublicationModerationEvents.Add(BuildModerationEvent(publication.Id, ExpensePlanModerationTriggerTypes.PrePublish, moderation, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            "expense_plan_publication",
            "published_attempt",
            nameof(ExpensePlanPublication),
            publication.Id.ToString(),
            currentUserProvider.UserId,
            "user",
            new { publication.PublicationStatus, publication.ModerationStatus, publication.SourcePlanId },
            cancellationToken);

        return ToDetailDto(publication, false, true);
    }

    public async Task<ExpensePlanPublicationDetailDto?> UpdatePublicationAsync(
        Guid publicationId,
        UpdateExpensePlanPublicationRequest request,
        CancellationToken cancellationToken)
    {
        var publication = await dbContext.ExpensePlanPublications
            .Include(x => x.ModerationEvents)
            .SingleOrDefaultAsync(x => x.Id == publicationId && x.CreatorUserId == currentUserProvider.UserId, cancellationToken);

        if (publication is null)
        {
            return null;
        }

        if (publication.PublicationStatus == ExpensePlanPublicationStatuses.Removed)
        {
            throw new InvalidOperationException("Removed publications cannot be edited.");
        }

        var utcNow = DateTime.UtcNow;
        var tags = NormalizeTags(request.Tags);
        var moderation = ExpensePlanPublicationModerationService.Scan(request.PublicTitle, request.PublicDescription, tags);

        publication.PublicTitle = request.PublicTitle.Trim();
        publication.PublicDescription = NormalizeOptionalText(request.PublicDescription);
        publication.TagsJson = JsonSerializer.Serialize(tags);
        publication.ModerationStatus = moderation.ModerationStatus;
        publication.ModerationSummary = moderation.Summary;
        publication.PublicationStatus = ResolvePublicationStatusForModeration(
            moderation,
            string.Equals(publication.PublicationStatus, ExpensePlanPublicationStatuses.Published, StringComparison.OrdinalIgnoreCase));
        publication.UpdatedAtUtc = utcNow;
        publication.LastModeratedAtUtc = utcNow;
        publication.LastRescannedAtUtc = utcNow;
        if (publication.PublicationStatus == ExpensePlanPublicationStatuses.Published)
        {
            publication.PublishedAtUtc ??= utcNow;
        }
        else
        {
            publication.UnpublishedAtUtc ??= utcNow;
        }
        publication.TrendingScore = BuildTrendingScore(publication, utcNow);

        dbContext.ExpensePlanPublicationModerationEvents.Add(BuildModerationEvent(publication.Id, ExpensePlanModerationTriggerTypes.MetadataUpdate, moderation, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetPublicationByIdAsync(publicationId, cancellationToken);
    }

    public async Task<ExpensePlanPublicationDetailDto?> ToggleLikeAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var publication = await dbContext.ExpensePlanPublications
            .SingleOrDefaultAsync(x => x.Id == publicationId, cancellationToken);

        if (publication is null || !CanInteractWithPublication(publication))
        {
            return null;
        }

        var like = await dbContext.ExpensePlanPublicationLikes
            .SingleOrDefaultAsync(x => x.PublicationId == publicationId && x.UserId == currentUserProvider.UserId, cancellationToken);

        if (like is null)
        {
            dbContext.ExpensePlanPublicationLikes.Add(new ExpensePlanPublicationLike
            {
                Id = Guid.NewGuid(),
                PublicationId = publicationId,
                UserId = currentUserProvider.UserId,
                CreatedAtUtc = DateTime.UtcNow
            });
            publication.LikeCount += 1;
        }
        else
        {
            dbContext.ExpensePlanPublicationLikes.Remove(like);
            publication.LikeCount = Math.Max(publication.LikeCount - 1, 0);
        }

        publication.UpdatedAtUtc = DateTime.UtcNow;
        publication.TrendingScore = BuildTrendingScore(publication, publication.UpdatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDetailDto(publication, false, false);
    }

    public async Task<ExpensePlanDto?> UsePublicationAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var publication = await dbContext.ExpensePlanPublications
            .SingleOrDefaultAsync(x => x.Id == publicationId, cancellationToken);

        if (publication is null || !CanInteractWithPublication(publication))
        {
            return null;
        }

        var creator = await GetCreatorSnapshotsAsync(cancellationToken);
        var snapshot = DeserializeSnapshot(publication.PlanSnapshotJson);
        var utcNow = DateTime.UtcNow;
        var importedPlan = new ExpensePlan
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            CreatorDisplayNameSnapshot = creator.DisplayName,
            CreatorTagSnapshot = creator.Tag,
            Title = $"{publication.PublicTitle} copy",
            Description = publication.PublicDescription,
            Notes = null,
            Status = ExpensePlanStatuses.Drafted,
            PlanType = snapshot.PlanType,
            PlanOriginType = ExpensePlanOriginTypes.Shared,
            PlanVersion = 1,
            StartDateUtc = snapshot.StartDateUtc,
            EndDateUtc = snapshot.EndDateUtc,
            CurrencyCode = snapshot.CurrencyCode,
            ExpectedIncomeTotal = snapshot.ExpectedIncomeTotal,
            ExpectedSpendTotal = snapshot.ExpectedSpendTotal,
            ExpectedRemainingTotal = snapshot.ExpectedRemainingTotal,
            TagsJson = publication.TagsJson,
            StatusReason = $"Imported from community publication {publication.Id}",
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            LastCalculatedAtUtc = utcNow,
            SourcePlanId = null,
            ImportedFromPublicPlanId = publication.Id,
            IsTemplate = false,
            IsRecurring = snapshot.IsRecurring,
            RecurrenceRuleJson = snapshot.RecurrenceRuleJson,
            IsShared = false,
            SharingMode = null,
            SharedIdentity = null
        };

        importedPlan.LineItems = snapshot.LineItems
            .OrderBy(item => item.SortOrder)
            .Select(item => new ExpensePlanLineItem
            {
                Id = Guid.NewGuid(),
                PlanId = importedPlan.Id,
                TaxonomyDomainId = item.TaxonomyDomainId,
                TaxonomyCategoryId = item.TaxonomyCategoryId,
                TaxonomySubcategoryId = item.TaxonomySubcategoryId,
                DisplayNameSnapshot = item.DisplayNameSnapshot,
                HierarchyPathSnapshot = item.HierarchyPathSnapshot,
                ExpectedAmount = item.ExpectedAmount,
                Notes = item.Notes,
                SortOrder = item.SortOrder,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            })
            .ToList();

        dbContext.ExpensePlans.Add(importedPlan);
        dbContext.ExpensePlanPublicationDownloads.Add(new ExpensePlanPublicationDownload
        {
            Id = Guid.NewGuid(),
            PublicationId = publication.Id,
            UserId = currentUserProvider.UserId,
            CreatedPlanId = importedPlan.Id,
            CreatedAtUtc = utcNow
        });
        publication.DownloadCount += 1;
        publication.UpdatedAtUtc = utcNow;
        publication.TrendingScore = BuildTrendingScore(publication, utcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            "expense_plan_publication",
            "downloaded",
            nameof(ExpensePlanPublication),
            publication.Id.ToString(),
            currentUserProvider.UserId,
            "user",
            new { ImportedPlanId = importedPlan.Id },
            cancellationToken);

        var entries = await dbContext.ExpenseTrackerEntries
            .AsNoTracking()
            .Include(x => x.LinkedOriginalEntry)
            .Where(x => x.UserId == currentUserProvider.UserId)
            .ToListAsync(cancellationToken);

        return ToPlanDto(importedPlan, entries, utcNow);
    }

    public async Task<ExpensePlanPublicationDetailDto?> ReportPublicationAsync(
        Guid publicationId,
        ReportExpensePlanPublicationRequest request,
        CancellationToken cancellationToken)
    {
        var publication = await dbContext.ExpensePlanPublications
            .Include(x => x.Reports)
            .SingleOrDefaultAsync(x => x.Id == publicationId, cancellationToken);

        if (publication is null || !ExpensePlanPublicationStatuses.PubliclyVisible.Contains(publication.PublicationStatus))
        {
            return null;
        }

        if (publication.CreatorUserId == currentUserProvider.UserId)
        {
            throw new InvalidOperationException("You cannot report your own publication.");
        }

        var existing = await dbContext.ExpensePlanPublicationReports
            .SingleOrDefaultAsync(
                x => x.PublicationId == publicationId
                    && x.ReporterUserId == currentUserProvider.UserId
                    && x.Reason == request.Reason
                    && x.Status == ExpensePlanReportStatuses.Open,
                cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException("You have already submitted this report.");
        }

        var utcNow = DateTime.UtcNow;
        dbContext.ExpensePlanPublicationReports.Add(new ExpensePlanPublicationReport
        {
            Id = Guid.NewGuid(),
            PublicationId = publicationId,
            ReporterUserId = currentUserProvider.UserId,
            Reason = request.Reason.Trim(),
            Notes = NormalizeOptionalText(request.Notes),
            Status = ExpensePlanReportStatuses.Open,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        });

        publication.ReportCount += 1;
        publication.LastReportedAtUtc = utcNow;
        publication.UpdatedAtUtc = utcNow;
        if (publication.ReportCount >= 2)
        {
            publication.PublicationStatus = ExpensePlanPublicationStatuses.Flagged;
            publication.ModerationStatus = ExpensePlanModerationStatuses.FlaggedAfterPublish;
            publication.ModerationSummary = "Flagged after repeated reports.";
            dbContext.ExpensePlanPublicationModerationEvents.Add(new ExpensePlanPublicationModerationEvent
            {
                Id = Guid.NewGuid(),
                PublicationId = publication.Id,
                TriggerType = ExpensePlanModerationTriggerTypes.ReportThreshold,
                ResultStatus = ExpensePlanModerationStatuses.FlaggedAfterPublish,
                Summary = publication.ModerationSummary,
                MatchedRulesJson = JsonSerializer.Serialize(new[] { "report_threshold" }),
                CreatedAtUtc = utcNow
            });
        }
        publication.TrendingScore = BuildTrendingScore(publication, utcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            "expense_plan_publication",
            "reported",
            nameof(ExpensePlanPublication),
            publication.Id.ToString(),
            currentUserProvider.UserId,
            "user",
            new { request.Reason },
            cancellationToken);

        return ToDetailDto(publication, false, false);
    }

    public async Task<ExpensePlanPublicationDashboardDto> GetMyDashboardAsync(CancellationToken cancellationToken)
    {
        await RescanCreatorPublicationsAsync(cancellationToken);

        var publications = await dbContext.ExpensePlanPublications
            .AsNoTracking()
            .Where(x => x.CreatorUserId == currentUserProvider.UserId)
            .OrderByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new ExpensePlanPublicationDashboardDto(
            publications.Count(x => x.PublicationStatus == ExpensePlanPublicationStatuses.Published),
            publications.Count(x => x.PublicationStatus == ExpensePlanPublicationStatuses.PendingReview),
            publications.Count(x => x.PublicationStatus == ExpensePlanPublicationStatuses.Flagged),
            publications.Sum(x => x.LikeCount),
            publications.Sum(x => x.DownloadCount),
            publications.Sum(x => x.ReportCount),
            publications.Select(x => new ExpensePlanPublicationDashboardItemDto(
                x.Id,
                x.SourcePlanId,
                x.PublicTitle,
                x.PublicationStatus,
                x.ModerationStatus,
                x.LikeCount,
                x.DownloadCount,
                x.ReportCount,
                x.CreatedAtUtc,
                x.PublishedAtUtc,
                x.IsTemplate,
                x.IsRecurring)).ToList());
    }

    public async Task<ExpensePlanPublicationDetailDto?> UnpublishAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var publication = await dbContext.ExpensePlanPublications
            .SingleOrDefaultAsync(x => x.Id == publicationId && x.CreatorUserId == currentUserProvider.UserId, cancellationToken);

        if (publication is null)
        {
            return null;
        }

        publication.PublicationStatus = ExpensePlanPublicationStatuses.Unpublished;
        publication.UnpublishedAtUtc = DateTime.UtcNow;
        publication.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetPublicationByIdAsync(publicationId, cancellationToken);
    }

    public async Task<ExpensePlanPublicationDetailDto?> RescanAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var publication = await dbContext.ExpensePlanPublications
            .SingleOrDefaultAsync(x => x.Id == publicationId && x.CreatorUserId == currentUserProvider.UserId, cancellationToken);

        if (publication is null)
        {
            return null;
        }

        await ApplyRescanAsync(publication, ExpensePlanModerationTriggerTypes.Rescan, cancellationToken);
        return await GetPublicationByIdAsync(publicationId, cancellationToken);
    }

    private async Task RescanPublishedPlansAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddHours(-12);
        var candidates = await dbContext.ExpensePlanPublications
            .Where(x => x.PublicationStatus == ExpensePlanPublicationStatuses.Published && (x.LastRescannedAtUtc == null || x.LastRescannedAtUtc < threshold))
            .Take(12)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await ApplyRescanAsync(candidate, ExpensePlanModerationTriggerTypes.Rescan, cancellationToken);
        }
    }

    private async Task RescanCreatorPublicationsAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddHours(-6);
        var candidates = await dbContext.ExpensePlanPublications
            .Where(x => x.CreatorUserId == currentUserProvider.UserId && (x.LastRescannedAtUtc == null || x.LastRescannedAtUtc < threshold))
            .Take(12)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await ApplyRescanAsync(candidate, ExpensePlanModerationTriggerTypes.Rescan, cancellationToken);
        }
    }

    private async Task RescanPublicationIfNeededAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddHours(-6);
        var candidate = await dbContext.ExpensePlanPublications
            .SingleOrDefaultAsync(x => x.Id == publicationId && (x.LastRescannedAtUtc == null || x.LastRescannedAtUtc < threshold), cancellationToken);

        if (candidate is not null)
        {
            await ApplyRescanAsync(candidate, ExpensePlanModerationTriggerTypes.Rescan, cancellationToken);
        }
    }

    private async Task ApplyRescanAsync(ExpensePlanPublication publication, string triggerType, CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var moderation = ExpensePlanPublicationModerationService.Scan(publication.PublicTitle, publication.PublicDescription, DeserializeTags(publication.TagsJson));
        publication.ModerationStatus = moderation.ModerationStatus;
        publication.ModerationSummary = moderation.Summary;
        publication.PublicationStatus = ResolvePublicationStatusForModeration(
            moderation,
            string.Equals(publication.PublicationStatus, ExpensePlanPublicationStatuses.Published, StringComparison.OrdinalIgnoreCase));
        publication.LastModeratedAtUtc = utcNow;
        publication.LastRescannedAtUtc = utcNow;
        publication.UpdatedAtUtc = utcNow;
        publication.TrendingScore = BuildTrendingScore(publication, utcNow);
        if (publication.PublicationStatus != ExpensePlanPublicationStatuses.Published)
        {
            publication.UnpublishedAtUtc ??= utcNow;
        }

        dbContext.ExpensePlanPublicationModerationEvents.Add(BuildModerationEvent(publication.Id, triggerType, moderation, utcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool MatchesBrowse(ExpensePlanPublication publication, BrowseExpensePlanPublicationsRequest request)
    {
        if (request.TemplatesOnly && !publication.IsTemplate)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.PlanType)
            && !string.Equals(publication.PlanType, request.PlanType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Creator))
        {
            var creatorFilter = request.Creator.Trim();
            if (!publication.CreatorDisplayNameSnapshot.Contains(creatorFilter, StringComparison.OrdinalIgnoreCase)
                && !publication.CreatorTagSnapshot.Contains(creatorFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(request.Search))
        {
            return true;
        }

        var search = request.Search.Trim();
        return publication.PublicTitle.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (publication.PublicDescription?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || publication.CreatorDisplayNameSnapshot.Contains(search, StringComparison.OrdinalIgnoreCase)
            || publication.CreatorTagSnapshot.Contains(search, StringComparison.OrdinalIgnoreCase)
            || DeserializeTags(publication.TagsJson).Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase))
            || publication.PlanType.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<ExpensePlanPublication> SortPublications(
        IEnumerable<ExpensePlanPublication> publications,
        string? sort,
        DateTime utcNow)
    {
        return (sort ?? ExpensePlanPublicationSorts.Trending).Trim().ToLowerInvariant() switch
        {
            ExpensePlanPublicationSorts.MostLiked => publications.OrderByDescending(x => x.LikeCount).ThenByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc),
            ExpensePlanPublicationSorts.MostDownloaded => publications.OrderByDescending(x => x.DownloadCount).ThenByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc),
            ExpensePlanPublicationSorts.RecentlyAdded => publications.OrderByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc),
            ExpensePlanPublicationSorts.Newest => publications.OrderByDescending(x => x.CreatedAtUtc),
            _ => publications.OrderByDescending(x => BuildTrendingScore(x, utcNow)).ThenByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc)
        };
    }

    private static decimal BuildTrendingScore(ExpensePlanPublication publication, DateTime utcNow)
    {
        var anchor = publication.PublishedAtUtc ?? publication.CreatedAtUtc;
        var ageDays = Math.Max((decimal)(utcNow - anchor).TotalDays, 1m);
        var recencyBoost = Math.Max(14m - ageDays, 0m);
        var reportPenalty = publication.ReportCount * 2m;
        if (publication.PublicationStatus == ExpensePlanPublicationStatuses.Flagged)
        {
            reportPenalty += 6m;
        }

        return decimal.Round((publication.LikeCount * 3m) + (publication.DownloadCount * 4m) + recencyBoost - reportPenalty, 4, MidpointRounding.AwayFromZero);
    }

    private static string ResolvePublicationStatusForModeration(ExpensePlanModerationScanResult moderation, bool wasPreviouslyPublished)
    {
        if (moderation.ShouldBlock)
        {
            return ExpensePlanPublicationStatuses.Blocked;
        }

        if (moderation.ShouldQueueReview)
        {
            return wasPreviouslyPublished ? ExpensePlanPublicationStatuses.Flagged : ExpensePlanPublicationStatuses.PendingReview;
        }

        return ExpensePlanPublicationStatuses.Published;
    }

    private static ExpensePlanPublicationModerationEvent BuildModerationEvent(
        Guid publicationId,
        string triggerType,
        ExpensePlanModerationScanResult moderation,
        DateTime utcNow)
    {
        return new ExpensePlanPublicationModerationEvent
        {
            Id = Guid.NewGuid(),
            PublicationId = publicationId,
            TriggerType = triggerType,
            ResultStatus = moderation.ModerationStatus,
            Summary = moderation.Summary,
            MatchedRulesJson = JsonSerializer.Serialize(moderation.MatchedRules),
            CreatedAtUtc = utcNow
        };
    }

    private static ExpensePlanPublicationSnapshot BuildSnapshot(ExpensePlan plan)
    {
        return new ExpensePlanPublicationSnapshot(
            plan.Id,
            plan.PlanVersion,
            plan.PlanType,
            plan.StartDateUtc,
            plan.EndDateUtc,
            plan.CurrencyCode,
            plan.ExpectedIncomeTotal,
            plan.ExpectedSpendTotal,
            plan.ExpectedRemainingTotal,
            plan.IsTemplate,
            plan.IsRecurring,
            plan.RecurrenceRuleJson,
            plan.LineItems
                .OrderBy(item => item.SortOrder)
                .Select(item => new ExpensePlanPublicationLineItemSnapshot(
                    item.TaxonomyDomainId,
                    ExtractSegment(item.HierarchyPathSnapshot, 0),
                    item.TaxonomyCategoryId,
                    ExtractSegment(item.HierarchyPathSnapshot, 1),
                    item.TaxonomySubcategoryId ?? 0,
                    item.DisplayNameSnapshot,
                    item.DisplayNameSnapshot,
                    item.HierarchyPathSnapshot,
                    item.ExpectedAmount,
                    item.Notes,
                    item.SortOrder))
                .ToList());
    }

    private static string ExtractSegment(string path, int index)
    {
        var segments = path.Split(" > ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > index ? segments[index] : "Unknown";
    }

    private static ExpensePlanPublicationSnapshot DeserializeSnapshot(string snapshotJson)
    {
        return JsonSerializer.Deserialize<ExpensePlanPublicationSnapshot>(snapshotJson)
            ?? throw new InvalidOperationException("Publication snapshot could not be read.");
    }

    private async Task<(string DisplayName, string Tag)> GetCreatorSnapshotsAsync(CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == currentUserProvider.UserId, cancellationToken);

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.FullName : user.DisplayName;
        var tag = !string.IsNullOrWhiteSpace(user.Handle)
            ? $"@{user.Handle.Trim()}"
            : $"@{displayName.Trim().ToLowerInvariant().Replace(' ', '_')}";

        return (displayName.Trim(), tag);
    }

    private ExpensePlanPublicationCardDto ToCardDto(ExpensePlanPublication publication, bool likedByCurrentUser, bool canManage)
    {
        return new ExpensePlanPublicationCardDto(
            publication.Id,
            publication.SourcePlanId,
            publication.CreatorUserId,
            publication.CreatorDisplayNameSnapshot,
            publication.CreatorTagSnapshot,
            publication.PublicTitle,
            publication.PublicDescription,
            DeserializeTags(publication.TagsJson),
            publication.PublicationStatus,
            publication.ModerationStatus,
            publication.PlanType,
            publication.IsTemplate,
            publication.IsRecurring,
            publication.ExpectedSpendTotal,
            publication.LikeCount,
            publication.DownloadCount,
            publication.ReportCount,
            publication.TrendingScore,
            publication.CreatedAtUtc,
            publication.PublishedAtUtc,
            likedByCurrentUser,
            canManage);
    }

    private ExpensePlanPublicationDetailDto ToDetailDto(ExpensePlanPublication publication, bool likedByCurrentUser, bool canManage)
    {
        var snapshot = DeserializeSnapshot(publication.PlanSnapshotJson);
        return new ExpensePlanPublicationDetailDto(
            publication.Id,
            publication.SourcePlanId,
            publication.CreatorUserId,
            publication.CreatorDisplayNameSnapshot,
            publication.CreatorTagSnapshot,
            publication.PublicTitle,
            publication.PublicDescription,
            DeserializeTags(publication.TagsJson),
            publication.PublicationStatus,
            publication.ModerationStatus,
            publication.ModerationSummary,
            publication.PlanType,
            publication.CurrencyCode,
            publication.IsTemplate,
            publication.IsRecurring,
            publication.ExpectedSpendTotal,
            publication.LikeCount,
            publication.DownloadCount,
            publication.ReportCount,
            publication.TrendingScore,
            publication.CreatedAtUtc,
            publication.PublishedAtUtc,
            publication.LastModeratedAtUtc,
            publication.LastRescannedAtUtc,
            publication.LastReportedAtUtc,
            likedByCurrentUser,
            canManage,
            snapshot.LineItems.Select(item => new ExpensePlanPublicationLineItemDto(
                item.TaxonomyDomainId,
                item.DomainName,
                item.TaxonomyCategoryId,
                item.CategoryName,
                item.TaxonomySubcategoryId,
                item.SubcategoryName,
                item.DisplayNameSnapshot,
                item.HierarchyPathSnapshot,
                item.ExpectedAmount,
                item.Notes,
                item.SortOrder)).ToList(),
            publication.ModerationEvents
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => new ExpensePlanPublicationModerationEventDto(
                    item.Id,
                    item.TriggerType,
                    item.ResultStatus,
                    item.Summary,
                    DeserializeTags(item.MatchedRulesJson),
                    item.CreatedAtUtc)).ToList(),
            canManage
                ? publication.Reports
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .Select(item => new ExpensePlanPublicationReportDto(
                        item.Id,
                        item.ReporterUserId,
                        item.Reason,
                        item.Notes,
                        item.Status,
                        item.CreatedAtUtc)).ToList()
                : []);
    }

    private ExpensePlanDto ToPlanDto(ExpensePlan plan, IReadOnlyList<ExpenseTrackerEntry> entries, DateTime utcNow)
    {
        var comparison = ExpensePlanComparisonService.BuildComparison(plan, entries, new ExpenseTaxonomyService(), utcNow);
        var lineItems = plan.LineItems
            .OrderBy(item => item.SortOrder)
            .Select(item => new ExpensePlanLineItemDto(
                item.Id,
                item.PlanId,
                item.TaxonomyDomainId,
                ExtractSegment(item.HierarchyPathSnapshot, 0),
                item.TaxonomyCategoryId,
                ExtractSegment(item.HierarchyPathSnapshot, 1),
                item.TaxonomySubcategoryId,
                item.DisplayNameSnapshot,
                item.DisplayNameSnapshot,
                item.HierarchyPathSnapshot,
                item.ExpectedAmount,
                item.Notes,
                item.SortOrder,
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToList();

        return new ExpensePlanDto(
            plan.Id,
            plan.UserId,
            plan.CreatorDisplayNameSnapshot,
            plan.CreatorTagSnapshot,
            plan.Title,
            plan.Description,
            plan.Notes,
            plan.Status,
            plan.PlanType,
            plan.PlanOriginType,
            plan.PlanVersion,
            plan.StartDateUtc,
            plan.EndDateUtc,
            plan.CurrencyCode,
            plan.ExpectedIncomeTotal,
            plan.ExpectedSpendTotal,
            plan.ExpectedRemainingTotal,
            DeserializeTags(plan.TagsJson),
            plan.StatusReason,
            plan.CreatedAtUtc,
            plan.UpdatedAtUtc,
            plan.ActivatedAtUtc,
            plan.CompletedAtUtc,
            plan.LockedAtUtc,
            plan.ArchivedAtUtc,
            plan.CancelledAtUtc,
            plan.LastCalculatedAtUtc,
            plan.SourcePlanId,
            plan.ImportedFromPublicPlanId,
            plan.IsTemplate,
            plan.IsRecurring,
            DeserializeRecurrence(plan.RecurrenceRuleJson),
            plan.IsShared,
            plan.SharingMode,
            plan.SharedIdentity,
            comparison,
            lineItems);
    }

    private static ExpensePlanRecurrenceDto? DeserializeRecurrence(string? recurrenceJson)
    {
        if (string.IsNullOrWhiteSpace(recurrenceJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ExpensePlanRecurrenceDto>(recurrenceJson);
        }
        catch
        {
            return null;
        }
    }

    private bool CanInteractWithPublication(ExpensePlanPublication publication)
    {
        return ExpensePlanPublicationStatuses.PubliclyVisible.Contains(publication.PublicationStatus)
            || publication.CreatorUserId == currentUserProvider.UserId;
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        return (tags ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<string> DeserializeTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
