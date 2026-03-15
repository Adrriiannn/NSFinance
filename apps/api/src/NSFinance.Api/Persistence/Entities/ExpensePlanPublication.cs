namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlanPublication
{
    public Guid Id { get; set; }
    public Guid CreatorUserId { get; set; }
    public Guid SourcePlanId { get; set; }
    public int SourcePlanVersion { get; set; }
    public string CreatorDisplayNameSnapshot { get; set; } = string.Empty;
    public string CreatorTagSnapshot { get; set; } = string.Empty;
    public string PublicTitle { get; set; } = string.Empty;
    public string? PublicDescription { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string PublicationStatus { get; set; } = "draft_publication";
    public string ModerationStatus { get; set; } = "approved";
    public string? ModerationSummary { get; set; }
    public string PlanSnapshotJson { get; set; } = "{}";
    public string PlanType { get; set; } = "monthly";
    public string CurrencyCode { get; set; } = "EUR";
    public decimal ExpectedSpendTotal { get; set; }
    public bool IsTemplate { get; set; }
    public bool IsRecurring { get; set; }
    public int LikeCount { get; set; }
    public int DownloadCount { get; set; }
    public int ReportCount { get; set; }
    public decimal TrendingScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? LastModeratedAtUtc { get; set; }
    public DateTime? LastRescannedAtUtc { get; set; }
    public DateTime? LastReportedAtUtc { get; set; }
    public DateTime? UnpublishedAtUtc { get; set; }
    public DateTime? RemovedAtUtc { get; set; }

    public User? CreatorUser { get; set; }
    public ExpensePlan? SourcePlan { get; set; }
    public ICollection<ExpensePlan> ImportedPlans { get; set; } = [];
    public ICollection<ExpensePlanPublicationLike> Likes { get; set; } = [];
    public ICollection<ExpensePlanPublicationDownload> Downloads { get; set; } = [];
    public ICollection<ExpensePlanPublicationReport> Reports { get; set; } = [];
    public ICollection<ExpensePlanPublicationModerationEvent> ModerationEvents { get; set; } = [];
}
