using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionIntentRouterTests
{
    private readonly CompanionIntentRouter _router = new(NullLogger<CompanionIntentRouter>.Instance);

    [Theory]
    [InlineData("What am I spending the most on this month?", FinancialCompanionIntent.SpendingAnalysis)]
    [InlineData("Where is most of my money going lately?", FinancialCompanionIntent.SpendingAnalysis)]
    [InlineData("How can I save more each month?", FinancialCompanionIntent.SavingsCutbackAdvice)]
    [InlineData("Where should I cut back first?", FinancialCompanionIntent.SavingsCutbackAdvice)]
    [InlineData("Can I afford a new laptop this month?", FinancialCompanionIntent.Affordability)]
    [InlineData("Would it be okay to buy this and still be okay this month?", FinancialCompanionIntent.Affordability)]
    [InlineData("How much budget do I have left this month?", FinancialCompanionIntent.BudgetStatus)]
    [InlineData("Am I over budget right now?", FinancialCompanionIntent.BudgetStatus)]
    [InlineData("Am I on track with my savings goal?", FinancialCompanionIntent.PlanProgress)]
    [InlineData("How far along am I with my plan?", FinancialCompanionIntent.PlanProgress)]
    [InlineData("Where can I go nearby for dinner tonight?", FinancialCompanionIntent.LocalPlacesOutings)]
    [InlineData("Suggest a restaurant near me within my budget.", FinancialCompanionIntent.LocalPlacesOutings)]
    [InlineData("How am I doing financially lately?", FinancialCompanionIntent.GeneralFinancialQuestion)]
    [InlineData("What's the smartest next step for my finances?", FinancialCompanionIntent.GeneralFinancialQuestion)]
    public void Route_SingleIntentQueries_ClassifiesExpectedIntent(string query, FinancialCompanionIntent expectedIntent)
    {
        var result = _router.Route(query);

        Assert.Equal(expectedIntent, result.IntentFamily);
        Assert.Equal(expectedIntent, result.PrimaryIntent);
        Assert.False(result.IsAmbiguous);
        Assert.False(result.IsUnsupported);
        Assert.Empty(result.SecondaryIntents);
        Assert.True(result.Confidence >= 0.45d);
        Assert.NotEmpty(result.ReasonCodes);
    }

    [Fact]
    public void Route_MixedQuery_AffordabilityAndLocalPlaces_DetectsMixedIntent()
    {
        var query = "Can I afford to go out this weekend and where should I go nearby?";
        var result = _router.Route(query);

        Assert.Equal(FinancialCompanionIntent.MixedQuery, result.IntentFamily);
        Assert.False(result.IsAmbiguous);
        Assert.False(result.IsUnsupported);
        Assert.NotEmpty(result.SecondaryIntents);
        Assert.Contains(FinancialCompanionIntent.Affordability, new[] { result.PrimaryIntent }.Concat(result.SecondaryIntents));
        Assert.Contains(FinancialCompanionIntent.LocalPlacesOutings, new[] { result.PrimaryIntent }.Concat(result.SecondaryIntents));
        Assert.Contains("mixed_query_detected", result.ReasonCodes);
    }

    [Fact]
    public void Route_MixedQuery_BudgetAndCutback_DetectsMixedIntent()
    {
        var query = "Am I over budget and how much budget do I have left, and where can I cut back?";
        var result = _router.Route(query);

        Assert.Equal(FinancialCompanionIntent.MixedQuery, result.IntentFamily);
        Assert.Contains(FinancialCompanionIntent.BudgetStatus, new[] { result.PrimaryIntent }.Concat(result.SecondaryIntents));
        Assert.Contains(FinancialCompanionIntent.SavingsCutbackAdvice, new[] { result.PrimaryIntent }.Concat(result.SecondaryIntents));
        Assert.True(result.Confidence >= 0.45d);
    }

    [Fact]
    public void Route_MixedQuery_SpendingAndCutback_DetectsMixedIntent()
    {
        var query = "Am I overspending on food and what should I reduce first?";
        var result = _router.Route(query);

        Assert.Equal(FinancialCompanionIntent.MixedQuery, result.IntentFamily);
        Assert.Equal(FinancialCompanionIntent.SpendingAnalysis, result.PrimaryIntent);
        Assert.Contains(FinancialCompanionIntent.SavingsCutbackAdvice, result.SecondaryIntents);
    }

    [Theory]
    [InlineData("What should I do?")]
    [InlineData("Help me with my money.")]
    [InlineData("How am I doing?")]
    public void Route_AmbiguousPrompts_ReturnsAmbiguous(string query)
    {
        var result = _router.Route(query);

        Assert.Equal(FinancialCompanionIntent.Ambiguous, result.IntentFamily);
        Assert.True(result.IsAmbiguous);
        Assert.False(result.IsUnsupported);
        Assert.Equal(FinancialCompanionIntent.Ambiguous, result.PrimaryIntent);
        Assert.Empty(result.SecondaryIntents);
    }

    [Theory]
    [InlineData("Write me a Python function for quicksort.")]
    [InlineData("What's the weather in London tomorrow?")]
    [InlineData("Translate this paragraph into German.")]
    public void Route_UnsupportedPrompts_ReturnsUnsupported(string query)
    {
        var result = _router.Route(query);

        Assert.Equal(FinancialCompanionIntent.Unsupported, result.IntentFamily);
        Assert.True(result.IsUnsupported);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(FinancialCompanionIntent.Unsupported, result.PrimaryIntent);
        Assert.Empty(result.SecondaryIntents);
    }

    [Fact]
    public void Route_NearCollision_SpendingVsSavings_IsSeparated()
    {
        var spending = _router.Route("What am I spending the most on?");
        var savings = _router.Route("Where should I cut back?");

        Assert.Equal(FinancialCompanionIntent.SpendingAnalysis, spending.IntentFamily);
        Assert.Equal(FinancialCompanionIntent.SavingsCutbackAdvice, savings.IntentFamily);
    }

    [Fact]
    public void Route_NearCollision_AffordabilityVsBudgetStatus_IsSeparated()
    {
        var affordability = _router.Route("Can I afford a new phone this month?");
        var budget = _router.Route("How much budget do I have left this month?");

        Assert.Equal(FinancialCompanionIntent.Affordability, affordability.IntentFamily);
        Assert.Equal(FinancialCompanionIntent.BudgetStatus, budget.IntentFamily);
    }

    [Fact]
    public void Route_NearCollision_AffordabilityVsLocalPlaces_IsSeparated()
    {
        var affordability = _router.Route("Can I afford to go out tonight?");
        var localPlaces = _router.Route("Where can I go nearby tonight?");

        Assert.Equal(FinancialCompanionIntent.Affordability, affordability.IntentFamily);
        Assert.Equal(FinancialCompanionIntent.LocalPlacesOutings, localPlaces.IntentFamily);
    }

    [Fact]
    public void Route_NearCollision_GeneralVsAmbiguous_IsSeparated()
    {
        var general = _router.Route("What's the smartest next step for my finances?");
        var ambiguous = _router.Route("Help me with money");

        Assert.Equal(FinancialCompanionIntent.GeneralFinancialQuestion, general.IntentFamily);
        Assert.Equal(FinancialCompanionIntent.Ambiguous, ambiguous.IntentFamily);
    }

    [Fact]
    public void Route_Determinism_SameInputProducesSameOutput()
    {
        var query = "Can I afford to go out this weekend and where should I go nearby?";
        var baseline = _router.Route(query);

        for (var i = 0; i < 10; i++)
        {
            var repeat = _router.Route(query);
            Assert.Equal(baseline.IntentFamily, repeat.IntentFamily);
            Assert.Equal(baseline.PrimaryIntent, repeat.PrimaryIntent);
            Assert.Equal(baseline.Confidence, repeat.Confidence);
            Assert.Equal(baseline.IsAmbiguous, repeat.IsAmbiguous);
            Assert.Equal(baseline.IsUnsupported, repeat.IsUnsupported);
            Assert.True(baseline.SecondaryIntents.SequenceEqual(repeat.SecondaryIntents));
            Assert.True(baseline.ReasonCodes.SequenceEqual(repeat.ReasonCodes));
        }
    }
}
