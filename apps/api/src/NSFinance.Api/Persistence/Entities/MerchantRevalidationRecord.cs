namespace NSFinance.Api.Persistence.Entities;

public class MerchantRevalidationRecord
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public DateTime AttemptedUtc { get; set; }
    public string TriggerReason { get; set; } = string.Empty;
    public MerchantRevalidationOutcome Outcome { get; set; } = MerchantRevalidationOutcome.KeepCautious;
    public string? DecisionCode { get; set; }
    public MerchantStatus PreviousStatus { get; set; } = MerchantStatus.LowConfidence;
    public MerchantStatus NewStatus { get; set; } = MerchantStatus.LowConfidence;
    public bool StatusChanged { get; set; }
    public int AliasTrustChanges { get; set; }
    public bool RequiresUnresolvedReview { get; set; }
    public bool ContradictionDetected { get; set; }
    public string? LeadingEvidenceSummary { get; set; }
    public string? ResultCode { get; set; }
    public string? DetailsJson { get; set; }

    public Merchant Merchant { get; set; } = null!;
}
