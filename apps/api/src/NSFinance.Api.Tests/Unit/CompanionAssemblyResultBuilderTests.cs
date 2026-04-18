using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionAssemblyResultBuilderTests
{
    [Fact]
    public void Build_ComposesWarningsEvidenceAndContextDeterministically()
    {
        var sut = new CompanionAssemblyResultBuilder(new CompanionEvidenceBuilder());
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
        var plan = new CompanionExecutionPlan(
            PlannedTools:
            [
                new CompanionPlannedTool(CompanionTool.FinancialSummary, true, 10, "primary_required", [FinancialCompanionIntent.BudgetStatus]),
                new CompanionPlannedTool(CompanionTool.BudgetStatus, true, 20, "primary_required", [FinancialCompanionIntent.BudgetStatus]),
                new CompanionPlannedTool(CompanionTool.SpendingAnalysis, false, 30, "primary_optional", [FinancialCompanionIntent.BudgetStatus])
            ],
            SkippedTools:
            [
                new CompanionSkippedToolDecision(CompanionTool.TransactionQuery, "cap_exceeded_or_skipped:plan_optional_budget", [FinancialCompanionIntent.BudgetStatus])
            ],
            Warnings: ["plan_warning"]);
        var execution = new CompanionToolExecutionResult(
            ContextOutputs: new Dictionary<string, object?>
            {
                [CompanionTool.FinancialSummary.ToOutputKey()] = new CompanionFinancialSummaryContext(2500m, 1800m, 700m, "EUR"),
                [CompanionTool.BudgetStatus.ToOutputKey()] = new CompanionBudgetStatusContext(true, 1800m, 1200m, 600m)
            },
            Records:
            [
                new CompanionToolExecutionRecord(plan.PlannedTools[0], CompanionToolExecutionStatus.Success, CompanionTool.FinancialSummary.ToContractName(), CompanionTool.FinancialSummary.ToOutputKey(), new object(), null, [], true),
                new CompanionToolExecutionRecord(plan.PlannedTools[1], CompanionToolExecutionStatus.Success, CompanionTool.BudgetStatus.ToContractName(), CompanionTool.BudgetStatus.ToOutputKey(), new object(), null, [], true),
                new CompanionToolExecutionRecord(plan.PlannedTools[2], CompanionToolExecutionStatus.NoData, CompanionTool.SpendingAnalysis.ToContractName(), CompanionTool.SpendingAnalysis.ToOutputKey(), null, "tool_returned_no_data:spending_analysis", ["tool_returned_no_data:spending_analysis"], false)
            ],
            Warnings: ["exec_warning"]);
        var trim = new CompanionContextTrimResult(
            Outputs: execution.ContextOutputs,
            TrimmedIndicators: ["payload_trimmed:transaction_matches_rows"],
            Warnings: ["trim_warning"],
            AdjustedRecords: execution.Records);
        var insufficiency = new CompanionInsufficiencyDecision(
            CanProceedToAI: true,
            HasInsufficientData: false,
            Reasons: [],
            MissingRequiredTools: [],
            Warnings: ["insuff_warning"]);

        var result = sut.Build(
            primaryIntent: FinancialCompanionIntent.BudgetStatus,
            profile: profile,
            plan: plan,
            execution: execution,
            trim: trim,
            insufficiency: insufficiency);

        Assert.Equal(FinancialCompanionIntent.BudgetStatus, result.Context.Intent);
        Assert.Contains("plan_warning", result.Warnings);
        Assert.Contains("exec_warning", result.Warnings);
        Assert.Contains("trim_warning", result.Warnings);
        Assert.Contains("insuff_warning", result.Warnings);
        Assert.NotNull(result.Evidence);
        Assert.Contains("based_on_budget_status", result.Evidence.BasisSummary);
        Assert.Contains(CompanionTool.TransactionQuery.ToContractName() + ":cap_exceeded_or_skipped:plan_optional_budget", result.Evidence.SkippedTools);
        Assert.Contains("payload_trimmed:transaction_matches_rows", result.Evidence.TrimmedPayloadIndicators ?? []);
    }
}
