namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceInsightItem(
    string InsightId,
    string FindingId,
    FinancialAdviceFindingType FindingType,
    FinancialAdviceSeverity Severity,
    int PriorityScore,
    double Confidence,
    string Headline,
    string Summary,
    IReadOnlyList<FinancialAdviceActionCandidate> Actions,
    FinancialAdviceFreshnessMetadata Freshness,
    FinancialAdviceConclusionSource ConclusionSource);

public sealed record FinancialAdviceEvidenceWindow(
    DateTime StartUtc,
    DateTime EndUtc,
    string Label);

public sealed record FinancialAdviceDecisionPacket(
    DateTime ComputedAtUtc,
    FinancialCompanionIntent Intent,
    FinancialAdviceConclusionSource ConclusionSource,
    FinancialAdviceEvidenceWindow EvidenceWindow,
    IReadOnlyList<FinancialAdviceFinding> DeterministicFindings,
    IReadOnlyList<FinancialAdvicePolicyReviewedFinding> PolicyReviewedFindings,
    FinancialAdviceAdjudicationResult Adjudication,
    IReadOnlyList<FinancialAdviceInsightItem> FinalInsights,
    IReadOnlyList<string> EvidenceSummary,
    IReadOnlyList<string> Warnings,
    string UserSafeSummary,
    int HighestPriorityScore,
    bool RequiresRefresh,
    IReadOnlyList<string> RefreshHints);

public sealed record FinancialAdviceDecisionResult(
    FinancialAdviceDecisionPacket Packet,
    string ModelUsed,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<string> Warnings);
