namespace NSFinance.Api.Persistence.Entities;

public class TransactionRelationship
{
    public Guid Id { get; set; }
    public string RelationshipKey { get; set; } = string.Empty;
    public TransactionRelationshipType RelationshipType { get; set; }
    public TransactionRelationshipStatus RelationshipStatus { get; set; } = TransactionRelationshipStatus.Active;
    public TransactionRelationshipDirection RelationshipDirection { get; set; } = TransactionRelationshipDirection.None;
    public Guid SourceTransactionId { get; set; }
    public Guid? TargetTransactionId { get; set; }
    public Guid? SourceRawBankTransactionId { get; set; }
    public Guid? TargetRawBankTransactionId { get; set; }
    public Guid SourceFinancialAccountId { get; set; }
    public Guid? TargetFinancialAccountId { get; set; }
    public int ConfidenceScore { get; set; }
    public string ConfidenceTier { get; set; } = "low";
    public string? MatchReasonsJson { get; set; }
    public string? ProviderPolicyKey { get; set; }
    public string? AnalyticsTreatment { get; set; }
    public string? VirtualDestinationLabel { get; set; }
    public Guid? RelationshipGroupId { get; set; }
    public string? DeterministicRelationshipType { get; set; }
    public string? PairingStatus { get; set; }
    public DateTime? PairedUtc { get; set; }
    public string? PairingRuleKey { get; set; }
    public string? PairingEvidenceJson { get; set; }
    public Guid? SourceConnectionId { get; set; }
    public Guid? TargetConnectionId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
