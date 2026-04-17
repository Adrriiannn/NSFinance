using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionIntentRouterTests
{
    private readonly CompanionIntentRouter _router = new(NullLogger<CompanionIntentRouter>.Instance);

    public static TheoryData<string, FinancialCompanionIntent> SingleIntentEvalFixtures => new()
    {
        // SpendingAnalysis: direct + paraphrase + conversational
        { "What am I spending the most on this month?", FinancialCompanionIntent.SpendingAnalysis },
        { "Where is most of my money going lately?", FinancialCompanionIntent.SpendingAnalysis },
        { "Can you break down which categories are draining my budget?", FinancialCompanionIntent.SpendingAnalysis },
        // SavingsCutbackAdvice: direct + colloquial + indirect
        { "Where should I cut back first?", FinancialCompanionIntent.SavingsCutbackAdvice },
        { "I need to save more, what should I spend less on?", FinancialCompanionIntent.SavingsCutbackAdvice },
        { "How can I lower expenses without wrecking my routine?", FinancialCompanionIntent.SavingsCutbackAdvice },
        // Affordability: direct + conversational
        { "Can I afford a new laptop this month?", FinancialCompanionIntent.Affordability },
        { "Would it be okay to buy this and still be okay this month?", FinancialCompanionIntent.Affordability },
        { "Can I afford to go out tonight?", FinancialCompanionIntent.Affordability },
        // BudgetStatus: direct + paraphrase
        { "How much budget do I have left this month?", FinancialCompanionIntent.BudgetStatus },
        { "Am I over budget right now?", FinancialCompanionIntent.BudgetStatus },
        { "Help me fix my monthly budget overspend.", FinancialCompanionIntent.BudgetStatus },
        // PlanProgress: direct + conversational
        { "Am I on track with my savings goal?", FinancialCompanionIntent.PlanProgress },
        { "How far along am I with my plan?", FinancialCompanionIntent.PlanProgress },
        { "Is my target still on track or falling behind?", FinancialCompanionIntent.PlanProgress },
        // LocalPlacesOutings: direct + budget-aware phrasing
        { "Where can I go nearby for dinner tonight?", FinancialCompanionIntent.LocalPlacesOutings },
        { "Suggest a restaurant near me within my budget.", FinancialCompanionIntent.LocalPlacesOutings },
        { "Any places around me for a cheap night out?", FinancialCompanionIntent.LocalPlacesOutings },
        // GeneralFinancialQuestion: broad but finance-grounded
        { "How am I doing financially lately?", FinancialCompanionIntent.GeneralFinancialQuestion },
        { "What's the smartest next step for my finances?", FinancialCompanionIntent.GeneralFinancialQuestion },
        { "What should I focus on first in my finances right now?", FinancialCompanionIntent.GeneralFinancialQuestion }
    };

    [Theory]
    [MemberData(nameof(SingleIntentEvalFixtures))]
    public void Route_SingleIntentEvalFixtures_ClassifiesExpectedIntent(string query, FinancialCompanionIntent expectedIntent)
    {
        var result = _router.Route(query);

        Assert.Equal(expectedIntent, result.IntentFamily);
        Assert.Equal(expectedIntent, result.PrimaryIntent);
        Assert.False(result.IsAmbiguous);
        Assert.False(result.IsUnsupported);
        Assert.Empty(result.SecondaryIntents);
        Assert.InRange(result.Confidence, 0.45d, 0.98d);
        Assert.NotEmpty(result.ReasonCodes);
        Assert.All(result.ReasonCodes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
    }

    [Theory]
    [InlineData("Can I afford to go out this weekend and where should I go nearby?")]
    [InlineData("How is my budget looking and where can I cut back this week?")]
    [InlineData("Where can I go nearby for 30 and can I afford it?")]
    public void Route_MixedIntentEvalFixtures_DetectsMixedIntent(string query)
    {
        var result = _router.Route(query);

        Assert.Equal(FinancialCompanionIntent.MixedQuery, result.IntentFamily);
        Assert.False(result.IsAmbiguous);
        Assert.False(result.IsUnsupported);
        Assert.NotEmpty(result.SecondaryIntents);
        Assert.Contains("mixed_query_detected", result.ReasonCodes);
    }

    [Fact]
    public void Route_MixedIntent_SpendingAndCutback_PreservesPrimaryAndSecondary()
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
    [InlineData("Money... uh... I don't know, help?")]
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
    [InlineData("Show me the latest sports score.")]
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
        Assert.Empty(affordability.SecondaryIntents);
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
    public void Route_DoesNotInflateMixedForSingleIntentBudgetQuestion()
    {
        var result = _router.Route("How much budget do I have left this month?");

        Assert.Equal(FinancialCompanionIntent.BudgetStatus, result.IntentFamily);
        Assert.Empty(result.SecondaryIntents);
    }

    [Fact]
    public void Route_Safeguard_WhitespaceOnly_ReturnsAmbiguous()
    {
        var result = _router.Route("      ");

        Assert.Equal(FinancialCompanionIntent.Ambiguous, result.IntentFamily);
        Assert.Contains("query_empty_or_whitespace", result.ReasonCodes);
    }

    [Fact]
    public void Route_Safeguard_PunctuationHeavyNoise_RemainsBounded()
    {
        var result = _router.Route("!!!! ???? .... $$$$ ### !!!! ???");

        Assert.True(result.IntentFamily is FinancialCompanionIntent.Ambiguous or FinancialCompanionIntent.Unsupported);
        Assert.NotEmpty(result.ReasonCodes);
    }

    [Fact]
    public void Route_Safeguard_VeryLongInput_RemainsDeterministic()
    {
        var longQuery = string.Join(' ', Enumerable.Repeat("Can I afford this purchase and how is my budget looking nearby", 80));
        var baseline = _router.Route(longQuery);

        for (var i = 0; i < 10; i++)
        {
            var repeat = _router.Route(longQuery);
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
