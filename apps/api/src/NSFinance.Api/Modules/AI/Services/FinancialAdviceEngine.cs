namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceEngine
{
    IReadOnlyList<FinancialAdviceFinding> ComputeDeterministicFindings(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        DateTime nowUtc);
}

public sealed class FinancialAdviceEngine(
    ICompanionProfileBaselineBuilder baselineBuilder,
    IFinancialAdviceFindingFactory findingFactory,
    CategoryPressureEvaluator categoryPressureEvaluator,
    RecurringSpendEvaluator recurringSpendEvaluator,
    BudgetHealthEvaluator budgetHealthEvaluator,
    AffordabilityEvaluator affordabilityEvaluator,
    PlanDriftEvaluator planDriftEvaluator,
    PositiveSignalEvaluator positiveSignalEvaluator) : IFinancialAdviceEngine
{
    private readonly IFinancialAdviceFindingEvaluator[] evaluators =
    [
        categoryPressureEvaluator,
        recurringSpendEvaluator,
        budgetHealthEvaluator,
        affordabilityEvaluator,
        planDriftEvaluator,
        positiveSignalEvaluator
    ];

    public IReadOnlyList<FinancialAdviceFinding> ComputeDeterministicFindings(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(context);

        var summary = FinancialAdviceContextAccessor.TryGetContext<CompanionFinancialSummaryContext>(
            context.ToolOutputs,
            CompanionTool.FinancialSummary);
        var spending = FinancialAdviceContextAccessor.TryGetContext<CompanionSpendingAnalysisContext>(
            context.ToolOutputs,
            CompanionTool.SpendingAnalysis);
        var recurring = FinancialAdviceContextAccessor.TryGetContext<CompanionRecurringObligationsContext>(
            context.ToolOutputs,
            CompanionTool.RecurringObligations);
        var budget = FinancialAdviceContextAccessor.TryGetContext<CompanionBudgetStatusContext>(
            context.ToolOutputs,
            CompanionTool.BudgetStatus);

        var evaluationContext = new FinancialAdviceEvaluationContext(
            Request: request,
            Routing: routing,
            Context: context,
            NowUtc: nowUtc,
            Summary: summary,
            Spending: spending,
            Recurring: recurring,
            Budget: budget,
            Baseline: baselineBuilder.Build(context.Profile));
        var session = new FinancialAdviceEvaluationSession();

        foreach (var evaluator in evaluators)
        {
            evaluator.Evaluate(evaluationContext, session);
        }

        if (session.Findings.Count == 0)
        {
            AddFallbackFinding(evaluationContext, session);
        }

        return session.Findings
            .OrderByDescending(finding => finding.PriorityScore)
            .ThenByDescending(finding => finding.Confidence)
            .ToArray();
    }

    private void AddFallbackFinding(
        FinancialAdviceEvaluationContext context,
        FinancialAdviceEvaluationSession session)
    {
        if (context.Summary is null
            && context.Spending is null
            && context.Recurring is null
            && context.Budget is null)
        {
            session.Findings.Add(
                findingFactory.Create(
                    new FinancialAdviceFindingBuildRequest(
                        FindingId: session.NextFindingId(FinancialAdviceFindingType.InsufficientEvidence),
                        FindingType: FinancialAdviceFindingType.InsufficientEvidence,
                        RelatedIntent: context.Routing.PrimaryIntent,
                        Severity: FinancialAdviceSeverity.Info,
                        Confidence: 0.35d,
                        EvidenceSummary: "Not enough grounded financial inputs were available to compute reliable guidance.",
                        SupportingMetrics: [],
                        DomainCode: null,
                        DomainName: null,
                        CategoryCode: null,
                        CategoryName: null,
                        ProtectedCategoryFlags: [],
                        RecommendedActions:
                        [
                            new FinancialAdviceActionCandidate(
                                ActionId: "collect_grounding_data",
                                ActionType: FinancialAdviceActionType.ReviewSpend,
                                Title: "Wait for more grounded data",
                                Guidance: "Sync recent transactions and budget data so guidance can be evidence-backed.")
                        ],
                        UncertaintyMarkers: ["missing_core_context_tools"],
                        AiAdjudicationAllowed: false,
                        AiAdjudicationRecommended: false,
                        ComputedAtUtc: context.NowUtc,
                        RenderingFamily: "insufficient_evidence")));
            return;
        }

        session.Findings.Add(
            findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: session.NextFindingId(FinancialAdviceFindingType.NoMaterialIssueDetected),
                    FindingType: FinancialAdviceFindingType.NoMaterialIssueDetected,
                    RelatedIntent: context.Routing.PrimaryIntent,
                    Severity: FinancialAdviceSeverity.Info,
                    Confidence: 0.72d,
                    EvidenceSummary: "No material financial pressure signal was detected from your current baseline and recent activity.",
                    SupportingMetrics: [],
                    DomainCode: null,
                    DomainName: null,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: [],
                    RecommendedActions:
                    [
                        new FinancialAdviceActionCandidate(
                            ActionId: "maintain_course",
                            ActionType: FinancialAdviceActionType.KeepCourse,
                            Title: "Stay on your current path",
                            Guidance: "Keep monitoring weekly to catch changes early.")
                    ],
                    UncertaintyMarkers: [],
                    AiAdjudicationAllowed: false,
                    AiAdjudicationRecommended: false,
                    ComputedAtUtc: context.NowUtc,
                    RenderingFamily: "no_material_issue")));
    }
}
