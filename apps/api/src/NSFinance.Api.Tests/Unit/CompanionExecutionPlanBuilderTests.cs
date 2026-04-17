using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionExecutionPlanBuilderTests
{
    [Fact]
    public void Build_BudgetStatusPlan_ContainsExpectedRequiredAndOptional()
    {
        var sut = CreateSut(new CompanionOrchestrationOptions());
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.BudgetStatus,
            PrimaryIntent: FinancialCompanionIntent.BudgetStatus,
            SecondaryIntents: [],
            Confidence: 0.9d,
            ReasonCodes: ["signal_budget_phrase"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var plan = sut.Build(routing);

        Assert.Contains(plan.PlannedTools, item => item.Tool == CompanionTool.FinancialSummary && item.IsRequired);
        Assert.Contains(plan.PlannedTools, item => item.Tool == CompanionTool.BudgetStatus && item.IsRequired);
        Assert.Contains(plan.PlannedTools, item => item.Tool == CompanionTool.SpendingAnalysis && !item.IsRequired);
    }

    [Fact]
    public void Build_MixedPlan_IsDeterministicAndDeduped()
    {
        var sut = CreateSut(new CompanionOrchestrationOptions());
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.MixedQuery,
            PrimaryIntent: FinancialCompanionIntent.Affordability,
            SecondaryIntents: [FinancialCompanionIntent.LocalPlacesOutings, FinancialCompanionIntent.BudgetStatus],
            Confidence: 0.72d,
            ReasonCodes: ["mixed_query_detected"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var first = sut.Build(routing);
        var second = sut.Build(routing);

        Assert.Equal(
            first.PlannedTools.Select(x => $"{x.Tool}:{x.IsRequired}:{x.Order}").ToArray(),
            second.PlannedTools.Select(x => $"{x.Tool}:{x.IsRequired}:{x.Order}").ToArray());
        Assert.Equal(first.SkippedTools.Select(x => $"{x.Tool}:{x.ReasonCode}").ToArray(), second.SkippedTools.Select(x => $"{x.Tool}:{x.ReasonCode}").ToArray());
        Assert.Equal(first.PlannedTools.Count, first.PlannedTools.Select(x => x.Tool).Distinct().Count());
    }

    [Fact]
    public void Build_WhenOptionalBudgetExceeded_EmitsSkipReasons()
    {
        var sut = CreateSut(new CompanionOrchestrationOptions
        {
            MaxToolCallsPerRequest = 3,
            MaxSecondaryOptionalTools = 4
        });
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.MixedQuery,
            PrimaryIntent: FinancialCompanionIntent.Affordability,
            SecondaryIntents: [FinancialCompanionIntent.LocalPlacesOutings],
            Confidence: 0.7d,
            ReasonCodes: ["mixed_query_detected"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var plan = sut.Build(routing);

        Assert.True(plan.PlannedTools.Count <= 3);
        Assert.Contains(plan.SkippedTools, skipped => skipped.ReasonCode.Contains("cap_exceeded_or_skipped:plan_optional_budget", StringComparison.Ordinal));
    }

    private static CompanionExecutionPlanBuilder CreateSut(CompanionOrchestrationOptions options)
    {
        var policyProvider = new CompanionIntentToolPolicyProvider();
        var mergePolicy = new CompanionMixedIntentMergePolicy(policyProvider, Options.Create(options));
        return new CompanionExecutionPlanBuilder(policyProvider, mergePolicy, Options.Create(options));
    }
}
