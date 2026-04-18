using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialAdviceEngineTests
{
    private readonly FinancialAdviceEngine _sut = new(
        new NSFinance.Api.Modules.ExpenseTracker.Services.ExpenseTaxonomyService(),
        Options.Create(new CompanionAdviceOptions()));

    [Fact]
    public void ComputeDeterministicFindings_CategoryPressureAgainstUserBaseline_EmitsPressureFinding()
    {
        var context = CreateContext(
            profile: CreateProfile(spendingTendenciesJson: """{"averageDailySpend":20,"spendByDomain":{"130":200}}"""),
            outputs: new Dictionary<string, object?>
            {
                [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(3000m, 1400m, 1600m, "EUR"),
                [CompanionTool.SpendingAnalysis.ToOutputKey()] = new CompanionSpendingAnalysisContext(
                    [new CompanionDomainSpendContextItem(130, 520m)],
                    DomainCount: 1,
                    AverageDailySpend: 23m,
                    LargestExpense: 130m)
            });

        var findings = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s1", "Where am I overspending?"),
            CreateRouting(FinancialCompanionIntent.SpendingAnalysis),
            context,
            DateTime.UtcNow);

        Assert.Contains(findings, item =>
            item.FindingType is FinancialAdviceFindingType.CategoryPressure or FinancialAdviceFindingType.DiscretionaryOverspend
            && item.DomainCode == 130
            && item.SupportingMetrics.Any(metric => metric.Key == "domainSpendRatio" && metric.Value > 1.25m));
    }

    [Fact]
    public void ComputeDeterministicFindings_RecurringPressure_EmitsRecurringPressureFinding()
    {
        var context = CreateContext(
            profile: CreateProfile(knownObligationsJson: """[{"name":"Rent","amount":250,"frequencyDays":30},{"name":"Gym","amount":150,"frequencyDays":30}]"""),
            outputs: new Dictionary<string, object?>
            {
                [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(2000m, 1300m, 700m, "EUR"),
                [CompanionTool.RecurringObligations.ToOutputKey()] = new CompanionRecurringObligationsContext(
                    TotalItemCount: 3,
                    EstimatedMonthlyTotal: 760m,
                    TopItems:
                    [
                        new CompanionRecurringItemContext("Rent", 520m, "EUR", 30),
                        new CompanionRecurringItemContext("Utilities", 120m, "EUR", 30)
                    ])
            });

        var findings = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s2", "How are my recurring bills?"),
            CreateRouting(FinancialCompanionIntent.SavingsCutbackAdvice),
            context,
            DateTime.UtcNow);

        Assert.Contains(findings, item => item.FindingType == FinancialAdviceFindingType.RecurringSpendPressure);
    }

    [Fact]
    public void ComputeDeterministicFindings_BudgetSlippage_EmitsBudgetFinding()
    {
        var context = CreateContext(
            profile: CreateProfile(),
            outputs: new Dictionary<string, object?>
            {
                [CompanionTool.BudgetStatus.ToOutputKey()] = new CompanionBudgetStatusContext(true, 1200m, 1450m, -250m)
            });

        var findings = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s3", "How is my budget?"),
            CreateRouting(FinancialCompanionIntent.BudgetStatus),
            context,
            DateTime.UtcNow);

        Assert.Contains(findings, item =>
            item.FindingType == FinancialAdviceFindingType.BudgetSlippage
            && item.Severity >= FinancialAdviceSeverity.Moderate);
    }

    [Fact]
    public void ComputeDeterministicFindings_AffordabilityRisk_EmitsAffordabilityFinding()
    {
        var context = CreateContext(
            profile: CreateProfile(),
            outputs: new Dictionary<string, object?>
            {
                [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(1800m, 2350m, -550m, "EUR"),
                [CompanionTool.RecurringObligations.ToOutputKey()] = new CompanionRecurringObligationsContext(
                    TotalItemCount: 2,
                    EstimatedMonthlyTotal: 500m,
                    TopItems: [new CompanionRecurringItemContext("Rent", 500m, "EUR", 30)])
            });

        var findings = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s4", "Can I afford this?"),
            CreateRouting(FinancialCompanionIntent.Affordability),
            context,
            DateTime.UtcNow);

        Assert.Contains(findings, item => item.FindingType == FinancialAdviceFindingType.AffordabilityRisk);
    }

    [Fact]
    public void ComputeDeterministicFindings_PlanDriftAndProgress_AreRepresentedWhenSupportExists()
    {
        var driftContext = CreateContext(
            profile: CreateProfile(activePlansJson: """[{"id":"p1","expectedSpendTotal":900}]"""),
            outputs: new Dictionary<string, object?>
            {
                [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(2600m, 1200m, 1400m, "EUR")
            });
        var driftFindings = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s5", "Am I on plan?"),
            CreateRouting(FinancialCompanionIntent.PlanProgress),
            driftContext with
            {
                ToolOutputs = new Dictionary<string, object?>
                {
                    [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(2600m, 1300m, 1300m, "EUR")
                }
            },
            DateTime.UtcNow);

        Assert.Contains(driftFindings, item => item.FindingType == FinancialAdviceFindingType.PlanDrift);

        var progressFindings = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s6", "Am I on plan?"),
            CreateRouting(FinancialCompanionIntent.PlanProgress),
            CreateContext(
                profile: CreateProfile(activePlansJson: """[{"id":"p1","expectedSpendTotal":1200}]"""),
                outputs: new Dictionary<string, object?>
                {
                    [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(2600m, 800m, 1800m, "EUR")
                }),
            DateTime.UtcNow);

        Assert.Contains(progressFindings, item => item.FindingType == FinancialAdviceFindingType.PositiveProgress);
    }

    [Fact]
    public void ComputeDeterministicFindings_InsufficientAndNoMaterialIssue_OutcomesAppearWhenExpected()
    {
        var insufficient = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s7", "Help"),
            CreateRouting(FinancialCompanionIntent.GeneralFinancialQuestion),
            CreateContext(CreateProfile(), new Dictionary<string, object?>()),
            DateTime.UtcNow);
        Assert.Contains(insufficient, item => item.FindingType == FinancialAdviceFindingType.InsufficientEvidence);

        var noIssue = _sut.ComputeDeterministicFindings(
            new FinancialCompanionRequest(Guid.NewGuid(), "s8", "How am I doing?"),
            CreateRouting(FinancialCompanionIntent.GeneralFinancialQuestion),
            CreateContext(
                CreateProfile(),
                new Dictionary<string, object?>
                {
                    [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(3500m, 2000m, 1500m, "EUR")
                }),
            DateTime.UtcNow);
        Assert.Contains(noIssue, item => item.FindingType == FinancialAdviceFindingType.NoMaterialIssueDetected);
    }

    private static CompanionIntentRoutingResult CreateRouting(FinancialCompanionIntent intent)
    {
        return new CompanionIntentRoutingResult(
            IntentFamily: intent,
            PrimaryIntent: intent,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: [],
            IsAmbiguous: false,
            IsUnsupported: false);
    }

    private static FinancialCompanionContext CreateContext(
        UserFinancialContextSnapshot profile,
        IReadOnlyDictionary<string, object?> outputs)
    {
        return new FinancialCompanionContext(
            Intent: FinancialCompanionIntent.GeneralFinancialQuestion,
            Profile: profile,
            ToolOutputs: outputs,
            ToolsUsed: outputs.Keys.ToArray(),
            Evidence: null);
    }

    private static UserFinancialContextSnapshot CreateProfile(
        string knownObligationsJson = "[]",
        string budgetStructureJson = "{}",
        string activePlansJson = "[]",
        string spendingTendenciesJson = "[]",
        string categoryFlexibilityMarkersJson = "[]")
    {
        return new UserFinancialContextSnapshot(
            Country: "IE",
            Currency: "EUR",
            MonthlyIncomeRange: "2000-4000",
            KnownObligationsJson: knownObligationsJson,
            BudgetStructureJson: budgetStructureJson,
            ActivePlansJson: activePlansJson,
            SpendingTendenciesJson: spendingTendenciesJson,
            CategoryFlexibilityMarkersJson: categoryFlexibilityMarkersJson,
            AdviceStylePreference: "balanced");
    }
}
