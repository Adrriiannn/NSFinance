using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed record DeterministicTransactionFeature(
    Guid TransactionId,
    Guid FinancialAccountId,
    decimal SignedAmount,
    decimal AbsoluteAmount,
    bool IsOutflow,
    bool IsInflow,
    string Currency,
    DateTime BookedAtUtc,
    string NormalizedDescription,
    IReadOnlySet<string> Tokens,
    bool HasTransferKeyword,
    bool HasSavingsKeyword,
    bool HasStrongSavingsKeyword,
    string? AccountHint,
    bool IsBooked,
    bool IsPending,
    bool HasProviderTransferHint,
    int NearbySameAmountCount,
    bool HasCounterpartyAccounts,
    double ReferenceEntropy);

public sealed record DeterministicClassificationOutcome(
    DeterministicClassificationStatus Status,
    bool Terminal,
    bool RetryEligible,
    string RuleKey,
    string ReasonCode,
    string EvidenceJson,
    int? MatchScore,
    int? ClassificationCategoryId,
    int? ClassificationSubcategoryId,
    Guid? LinkedTransactionId,
    string? RelationshipType,
    Guid? RelationshipGroupId);

public sealed record DeterministicCategorizationSummary(
    int RowsSelected,
    int RowsEvaluated,
    int RowsTerminal,
    int RowsClassifiedBankTransfer,
    int RowsClassifiedSavingsTransfer,
    int RowsNoMatch,
    int RowsDeferredCounterparty,
    int RowsDeferredContext,
    int RowsRejectedAmbiguous,
    int RowsRetryQueued,
    int PairingAttemptCount,
    int PairingSuccessCount,
    int RelationshipRowsUpserted,
    bool HasChanges);

public sealed record TransferPairDecision(
    Guid DebitTransactionId,
    Guid CreditTransactionId,
    string RuleKey,
    string ReasonCode,
    int Score,
    string EvidenceJson);

public sealed record TransferPendingDecision(
    Guid TransactionId,
    DeterministicClassificationStatus Status,
    string ReasonCode,
    bool RetryEligible,
    string EvidenceJson);
