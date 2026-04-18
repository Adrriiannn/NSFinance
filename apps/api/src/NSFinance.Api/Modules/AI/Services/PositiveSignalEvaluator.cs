namespace NSFinance.Api.Modules.AI.Services;

public sealed class PositiveSignalEvaluator(
    IFinancialAdviceFindingFactory findingFactory) : IFinancialAdviceFindingEvaluator
{
    public void Evaluate(
        FinancialAdviceEvaluationContext context,
        FinancialAdviceEvaluationSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        if (context.Summary is null
            || context.Budget is null
            || !context.Budget.HasBudgetPlan
            || session.Findings.Any(finding => finding.Severity >= FinancialAdviceSeverity.Moderate))
        {
            return;
        }

        if ((context.Budget.RemainingBudget ?? 0m) <= 0m || context.Summary.NetLast30Days <= 0m)
        {
            return;
        }

        session.Findings.Add(
            findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: session.NextFindingId(FinancialAdviceFindingType.PositiveProgress),
                    FindingType: FinancialAdviceFindingType.PositiveProgress,
                    RelatedIntent: context.Routing.PrimaryIntent,
                    Severity: FinancialAdviceSeverity.Info,
                    Confidence: 0.70d,
                    EvidenceSummary: "Current signals show a positive direction: budget remains positive and net cashflow is above zero.",
                    SupportingMetrics:
                    [
                        new FinancialAdviceEvidenceMetric("remainingBudget", context.Budget.RemainingBudget ?? 0m, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("netLast30Days", context.Summary.NetLast30Days, context.Summary.Currency)
                    ],
                    DomainCode: null,
                    DomainName: null,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: [],
                    RecommendedActions:
                    [
                        new FinancialAdviceActionCandidate(
                            ActionId: "continue_current_trajectory",
                            ActionType: FinancialAdviceActionType.KeepCourse,
                            Title: "Continue current trajectory",
                            Guidance: "Maintain current spending patterns and run a weekly variance check.")
                    ],
                    UncertaintyMarkers: [],
                    AiAdjudicationAllowed: false,
                    AiAdjudicationRecommended: false,
                    ComputedAtUtc: context.NowUtc,
                    RenderingFamily: "positive_progress")));
    }
}
