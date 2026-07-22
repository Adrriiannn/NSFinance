namespace NSFinance.Api.Persistence.Entities;

// Curation findings over the global merchant knowledge base (CAT-001 phase
// two): recurring checks record what they distrust here - one open issue per
// knowledge row and issue type - so suspect rows surface for review instead
// of silently drifting. Issues auto-resolve when the evidence that raised
// them goes away.
public class MerchantKnowledgeCurationIssue
{
    public Guid Id { get; set; }
    public Guid KnowledgeId { get; set; }

    // correction_conflict | (future: stale_verification, overlap, dead_pattern)
    public string IssueType { get; set; } = string.Empty;

    // open | resolved
    public string Status { get; set; } = MerchantKnowledgeCurationIssueStatuses.Open;

    // What the check saw, for the review surface. Aggregates only - never
    // user identifiers.
    public string? EvidenceJson { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public static class MerchantKnowledgeCurationIssueStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public static class MerchantKnowledgeCurationIssueTypes
{
    public const string CorrectionConflict = "correction_conflict";
}
