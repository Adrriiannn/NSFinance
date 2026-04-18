namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceFindingForAdjudication(
    string FindingId,
    FinancialAdviceFindingType FindingType,
    FinancialAdviceSeverity Severity,
    int PriorityScore,
    double Confidence,
    string EvidenceSummary,
    IReadOnlyList<FinancialAdviceEvidenceMetric> SupportingMetrics,
    IReadOnlyList<string> UncertaintyMarkers,
    IReadOnlyList<string> PolicyWarnings,
    IReadOnlyList<FinancialAdviceActionCandidate> RecommendedActions);

public sealed record FinancialAdviceProfileSummaryForAdjudication(
    string Currency,
    string? MonthlyIncomeRange,
    string AdviceStylePreference,
    decimal? BaselineRecurringMonthly,
    decimal? BaselineAverageDailySpend,
    IReadOnlyList<string> ProtectedPreferenceHints);

public sealed record FinancialAdviceAdjudicationInputPacket(
    string UserQuery,
    FinancialCompanionIntent Intent,
    FinancialAdviceAdjudicationMode Mode,
    IReadOnlyList<FinancialAdviceFindingForAdjudication> Findings,
    FinancialAdviceProfileSummaryForAdjudication ProfileSummary,
    IReadOnlyList<string> EvidenceSummary,
    IReadOnlyList<string> UncertaintyFlags,
    IReadOnlyList<string> PolicyConstraints,
    IReadOnlyList<string> AllowedOutcomes);

public sealed record FinancialAdviceFindingAdjudication(
    string FindingId,
    FinancialAdviceAdjudicationOutcome Outcome,
    string? RefinedSummary,
    double? ConfidenceDelta,
    IReadOnlyList<string> NuanceNotes,
    string? ReasonCode);

public sealed record FinancialAdviceAdjudicationResult(
    bool UsedAi,
    bool Succeeded,
    FinancialAdviceAdjudicationMode Mode,
    string ModelUsed,
    int InputTokens,
    int OutputTokens,
    string? ResponseSummary,
    IReadOnlyList<FinancialAdviceFindingAdjudication> FindingOutcomes,
    IReadOnlyList<string> Warnings);
