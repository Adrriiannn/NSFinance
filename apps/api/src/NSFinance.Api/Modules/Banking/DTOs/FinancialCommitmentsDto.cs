namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record FinancialCommitmentsDto(
    DateTime AsOfUtc,
    int Limit,
    bool IsTruncated,
    IReadOnlyList<FinancialCommitmentDto> Items);

public sealed record FinancialCommitmentDto(
    string Id,
    string Kind,
    string Lifecycle,
    string Source,
    string Confidence,
    double? ConfidenceScore,
    string Direction,
    Guid? AccountId,
    Guid? LinkedBankAccountId,
    string AccountDisplayName,
    string Label,
    string? Cadence,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    DateTime? LastObservedDateUtc,
    decimal? LastObservedAmount,
    string? LastObservedCurrency,
    DateTime? NextDateUtc,
    string DateCertainty,
    decimal? NextAmount,
    string? Currency,
    string AmountCertainty,
    bool? IsVariableAmount,
    DateTime SourceUpdatedUtc,
    string Freshness,
    bool AnalyticsNeutral,
    string? ProviderStatus,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<FinancialCommitmentEvidenceDto> Evidence,
    FinancialCommitmentUserDecisionDto? UserDecision = null);

public sealed record FinancialCommitmentEvidenceDto(
    string Type,
    Guid SourceRecordId,
    DateTime ObservedUtc,
    string Authority,
    IReadOnlyList<string> ReasonCodes);

public sealed record FinancialCommitmentUserDecisionDto(
    string State,
    string DecisionMode,
    string LastAction,
    int Revision,
    DateTime UpdatedUtc);
