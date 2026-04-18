namespace NSFinance.Api.Modules.AI.Services;

public enum FinancialAdviceFindingType
{
    CategoryPressure = 0,
    RecurringSpendPressure = 1,
    DiscretionaryOverspend = 2,
    BudgetSlippage = 3,
    AffordabilityRisk = 4,
    PlanDrift = 5,
    PositiveProgress = 6,
    InsufficientEvidence = 7,
    NoMaterialIssueDetected = 8
}

public enum FinancialAdviceSeverity
{
    Info = 0,
    Low = 1,
    Moderate = 2,
    High = 3,
    Critical = 4
}

public enum FinancialAdviceActionType
{
    ReviewSpend = 0,
    ReduceSpend = 1,
    TrackRecurringCharge = 2,
    AdjustBudget = 3,
    BuildBuffer = 4,
    ReviewPlan = 5,
    KeepCourse = 6
}

public enum FinancialAdvicePolicyDecision
{
    Approved = 0,
    ApprovedWithAdjustments = 1,
    Rejected = 2
}

public enum FinancialAdviceAdjudicationMode
{
    Skipped = 0,
    Optional = 1,
    Required = 2
}

public enum FinancialAdviceAdjudicationOutcome
{
    Approve = 0,
    ApproveWithRefinement = 1,
    LowerConfidence = 2,
    RejectConclusion = 3,
    InsufficientEvidence = 4
}

public enum FinancialAdviceFreshnessState
{
    Fresh = 0,
    NeedsRefresh = 1,
    Stale = 2
}

public enum FinancialAdviceConclusionSource
{
    DeterministicOnly = 0,
    Adjudicated = 1
}

public sealed record FinancialAdviceEvidenceMetric(
    string Key,
    decimal Value,
    string Unit,
    string? DisplayValue = null);

public sealed record FinancialAdviceActionCandidate(
    string ActionId,
    FinancialAdviceActionType ActionType,
    string Title,
    string Guidance,
    decimal? SuggestedMagnitude = null,
    int? TargetDomainCode = null,
    int? TargetCategoryCode = null,
    bool IsProtectedCategory = false);

public sealed record FinancialAdviceFreshnessMetadata(
    DateTime ComputedAtUtc,
    DateTime EvidencePeriodStartUtc,
    DateTime EvidencePeriodEndUtc,
    DateTime FreshUntilUtc,
    DateTime RecheckAfterUtc,
    FinancialAdviceFreshnessState FreshnessState,
    double ConfidenceDecayPerDay,
    double RelevanceScore,
    bool RequiresRecheck,
    IReadOnlyList<string> InvalidationHints);

public sealed record FinancialAdviceFinding(
    string FindingId,
    FinancialAdviceFindingType FindingType,
    FinancialCompanionIntent RelatedIntent,
    FinancialAdviceSeverity Severity,
    int PriorityScore,
    double Confidence,
    string EvidenceSummary,
    IReadOnlyList<FinancialAdviceEvidenceMetric> SupportingMetrics,
    int? DomainCode,
    string? DomainName,
    int? CategoryCode,
    string? CategoryName,
    IReadOnlyList<string> ProtectedCategoryFlags,
    IReadOnlyList<FinancialAdviceActionCandidate> RecommendedActions,
    IReadOnlyList<string> UncertaintyMarkers,
    IReadOnlyList<string> PolicyWarnings,
    IReadOnlyList<string> PolicyExclusions,
    bool AiAdjudicationAllowed,
    bool AiAdjudicationRecommended,
    FinancialAdviceFreshnessMetadata Freshness,
    IReadOnlyDictionary<string, string> RenderingHints);

public sealed record FinancialAdvicePolicyReviewedFinding(
    FinancialAdviceFinding Finding,
    FinancialAdvicePolicyDecision Decision,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Exclusions);

public sealed record FinancialAdviceAdjudicationPlan(
    FinancialAdviceAdjudicationMode Mode,
    IReadOnlyList<string> TargetFindingIds,
    IReadOnlyList<string> ReasonCodes);
