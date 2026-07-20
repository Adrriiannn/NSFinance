namespace NSFinance.Api.Persistence.Entities;

// Review-queue and cooldown ledger for the merchant knowledge growth loop
// (CAT-001): every unknown descriptor the AI worker considers gets exactly
// one row here. Promotion writes a MerchantKnowledge row and links it;
// uncertain outcomes park as needs_review with their full investigation
// summary so a human can decide - nothing is ever categorized silently.
public class MerchantKnowledgeCandidate
{
    public Guid Id { get; set; }

    // Uppercase normalized statement text this candidate was observed as.
    public string NormalizedDescriptor { get; set; } = string.Empty;

    public string RawDescriptorSample { get; set; } = string.Empty;

    // pending | promoted | needs_review | rejected
    public string Status { get; set; } = MerchantKnowledgeCandidateStatuses.Pending;

    public int ObservedOccurrences { get; set; }
    public decimal ObservedSpendAbs { get; set; }

    // outflow | inflow | mixed - across the observed transactions.
    public string ObservedDirection { get; set; } = "outflow";

    public int AttemptCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextEligibleUtc { get; set; }
    public string? LastOutcomeCode { get; set; }

    // Full audit of the last run: acceptance decision, evidence summaries,
    // and category judgment - the data a review surface renders.
    public string? InvestigationSummaryJson { get; set; }

    public int? ProposedTaxonomyDomainId { get; set; }
    public int? ProposedTaxonomyCategoryId { get; set; }
    public int? ProposedTaxonomySubcategoryId { get; set; }
    public double? ProposedConfidence { get; set; }

    public Guid? PromotedKnowledgeId { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public static class MerchantKnowledgeCandidateStatuses
{
    public const string Pending = "pending";
    public const string Promoted = "promoted";
    public const string NeedsReview = "needs_review";
    public const string Rejected = "rejected";
}
