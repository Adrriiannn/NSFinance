namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record CreateManualFinancialCommitmentRequest(
    Guid? AccountId,
    string? Label,
    string? Cadence,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    DateTimeOffset? NextDateUtc,
    decimal? NextAmount,
    string? Currency,
    bool? IsVariableAmount);

public sealed record FinancialCommitmentDecisionRequest(
    string? Action,
    int? ExpectedRevision,
    Guid? AccountId,
    bool ClearAccount,
    string? Label,
    string? Cadence,
    bool ClearCadence,
    DateTimeOffset? NextDateUtc,
    bool ClearNextDate,
    decimal? NextAmount,
    bool ClearNextAmount,
    string? Currency,
    bool ClearCurrency,
    bool? IsVariableAmount,
    bool ClearVariableAmount,
    IReadOnlyList<string>? ResetFields);

public sealed record FinancialCommitmentMutationDto(
    string Id,
    string? TargetCommitmentId,
    string State,
    string DecisionMode,
    string LastAction,
    int Revision,
    DateTime UpdatedUtc);
