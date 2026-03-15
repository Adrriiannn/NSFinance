namespace NSFinance.Api.Modules.ExpenseTracker.DTOs;

public sealed record ExpensePlanPublicationLineItemDto(
    int TaxonomyDomainId,
    string DomainName,
    int TaxonomyCategoryId,
    string CategoryName,
    int TaxonomySubcategoryId,
    string SubcategoryName,
    string DisplayNameSnapshot,
    string HierarchyPathSnapshot,
    decimal ExpectedAmount,
    string? Notes,
    int SortOrder);

public sealed record ExpensePlanPublicationCardDto(
    Guid Id,
    Guid SourcePlanId,
    Guid CreatorUserId,
    string CreatorDisplayNameSnapshot,
    string CreatorTagSnapshot,
    string PublicTitle,
    string? PublicDescription,
    IReadOnlyList<string> Tags,
    string PublicationStatus,
    string ModerationStatus,
    string PlanType,
    bool IsTemplate,
    bool IsRecurring,
    decimal ExpectedSpendTotal,
    int LikeCount,
    int DownloadCount,
    int ReportCount,
    decimal TrendingScore,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    bool LikedByCurrentUser,
    bool CanCurrentUserManage);

public sealed record ExpensePlanPublicationModerationEventDto(
    Guid Id,
    string TriggerType,
    string ResultStatus,
    string Summary,
    IReadOnlyList<string> MatchedRules,
    DateTime CreatedAtUtc);

public sealed record ExpensePlanPublicationReportDto(
    Guid Id,
    Guid ReporterUserId,
    string Reason,
    string? Notes,
    string Status,
    DateTime CreatedAtUtc);

public sealed record ExpensePlanPublicationDetailDto(
    Guid Id,
    Guid SourcePlanId,
    Guid CreatorUserId,
    string CreatorDisplayNameSnapshot,
    string CreatorTagSnapshot,
    string PublicTitle,
    string? PublicDescription,
    IReadOnlyList<string> Tags,
    string PublicationStatus,
    string ModerationStatus,
    string? ModerationSummary,
    string PlanType,
    string CurrencyCode,
    bool IsTemplate,
    bool IsRecurring,
    decimal ExpectedSpendTotal,
    int LikeCount,
    int DownloadCount,
    int ReportCount,
    decimal TrendingScore,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? LastModeratedAtUtc,
    DateTime? LastRescannedAtUtc,
    DateTime? LastReportedAtUtc,
    bool LikedByCurrentUser,
    bool CanCurrentUserManage,
    IReadOnlyList<ExpensePlanPublicationLineItemDto> LineItems,
    IReadOnlyList<ExpensePlanPublicationModerationEventDto> ModerationEvents,
    IReadOnlyList<ExpensePlanPublicationReportDto> Reports);

public sealed record ExpensePlanPublicationDashboardItemDto(
    Guid Id,
    Guid SourcePlanId,
    string PublicTitle,
    string PublicationStatus,
    string ModerationStatus,
    int LikeCount,
    int DownloadCount,
    int ReportCount,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    bool IsTemplate,
    bool IsRecurring);

public sealed record ExpensePlanPublicationDashboardDto(
    int PublishedPlanCount,
    int PendingReviewCount,
    int FlaggedCount,
    int TotalLikes,
    int TotalDownloads,
    int TotalReports,
    IReadOnlyList<ExpensePlanPublicationDashboardItemDto> Plans);
