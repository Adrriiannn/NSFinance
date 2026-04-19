using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionIntentRoutingPipelineTests
{
    private readonly CompanionIntentNormalizer _normalizer = new();
    private readonly CompanionIntentSignalExtractor _extractor = new();
    private readonly CompanionIntentScorer _scorer = new();
    private readonly CompanionIntentResolutionPolicy _resolutionPolicy = new();

    [Fact]
    public void Normalizer_NormalizesCaseWhitespaceAndPunctuation()
    {
        var normalized = _normalizer.Normalize("  HOW   much budget... do I have LEFT?!  ");

        Assert.Equal("how much budget do i have left", normalized.NormalizedText);
        Assert.Equal(["how", "much", "budget", "do", "i", "have", "left"], normalized.Tokens);
        Assert.False(normalized.WasTruncated);
        Assert.False(normalized.IsEmpty);
    }

    [Fact]
    public void Normalizer_TruncatesVeryLongInputAndMarksFlag()
    {
        var input = string.Join(' ', Enumerable.Repeat("can i afford this and where should i go nearby", 120));
        var normalized = _normalizer.Normalize(input);

        Assert.True(normalized.WasTruncated);
        Assert.True(normalized.NormalizedLength <= 640);
        Assert.True(normalized.Tokens.Count <= 120);
    }

    [Fact]
    public void SignalExtractor_OverlappingPrompt_EmitsMixedRelevantSignalGroups()
    {
        var normalized = _normalizer.Normalize("Can I afford to go out this weekend and where should I go nearby for 30?");
        var extraction = _extractor.Extract(normalized);

        Assert.Contains("affordability", extraction.SignalGroups);
        Assert.Contains("local_places", extraction.SignalGroups);
        Assert.Contains("mixed_connectors", extraction.SignalGroups);
        Assert.True(extraction.HasMixedConnector);
        Assert.True(extraction.HasFinanceMarker);
        Assert.Contains(extraction.Signals, signal => signal.ReasonCode == "signal_places_budget_constraint");
    }

    [Fact]
    public void SignalExtractor_LocalityDrivenDiscoveryPrompt_EmitsLocalPlacesSignals()
    {
        var normalized = _normalizer.Normalize("Museums around Dublin with family this weekend");
        var extraction = _extractor.Extract(normalized);

        Assert.Contains("local_places", extraction.SignalGroups);
        Assert.Contains(extraction.Signals, signal => signal.ReasonCode == "signal_local_discovery_structured");
        Assert.Contains(extraction.Signals, signal => signal.ReasonCode == "signal_local_discovery_explicit_locality");
        Assert.Contains(extraction.Signals, signal => signal.ReasonCode == "signal_local_discovery_place_type");
    }

    [Fact]
    public void Scorer_SameSignals_ProducesDeterministicScoresAndReasons()
    {
        var normalized = _normalizer.Normalize("Am I over budget and where can I cut back?");
        var extraction = _extractor.Extract(normalized);

        var first = _scorer.Score(extraction);
        var second = _scorer.Score(extraction);

        Assert.Equal(first.ScoreSummary, second.ScoreSummary);
        Assert.True(first.RankedScores.SequenceEqual(second.RankedScores));
        Assert.Equal(
            first.ReasonCodesByIntent.Keys.OrderBy(x => x).ToArray(),
            second.ReasonCodesByIntent.Keys.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void ResolutionPolicy_FinanceAdjacentButLowSpecificity_ReturnsAmbiguous()
    {
        var normalized = _normalizer.Normalize("Help me with my money");
        var extraction = _extractor.Extract(normalized);
        var scoring = _scorer.Score(extraction);

        var resolution = _resolutionPolicy.Resolve(extraction, scoring);

        Assert.Equal(FinancialCompanionIntent.Ambiguous, resolution.Routing.IntentFamily);
        Assert.True(resolution.Routing.IsAmbiguous);
        Assert.Equal("no_match_ambiguous_phrase", resolution.ResolutionPath);
    }

    [Fact]
    public void ResolutionPolicy_OutsideScope_ReturnsUnsupported()
    {
        var normalized = _normalizer.Normalize("What's the weather tomorrow in Madrid?");
        var extraction = _extractor.Extract(normalized);
        var scoring = _scorer.Score(extraction);

        var resolution = _resolutionPolicy.Resolve(extraction, scoring);

        Assert.Equal(FinancialCompanionIntent.Unsupported, resolution.Routing.IntentFamily);
        Assert.True(resolution.Routing.IsUnsupported);
        Assert.Equal("unsupported_marker_without_finance", resolution.ResolutionPath);
    }
}
