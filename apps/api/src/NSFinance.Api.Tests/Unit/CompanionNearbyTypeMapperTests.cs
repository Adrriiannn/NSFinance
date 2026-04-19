using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionNearbyTypeMapperTests
{
    private readonly CompanionNearbyTypeMapper sut = new();

    [Fact]
    public void Map_CoffeeNearMe_MapsToCafeType()
    {
        var result = sut.Map(
            "coffee shops near me",
            BuildConstraints());

        Assert.Contains("cafe", result.IncludedTypes);
        Assert.Contains("nearby_type_from_query_phrase", result.ReasonCodes);
    }

    [Fact]
    public void Map_PlaceTypeHints_MapsDeterministically()
    {
        var result = sut.Map(
            "any query",
            BuildConstraints(placeTypeHints: ["museum", "park", "museum"]));

        Assert.Equal(["museum", "park"], result.IncludedTypes);
        Assert.Contains("nearby_type_from_place_hint", result.ReasonCodes);
    }

    [Fact]
    public void Map_AudienceFallback_MapsKidsToPlayground()
    {
        var result = sut.Map(
            "somewhere near me",
            BuildConstraints(audienceHints: ["kids"]));

        Assert.Equal(["playground"], result.IncludedTypes);
        Assert.Contains("nearby_type_from_audience_default", result.ReasonCodes);
    }

    [Fact]
    public void Map_UnmappedPrompt_ReturnsEmptyTypes()
    {
        var result = sut.Map(
            "somewhere near me",
            BuildConstraints());

        Assert.Empty(result.IncludedTypes);
    }

    private static LocalDiscoveryConstraintExtractionResult BuildConstraints(
        IReadOnlyList<string>? placeTypeHints = null,
        IReadOnlyList<string>? audienceHints = null)
    {
        return new LocalDiscoveryConstraintExtractionResult(
            IsLocalDiscoveryCandidate: true,
            Confidence: 0.9,
            HasNearMeLanguage: true,
            HasExplicitLocality: false,
            LocalityHint: null,
            PlaceTypeHints: placeTypeHints ?? [],
            AudienceHints: audienceHints ?? [],
            TimeHints: [],
            PreferenceHints: [],
            ReasonCodes: []);
    }
}
