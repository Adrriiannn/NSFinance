using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionIntentToolPolicyProviderTests
{
    private readonly CompanionIntentToolPolicyProvider _sut = new();

    [Theory]
    [InlineData(FinancialCompanionIntent.SpendingAnalysis, true, true, false)]
    [InlineData(FinancialCompanionIntent.SavingsCutbackAdvice, true, true, false)]
    [InlineData(FinancialCompanionIntent.Affordability, true, true, true)]
    [InlineData(FinancialCompanionIntent.BudgetStatus, true, true, false)]
    [InlineData(FinancialCompanionIntent.PlanProgress, true, true, true)]
    [InlineData(FinancialCompanionIntent.LocalPlacesOutings, true, true, true)]
    [InlineData(FinancialCompanionIntent.GeneralFinancialQuestion, true, true, true)]
    public void Resolve_IntentMappings_AreExplicitAndGrounded(
        FinancialCompanionIntent intent,
        bool expectsSummaryRequired,
        bool expectsOptionals,
        bool expectsBudgetOrContextOptional)
    {
        var policy = _sut.Resolve(intent);

        Assert.Equal(intent, policy.Intent);
        if (expectsSummaryRequired)
        {
            Assert.Contains(CompanionTool.FinancialSummary, policy.RequiredTools);
        }

        if (expectsOptionals)
        {
            Assert.NotNull(policy.OptionalTools);
        }

        if (expectsBudgetOrContextOptional)
        {
            Assert.True(policy.OptionalTools.Count > 0 || policy.RequiredTools.Contains(CompanionTool.BudgetStatus));
        }
    }

    [Theory]
    [InlineData(FinancialCompanionIntent.Ambiguous)]
    [InlineData(FinancialCompanionIntent.Unsupported)]
    public void Resolve_AmbiguousAndUnsupported_DisallowBroadTooling(FinancialCompanionIntent intent)
    {
        var policy = _sut.Resolve(intent);

        Assert.Empty(policy.RequiredTools);
        Assert.Empty(policy.OptionalTools);
        Assert.NotEmpty(policy.DisallowedTools);
    }
}
