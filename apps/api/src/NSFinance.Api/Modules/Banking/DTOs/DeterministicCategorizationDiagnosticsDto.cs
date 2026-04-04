namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record DeterministicCategorizationStatusCountDto(
    string Status,
    int Count);

public sealed record DeterministicTransactionDecisionDto(
    Guid TransactionId,
    Guid FinancialAccountId,
    decimal Amount,
    string Currency,
    DateTime BookedAtUtc,
    string ClassificationStatus,
    bool Terminal,
    bool RetryEligible,
    int? ClassificationVersion,
    string? RuleKey,
    string? ReasonCode,
    Guid? LinkedTransactionId,
    string? RelationshipType,
    Guid? RelationshipGroupId,
    int? MatchScore,
    DateTime? EvaluatedUtc);

public sealed record DeterministicCategorizationDiagnosticsDto(
    Guid ConnectionId,
    int ClassificationVersion,
    int TotalTransactions,
    int TerminalTransactions,
    int NonTerminalTransactions,
    int ActionableRemainingTransactions,
    int DeferredMoreContextTransactions,
    int DeferredCounterpartyTransactions,
    bool QueueEligible,
    string QueueEligibilityReason,
    string ContinuationDecision,
    string ContinuationReason,
    IReadOnlyList<DeterministicCategorizationStatusCountDto> StatusCounts,
    IReadOnlyList<DeterministicTransactionDecisionDto> SampleDecisions);
