namespace NSFinance.Api.Persistence.Entities;

public class MerchantEvidence
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public MerchantEvidenceType EvidenceType { get; set; } = MerchantEvidenceType.Deterministic;
    public string EvidenceSummary { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? SourceReference { get; set; }
    public DateTime CapturedUtc { get; set; }

    public Merchant? Merchant { get; set; }
}
