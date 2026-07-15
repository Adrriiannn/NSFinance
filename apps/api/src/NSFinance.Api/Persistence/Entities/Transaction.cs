namespace NSFinance.Api.Persistence.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid FinancialAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Description { get; set; } = string.Empty;
    public string EntryKind { get; set; } = TransactionEntryKinds.Ordinary;
    public string AnalyticsTreatment { get; set; } = TransactionAnalyticsTreatments.Ordinary;
    public DateTime BookedAtUtc { get; set; }
    public Guid? CategoryId { get; set; }
    public int? TaxonomyDomainId { get; set; }
    public int? TaxonomyCategoryId { get; set; }
    public int? TaxonomySubcategoryId { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public TransactionTransferKind? TransferKind { get; set; }
    public Guid? LinkedTransferTransactionId { get; set; }
    public DateTime? LinkedTransferMatchedUtc { get; set; }
    public int? TransferMatchConfidenceScore { get; set; }
    public string? TransferMatchConfidenceTier { get; set; }
    public string? TransferMatchReason { get; set; }
    public int? DeterministicEnrichmentVersion { get; set; }
    public DateTime? LastDeterministicEnrichedUtc { get; set; }
    public DeterministicClassificationStatus DeterministicClassificationStatus { get; set; } = DeterministicClassificationStatus.NotEvaluated;
    public int? DeterministicClassificationVersion { get; set; }
    public string? DeterministicClassificationRuleKey { get; set; }
    public int? DeterministicClassificationCategoryId { get; set; }
    public int? DeterministicClassificationSubcategoryId { get; set; }
    public Guid? DeterministicLinkedTransactionId { get; set; }
    public string? DeterministicRelationshipType { get; set; }
    public Guid? DeterministicRelationshipGroupId { get; set; }
    public int? DeterministicMatchScore { get; set; }
    public string? DeterministicReasonCode { get; set; }
    public string? DeterministicReasonDetailJson { get; set; }
    public DateTime? DeterministicClassificationEvaluatedUtc { get; set; }
    public bool DeterministicClassificationTerminal { get; set; }
    public bool DeterministicDeferredRetryEligible { get; set; }
    public DateTime? DeterministicLastRetryConsideredUtc { get; set; }
    public bool NeedsDeterministicReclassification { get; set; }
    public string? DeterministicSourceSignature { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? MetadataUpdatedUtc { get; set; }

    public FinancialAccount? FinancialAccount { get; set; }
    public TransactionCategory? Category { get; set; }
    public StatementImportRow? StatementImportRow { get; set; }
}
