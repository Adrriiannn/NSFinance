namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceAdjudicationStructuredResponse(
    string? Outcome,
    string? SummaryRefinement,
    IReadOnlyList<FinancialAdviceAdjudicationStructuredAdjustment>? Adjustments,
    IReadOnlyList<string>? Warnings,
    string? Rationale);

public sealed record FinancialAdviceAdjudicationStructuredAdjustment(
    string? FindingId,
    string? Outcome,
    double? ConfidenceDelta,
    string? RefinedSummary,
    IReadOnlyList<string>? NuanceNotes,
    string? ReasonCode);

public sealed record FinancialAdviceAdjudicationValidationRequest(
    FinancialAdviceAdjudicationStructuredResponse Response,
    IReadOnlyList<FinancialAdviceFinding> TargetFindings,
    IReadOnlyList<string> EvidenceSummary,
    IReadOnlyList<string> PlanReasonCodes);

public sealed record FinancialAdviceAdjudicationValidationResult(
    IReadOnlyList<FinancialAdviceFindingAdjudication> FindingOutcomes,
    string? ResponseSummary,
    IReadOnlyList<string> Warnings);
