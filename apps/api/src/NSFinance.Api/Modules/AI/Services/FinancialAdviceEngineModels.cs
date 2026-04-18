namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceEvaluationContext(
    FinancialCompanionRequest Request,
    CompanionIntentRoutingResult Routing,
    FinancialCompanionContext Context,
    DateTime NowUtc,
    CompanionFinancialSummaryContext? Summary,
    CompanionSpendingAnalysisContext? Spending,
    CompanionRecurringObligationsContext? Recurring,
    CompanionBudgetStatusContext? Budget,
    CompanionProfileBaseline Baseline);

public interface IFinancialAdviceFindingEvaluator
{
    void Evaluate(
        FinancialAdviceEvaluationContext context,
        FinancialAdviceEvaluationSession session);
}

public sealed class FinancialAdviceEvaluationSession
{
    private int idSequence;

    public List<FinancialAdviceFinding> Findings { get; } = [];

    public string NextFindingId(FinancialAdviceFindingType findingType)
    {
        idSequence += 1;
        return $"{findingType.ToString().ToLowerInvariant()}_{idSequence}";
    }
}

public sealed record CompanionProfileBaseline(
    IReadOnlyDictionary<int, decimal> BaselineSpendByDomain,
    decimal? BaselineAverageDailySpend,
    decimal BaselineRecurringMonthlyTotal,
    decimal ActivePlanExpectedSpendTotal,
    int ActivePlanCount,
    IReadOnlyList<string> ProtectedPreferenceHints);

public sealed record FinancialAdviceFindingBuildRequest(
    string FindingId,
    FinancialAdviceFindingType FindingType,
    FinancialCompanionIntent RelatedIntent,
    FinancialAdviceSeverity Severity,
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
    bool AiAdjudicationAllowed,
    bool AiAdjudicationRecommended,
    DateTime ComputedAtUtc,
    string RenderingFamily);
