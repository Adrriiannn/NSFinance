using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionInsufficiencyEvaluatorTests
{
    private readonly CompanionInsufficiencyEvaluator _sut = new();

    [Fact]
    public void Evaluate_RequiredToolFailure_BlocksAI()
    {
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.BudgetStatus,
            PrimaryIntent: FinancialCompanionIntent.BudgetStatus,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: ["signal_budget_phrase"],
            IsAmbiguous: false,
            IsUnsupported: false);
        var plan = new CompanionExecutionPlan(
            PlannedTools:
            [
                new CompanionPlannedTool(CompanionTool.FinancialSummary, true, 10, "required", [FinancialCompanionIntent.BudgetStatus]),
                new CompanionPlannedTool(CompanionTool.BudgetStatus, true, 20, "required", [FinancialCompanionIntent.BudgetStatus])
            ],
            SkippedTools: [],
            Warnings: []);
        var records = new[]
        {
            new CompanionToolExecutionRecord(
                plan.PlannedTools[0],
                CompanionToolExecutionStatus.Success,
                CompanionTool.FinancialSummary.ToContractName(),
                CompanionTool.FinancialSummary.ToOutputKey(),
                new object(),
                null,
                [],
                true),
            new CompanionToolExecutionRecord(
                plan.PlannedTools[1],
                CompanionToolExecutionStatus.Failed,
                CompanionTool.BudgetStatus.ToContractName(),
                CompanionTool.BudgetStatus.ToOutputKey(),
                null,
                "tool_failed",
                ["tool_failed"],
                false)
        };

        var decision = _sut.Evaluate(routing, plan, records, []);

        Assert.False(decision.CanProceedToAI);
        Assert.True(decision.HasInsufficientData);
        Assert.Contains(decision.MissingRequiredTools, name => name == CompanionTool.BudgetStatus.ToContractName());
        Assert.Contains(decision.Reasons, reason => reason.StartsWith("required_tool_failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_OptionalToolFailure_DoesNotBlockAI()
    {
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.BudgetStatus,
            PrimaryIntent: FinancialCompanionIntent.BudgetStatus,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: ["signal_budget_phrase"],
            IsAmbiguous: false,
            IsUnsupported: false);
        var plan = new CompanionExecutionPlan(
            PlannedTools:
            [
                new CompanionPlannedTool(CompanionTool.FinancialSummary, true, 10, "required", [FinancialCompanionIntent.BudgetStatus]),
                new CompanionPlannedTool(CompanionTool.BudgetStatus, true, 20, "required", [FinancialCompanionIntent.BudgetStatus]),
                new CompanionPlannedTool(CompanionTool.SpendingAnalysis, false, 30, "optional", [FinancialCompanionIntent.BudgetStatus])
            ],
            SkippedTools: [],
            Warnings: []);
        var records = new[]
        {
            new CompanionToolExecutionRecord(plan.PlannedTools[0], CompanionToolExecutionStatus.Success, CompanionTool.FinancialSummary.ToContractName(), CompanionTool.FinancialSummary.ToOutputKey(), new object(), null, [], true),
            new CompanionToolExecutionRecord(plan.PlannedTools[1], CompanionToolExecutionStatus.Success, CompanionTool.BudgetStatus.ToContractName(), CompanionTool.BudgetStatus.ToOutputKey(), new object(), null, [], true),
            new CompanionToolExecutionRecord(plan.PlannedTools[2], CompanionToolExecutionStatus.Failed, CompanionTool.SpendingAnalysis.ToContractName(), CompanionTool.SpendingAnalysis.ToOutputKey(), null, "tool_failed", ["tool_failed"], false)
        };

        var decision = _sut.Evaluate(routing, plan, records, []);

        Assert.True(decision.CanProceedToAI);
        Assert.False(decision.HasInsufficientData);
        Assert.Contains(decision.Warnings, warning => warning.StartsWith("optional_tool_failed", StringComparison.Ordinal));
    }
}
