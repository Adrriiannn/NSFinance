namespace NSFinance.Api.Modules.ExpenseTracker.Models;

public static class ExpensePlanPublicationStatuses
{
    public const string DraftPublication = "draft_publication";
    public const string PendingReview = "pending_review";
    public const string Published = "published";
    public const string Blocked = "blocked";
    public const string Unpublished = "unpublished";
    public const string Flagged = "flagged";
    public const string Removed = "removed";

    public static readonly IReadOnlySet<string> PubliclyVisible = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Published
    };
}

public static class ExpensePlanModerationStatuses
{
    public const string Approved = "approved";
    public const string Blocked = "blocked";
    public const string NeedsReview = "needs_review";
    public const string FlaggedAfterPublish = "flagged_after_publish";
}

public static class ExpensePlanModerationTriggerTypes
{
    public const string PrePublish = "pre_publish";
    public const string MetadataUpdate = "metadata_update";
    public const string Rescan = "rescan";
    public const string ReportThreshold = "report_threshold";
}

public static class ExpensePlanPublicationSorts
{
    public const string Trending = "trending";
    public const string MostLiked = "most_liked";
    public const string MostDownloaded = "most_downloaded";
    public const string RecentlyAdded = "recently_added";
    public const string Newest = "newest";
}

public static class ExpensePlanReportReasons
{
    public const string Spam = "spam";
    public const string Abusive = "abusive_offensive";
    public const string Misleading = "misleading";
    public const string Inappropriate = "inappropriate";
    public const string Duplicate = "duplicate";
    public const string DangerousAdvice = "dangerous_financial_advice";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Spam,
        Abusive,
        Misleading,
        Inappropriate,
        Duplicate,
        DangerousAdvice,
        Other
    };
}

public static class ExpensePlanReportStatuses
{
    public const string Open = "open";
    public const string Reviewed = "reviewed";
    public const string Dismissed = "dismissed";
}
