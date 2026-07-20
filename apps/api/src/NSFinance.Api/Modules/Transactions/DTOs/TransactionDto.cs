namespace NSFinance.Api.Modules.Transactions.DTOs;

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string Description,
    decimal Amount,
    string Currency,
    Guid? CategoryId,
    string? CategoryName,
    int? TaxonomyDomainId,
    string? TaxonomyDomainName,
    int? TaxonomyCategoryId,
    string? TaxonomyCategoryName,
    int? TaxonomySubcategoryId,
    string? TaxonomySubcategoryName,
    string? TransferKind,
    Guid? LinkedTransferTransactionId,
    string DeterministicClassificationStatus,
    bool DeterministicClassificationTerminal,
    int? DeterministicClassificationVersion,
    string? DeterministicClassificationRuleKey,
    string? DeterministicClassificationReasonCode,
    string? DeterministicClassificationEvidenceJson,
    bool DeterministicDeferredRetryEligible,
    Guid? DeterministicLinkedTransactionId,
    string? DeterministicRelationshipType,
    Guid? DeterministicRelationshipGroupId,
    int? TransferMatchConfidenceScore,
    string? TransferMatchConfidenceTier,
    string? TransferMatchReason,
    string? RelationshipType,
    string? RelationshipStatus,
    string? RelationshipDirection,
    int? RelationshipConfidenceScore,
    string? RelationshipConfidenceTier,
    string? RelationshipAnalyticsTreatment,
    string? RelationshipVirtualDestinationLabel,
    Guid? RelationshipCounterpartyTransactionId,
    string DisplaySemantic,
    string? TransferPolicyKind,
    string ReportingBucket,
    bool IsGloballyNeutralized,
    string? Reason,
    string? Notes,
    DateTime BookedAtUtc,
    DateTime CreatedUtc,
    DateTime? MetadataUpdatedUtc,
    string Direction,
    string EntryKind,
    string AnalyticsTreatment,
    string AccountSource,
    string AccountCurrency,
    TransactionEffectiveTimeDto EffectiveTime,
    StatementImportProvenanceDto? StatementImport,
    TransactionCategorizationEvidenceDto? CategorizationEvidence = null);

// Why this category (CAT-001 explainability): present when an automatic
// pass or a manual correction assigned the taxonomy. Knowledge enrichment
// (source, merchant name, confidence) is resolved on the detail read.
public sealed record TransactionCategorizationEvidenceDto(
    string RuleKey,
    string? Signal,
    int? CharacteristicsVersion,
    DateTime? CategorizedUtc,
    string? KnowledgeSource = null,
    string? MerchantDisplayName = null,
    double? Confidence = null);

public sealed record TransactionEffectiveTimeDto(
    string Precision,
    DateOnly? Date,
    DateTime? InstantUtc);

public sealed record StatementImportProvenanceDto(
    Guid BatchId,
    int RowNumber,
    DateTime CommittedUtc);
