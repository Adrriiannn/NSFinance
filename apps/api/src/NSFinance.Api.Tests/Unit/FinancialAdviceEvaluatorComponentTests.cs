using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialAdviceEvaluatorComponentTests
{
    private readonly CompanionAdviceOptions options = new();
    private readonly FinancialAdviceFindingFactory findingFactory;
    private readonly FinancialAdviceCategoryClassifier categoryClassifier;
    private readonly NSFinance.Api.Modules.ExpenseTracker.Services.ExpenseTaxonomyService taxonomyService = new();

    public FinancialAdviceEvaluatorComponentTests()
    {
        var freshness = new InsightFreshnessEvaluator(
            Options.Create(options),
            new InsightInvalidationHintBuilder());
        findingFactory = new FinancialAdviceFindingFactory(freshness);
        categoryClassifier = new FinancialAdviceCategoryClassifier(taxonomyService);
    }

    [Fact]
    public void CategoryPressureEvaluator_ProducesCategoryPressureFinding()
    {
        var evaluator = new CategoryPressureEvaluator(
            taxonomyService,
            categoryClassifier,
            findingFactory,
            Options.Create(options));
        var session = new FinancialAdviceEvaluationSession();
        var context = CreateContext(
            summary: new CompanionFinancialSummaryContext(3000m, 1500m, 1500m, "EUR"),
            spending: new CompanionSpendingAnalysisContext(
                [new CompanionDomainSpendContextItem(130, 520m)],
                DomainCount: 1,
                AverageDailySpend: 22m,
                LargestExpense: 130m),
            baseline: new CompanionProfileBaseline(
                BaselineSpendByDomain: new Dictionary<int, decimal> { [130] = 200m },
                BaselineAverageDailySpend: 20m,
                BaselineRecurringMonthlyTotal: 0m,
                ActivePlanExpectedSpendTotal: 0m,
                ActivePlanCount: 0,
                ProtectedPreferenceHints: []));

        evaluator.Evaluate(context, session);

        Assert.Contains(session.Findings, finding =>
            finding.FindingType is FinancialAdviceFindingType.CategoryPressure
                or FinancialAdviceFindingType.DiscretionaryOverspend);
    }

    [Fact]
    public void RecurringSpendEvaluator_ProducesRecurringPressureFinding()
    {
        var evaluator = new RecurringSpendEvaluator(
            categoryClassifier,
            findingFactory,
            Options.Create(options));
        var session = new FinancialAdviceEvaluationSession();
        var context = CreateContext(
            summary: new CompanionFinancialSummaryContext(1800m, 900m, 900m, "EUR"),
            recurring: new CompanionRecurringObligationsContext(
                TotalItemCount: 2,
                EstimatedMonthlyTotal: 900m,
                TopItems:
                [
                    new CompanionRecurringItemContext("Rent", 780m, "EUR", 30),
                    new CompanionRecurringItemContext("Gym", 120m, "EUR", 30)
                ]),
            baseline: new CompanionProfileBaseline(
                BaselineSpendByDomain: new Dictionary<int, decimal>(),
                BaselineAverageDailySpend: 20m,
                BaselineRecurringMonthlyTotal: 500m,
                ActivePlanExpectedSpendTotal: 0m,
                ActivePlanCount: 0,
                ProtectedPreferenceHints: []));

        evaluator.Evaluate(context, session);

        Assert.Contains(session.Findings, finding => finding.FindingType == FinancialAdviceFindingType.RecurringSpendPressure);
    }

    [Fact]
    public void BudgetHealthEvaluator_ProducesBudgetSlippageFinding()
    {
        var evaluator = new BudgetHealthEvaluator(findingFactory, Options.Create(options));
        var session = new FinancialAdviceEvaluationSession();
        var context = CreateContext(
            budget: new CompanionBudgetStatusContext(
                HasBudgetPlan: true,
                MonthlyBudget: 1200m,
                MonthToDateSpend: 1450m,
                RemainingBudget: -250m));

        evaluator.Evaluate(context, session);

        Assert.Contains(session.Findings, finding =>
            finding.FindingType == FinancialAdviceFindingType.BudgetSlippage
            && finding.Severity == FinancialAdviceSeverity.High);
    }

    [Fact]
    public void AffordabilityEvaluator_ProducesAffordabilityRiskFinding()
    {
        var evaluator = new AffordabilityEvaluator(findingFactory, Options.Create(options));
        var session = new FinancialAdviceEvaluationSession();
        var context = CreateContext(
            summary: new CompanionFinancialSummaryContext(1600m, 2200m, -600m, "EUR"),
            recurring: new CompanionRecurringObligationsContext(
                TotalItemCount: 1,
                EstimatedMonthlyTotal: 600m,
                TopItems: [new CompanionRecurringItemContext("Rent", 600m, "EUR", 30)]));

        evaluator.Evaluate(context, session);

        Assert.Contains(session.Findings, finding => finding.FindingType == FinancialAdviceFindingType.AffordabilityRisk);
    }

    [Fact]
    public void PlanDriftEvaluator_ProducesPlanDriftFinding()
    {
        var evaluator = new PlanDriftEvaluator(findingFactory);
        var session = new FinancialAdviceEvaluationSession();
        var context = CreateContext(
            summary: new CompanionFinancialSummaryContext(2600m, 1300m, 1300m, "EUR"),
            baseline: new CompanionProfileBaseline(
                BaselineSpendByDomain: new Dictionary<int, decimal>(),
                BaselineAverageDailySpend: 20m,
                BaselineRecurringMonthlyTotal: 0m,
                ActivePlanExpectedSpendTotal: 900m,
                ActivePlanCount: 1,
                ProtectedPreferenceHints: []));

        evaluator.Evaluate(context, session);

        Assert.Contains(session.Findings, finding => finding.FindingType == FinancialAdviceFindingType.PlanDrift);
    }

    [Fact]
    public void PositiveSignalEvaluator_ProducesPositiveProgressOnlyWhenNoHigherSeveritySignalsExist()
    {
        var evaluator = new PositiveSignalEvaluator(findingFactory);
        var cleanSession = new FinancialAdviceEvaluationSession();
        var context = CreateContext(
            summary: new CompanionFinancialSummaryContext(2500m, 1700m, 800m, "EUR"),
            budget: new CompanionBudgetStatusContext(
                HasBudgetPlan: true,
                MonthlyBudget: 1800m,
                MonthToDateSpend: 1200m,
                RemainingBudget: 600m));

        evaluator.Evaluate(context, cleanSession);

        Assert.Contains(cleanSession.Findings, finding => finding.FindingType == FinancialAdviceFindingType.PositiveProgress);

        var blockedSession = new FinancialAdviceEvaluationSession();
        blockedSession.Findings.Add(
            findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: "existing_high",
                    FindingType: FinancialAdviceFindingType.AffordabilityRisk,
                    RelatedIntent: FinancialCompanionIntent.Affordability,
                    Severity: FinancialAdviceSeverity.High,
                    Confidence: 0.9d,
                    EvidenceSummary: "Existing high-severity signal.",
                    SupportingMetrics: [],
                    DomainCode: null,
                    DomainName: null,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: [],
                    RecommendedActions: [],
                    UncertaintyMarkers: [],
                    AiAdjudicationAllowed: false,
                    AiAdjudicationRecommended: false,
                    ComputedAtUtc: DateTime.UtcNow,
                    RenderingFamily: "test")));

        evaluator.Evaluate(context, blockedSession);
        Assert.Single(blockedSession.Findings);
    }

    private static FinancialAdviceEvaluationContext CreateContext(
        CompanionFinancialSummaryContext? summary = null,
        CompanionSpendingAnalysisContext? spending = null,
        CompanionRecurringObligationsContext? recurring = null,
        CompanionBudgetStatusContext? budget = null,
        CompanionProfileBaseline? baseline = null)
    {
        var request = new FinancialCompanionRequest(Guid.NewGuid(), "session", "help");
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.GeneralFinancialQuestion,
            PrimaryIntent: FinancialCompanionIntent.GeneralFinancialQuestion,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: [],
            IsAmbiguous: false,
            IsUnsupported: false);
        var profile = new UserFinancialContextSnapshot(
            Country: "IE",
            Currency: "EUR",
            MonthlyIncomeRange: "2000-4000",
            KnownObligationsJson: "[]",
            BudgetStructureJson: "{}",
            ActivePlansJson: "[]",
            SpendingTendenciesJson: "[]",
            CategoryFlexibilityMarkersJson: "[]",
            AdviceStylePreference: "balanced");
        var context = new FinancialCompanionContext(
            Intent: FinancialCompanionIntent.GeneralFinancialQuestion,
            Profile: profile,
            ToolOutputs: new Dictionary<string, object?>(),
            ToolsUsed: [],
            Evidence: null);

        return new FinancialAdviceEvaluationContext(
            Request: request,
            Routing: routing,
            Context: context,
            NowUtc: DateTime.UtcNow,
            Summary: summary,
            Spending: spending,
            Recurring: recurring,
            Budget: budget,
            Baseline: baseline
                      ?? new CompanionProfileBaseline(
                          BaselineSpendByDomain: new Dictionary<int, decimal>(),
                          BaselineAverageDailySpend: null,
                          BaselineRecurringMonthlyTotal: 0m,
                          ActivePlanExpectedSpendTotal: 0m,
                          ActivePlanCount: 0,
                          ProtectedPreferenceHints: []));
    }
}
