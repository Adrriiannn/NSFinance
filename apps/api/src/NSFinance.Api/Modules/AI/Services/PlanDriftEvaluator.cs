namespace NSFinance.Api.Modules.AI.Services;

public sealed class PlanDriftEvaluator(
    IFinancialAdviceFindingFactory findingFactory) : IFinancialAdviceFindingEvaluator
{
    public void Evaluate(
        FinancialAdviceEvaluationContext context,
        FinancialAdviceEvaluationSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        if (context.Summary is null || context.Baseline.ActivePlanExpectedSpendTotal <= 0m)
        {
            return;
        }

        var planSpendTarget = context.Baseline.ActivePlanExpectedSpendTotal;
        var actualSpend = context.Summary.SpendLast30Days;
        var ratio = planSpendTarget > 0m ? actualSpend / planSpendTarget : 0m;
        var hasDrift = ratio > 1.10m;
        var hasProgress = ratio < 0.90m && context.Summary.NetLast30Days > 0m;

        if (hasDrift)
        {
            session.Findings.Add(
                findingFactory.Create(
                    new FinancialAdviceFindingBuildRequest(
                        FindingId: session.NextFindingId(FinancialAdviceFindingType.PlanDrift),
                        FindingType: FinancialAdviceFindingType.PlanDrift,
                        RelatedIntent: context.Routing.PrimaryIntent,
                        Severity: FinancialAdviceSeverity.Moderate,
                        Confidence: 0.76d,
                        EvidenceSummary: $"Recent spending is {FinancialAdviceFormatting.FormatRatio(ratio)} versus active plan targets, indicating drift.",
                        SupportingMetrics:
                        [
                            new FinancialAdviceEvidenceMetric("activePlanExpectedSpend", planSpendTarget, context.Summary.Currency),
                            new FinancialAdviceEvidenceMetric("actualSpendLast30Days", actualSpend, context.Summary.Currency),
                            new FinancialAdviceEvidenceMetric("planSpendRatio", ratio, "ratio"),
                            new FinancialAdviceEvidenceMetric("activePlanCount", context.Baseline.ActivePlanCount, "count")
                        ],
                        DomainCode: null,
                        DomainName: null,
                        CategoryCode: null,
                        CategoryName: null,
                        ProtectedCategoryFlags: [],
                        RecommendedActions:
                        [
                            new FinancialAdviceActionCandidate(
                                ActionId: "review_active_plan_targets",
                                ActionType: FinancialAdviceActionType.ReviewPlan,
                                Title: "Review active plan targets",
                                Guidance: "Review target assumptions and adjust plan lines that no longer reflect current spending.")
                        ],
                        UncertaintyMarkers: [],
                        AiAdjudicationAllowed: true,
                        AiAdjudicationRecommended: true,
                        ComputedAtUtc: context.NowUtc,
                        RenderingFamily: "plan_drift")));
            return;
        }

        if (!hasProgress)
        {
            return;
        }

        session.Findings.Add(
            findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: session.NextFindingId(FinancialAdviceFindingType.PositiveProgress),
                    FindingType: FinancialAdviceFindingType.PositiveProgress,
                    RelatedIntent: context.Routing.PrimaryIntent,
                    Severity: FinancialAdviceSeverity.Low,
                    Confidence: 0.74d,
                    EvidenceSummary: "Spending is currently below active plan targets while net cashflow remains positive.",
                    SupportingMetrics:
                    [
                        new FinancialAdviceEvidenceMetric("activePlanExpectedSpend", planSpendTarget, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("actualSpendLast30Days", actualSpend, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("planSpendRatio", ratio, "ratio"),
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
                            ActionId: "lock_in_plan_progress",
                            ActionType: FinancialAdviceActionType.KeepCourse,
                            Title: "Lock in recent progress",
                            Guidance: "Keep the current plan discipline and continue weekly checks to sustain progress.")
                    ],
                    UncertaintyMarkers: [],
                    AiAdjudicationAllowed: true,
                    AiAdjudicationRecommended: false,
                    ComputedAtUtc: context.NowUtc,
                    RenderingFamily: "plan_progress")));
    }
}
