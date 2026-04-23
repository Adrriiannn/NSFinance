using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class LocalDiscoveryConstraintExtractionTests
{
    private readonly LocalDiscoveryConstraintExtractor _extractor = new();
    private readonly LocalDiscoveryQueryShaper _shaper;

    public LocalDiscoveryConstraintExtractionTests()
    {
        _shaper = new LocalDiscoveryQueryShaper(_extractor);
    }

    [Theory]
    [InlineData("places near me to dine out tonight")]
    [InlineData("kids playgrounds near me")]
    [InlineData("places to visit near me")]
    [InlineData("museums around Dublin")]
    [InlineData("parks in Lucan")]
    [InlineData("something fun nearby")]
    [InlineData("where can I go with family this weekend")]
    [InlineData("family places in Lucan")]
    [InlineData("dog friendly cafes nearby")]
    public void Extract_BroadLocalDiscoveryPrompts_AreDetected(string query)
    {
        var result = _extractor.Extract(query);

        Assert.True(result.IsLocalDiscoveryCandidate);
        Assert.True(result.Confidence >= 0.55d);
        Assert.NotEmpty(result.ReasonCodes);
    }

    [Fact]
    public void Extract_LocalityPrompt_CapturesLocalityAndPlaceType()
    {
        var result = _extractor.Extract("museums around Dublin");

        Assert.True(result.IsLocalDiscoveryCandidate);
        Assert.True(result.HasExplicitLocality);
        Assert.Equal("dublin", result.LocalityHint);
        Assert.Contains("museum", result.PlaceTypeHints);
    }

    [Fact]
    public void Extract_NoisyNumericLocality_IsRejected()
    {
        var result = _extractor.Extract("can you pleade show me coffee shops in 46?");

        Assert.False(result.HasExplicitLocality);
        Assert.Null(result.LocalityHint);
    }

    [Fact]
    public void Extract_AlphanumericLocality_RemainsSupported()
    {
        var result = _extractor.Extract("pubs in Dublin 2");

        Assert.True(result.HasExplicitLocality);
        Assert.Equal("dublin 2", result.LocalityHint);
    }

    [Theory]
    [InlineData("what about Dublin 2 instead?", "dublin 2")]
    [InlineData("try Rathmines instead", "rathmines")]
    [InlineData("not near me, Dublin 8", "dublin 8")]
    [InlineData("same thing but in Dublin 2", "dublin 2")]
    public void Extract_BranchOverridePhrases_CaptureLocality(string query, string expectedLocality)
    {
        var result = _extractor.Extract(query);

        Assert.True(result.HasExplicitLocality);
        Assert.Equal(expectedLocality, result.LocalityHint);
    }

    [Fact]
    public void Extract_FinanceOnlyPrompt_IsNotLocalDiscovery()
    {
        var result = _extractor.Extract("How is my monthly budget doing?");

        Assert.False(result.IsLocalDiscoveryCandidate);
    }

    [Fact]
    public void Extract_OpenNearMePrompt_IsStructuredCandidateEvenWithoutExplicitPlaceType()
    {
        var result = _extractor.Extract("what's open near me");

        Assert.True(result.IsLocalDiscoveryCandidate);
        Assert.True(result.HasNearMeLanguage);
        Assert.Contains("local_discovery_open_token", result.ReasonCodes);
    }

    [Fact]
    public void Extract_CoffeeShopsPhrase_DoesNotAddGenericStoreTypeHint()
    {
        var result = _extractor.Extract("coffee shops near me");

        Assert.Contains("cafe", result.PlaceTypeHints);
        Assert.DoesNotContain("store", result.PlaceTypeHints);
    }

    [Fact]
    public void Shape_NoGpsWithTypedArea_AppliesTypedAreaAndFriendlyHints()
    {
        var shaped = _shaper.Shape(
            "dog friendly cafes nearby",
            new PlaceSearchLocationContext(TypedArea: "Lucan"));

        Assert.Contains("in Lucan", shaped.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dog friendly", shaped.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dog_friendly", shaped.Query, StringComparison.Ordinal);
        Assert.Contains(
            "local_discovery_query_shaped",
            shaped.ReasonCodes);
    }

    [Fact]
    public void Shape_BroadFamilyPrompt_AppendsDefaultDiscoveryType()
    {
        var shaped = _shaper.Shape(
            "family places in Dublin",
            new PlaceSearchLocationContext(TypedArea: "Dublin"));

        Assert.Contains("family friendly attractions", shaped.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local_discovery_query_default_type_appended", shaped.ReasonCodes);
    }
}
