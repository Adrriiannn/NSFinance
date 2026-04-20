using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionLocationGroundingParserTests
{
    [Fact]
    public void Parse_MetadataCoordinatesAndTypedArea_ParsesGrounding()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CompanionLocationMetadataKeys.Source] = "gps",
            [CompanionLocationMetadataKeys.Latitude] = "53.3571",
            [CompanionLocationMetadataKeys.Longitude] = "-6.4479",
            [CompanionLocationMetadataKeys.RadiusMeters] = "3500",
            [CompanionLocationMetadataKeys.TypedArea] = "Lucan Village"
        };

        var parsed = CompanionLocationGroundingParser.Parse(metadata, state: null);

        Assert.True(parsed.HasCoordinates);
        Assert.True(parsed.HasTypedArea);
        Assert.Equal("gps", parsed.Source);
        Assert.Equal(53.3571d, parsed.Latitude);
        Assert.Equal(-6.4479d, parsed.Longitude);
        Assert.Equal(3500, parsed.RadiusMeters);
        Assert.Equal("Lucan Village", parsed.TypedArea);
    }

    [Fact]
    public void Parse_StateConstraintsCoordinates_ParsesGroundingWhenMetadataMissing()
    {
        var state = new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CompanionLocationMetadataKeys.Source] = "gps",
                [CompanionLocationMetadataKeys.Latitude] = "53.3498",
                [CompanionLocationMetadataKeys.Longitude] = "-6.2603",
                [CompanionLocationMetadataKeys.RadiusMeters] = "2000"
            },
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: "current_location",
            MerchantInvestigationSubject: null,
            RecentConclusions: []);

        var parsed = CompanionLocationGroundingParser.Parse(metadata: null, state);

        Assert.True(parsed.HasCoordinates);
        Assert.Equal("gps", parsed.Source);
        Assert.Equal(53.3498d, parsed.Latitude);
        Assert.Equal(-6.2603d, parsed.Longitude);
        Assert.Equal(2000, parsed.RadiusMeters);
        Assert.False(parsed.HasTypedArea);
    }

    [Fact]
    public void Parse_StateLocationPreferenceFallback_UsesTypedArea()
    {
        var state = new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(),
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: "Dublin 9",
            MerchantInvestigationSubject: null,
            RecentConclusions: []);

        var parsed = CompanionLocationGroundingParser.Parse(metadata: null, state);

        Assert.False(parsed.HasCoordinates);
        Assert.True(parsed.HasTypedArea);
        Assert.Equal("typed_area", parsed.Source);
        Assert.Equal("Dublin 9", parsed.TypedArea);
    }

    [Theory]
    [InlineData("Find coffee near me", true)]
    [InlineData("Any brunch spots around here?", true)]
    [InlineData("Find coffee near Dublin city centre", false)]
    [InlineData("Show restaurants in Lucan village", false)]
    public void RequiresCurrentLocation_UsesNearMeStyleSignals(string query, bool expected)
    {
        Assert.Equal(expected, CompanionLocationGroundingParser.RequiresCurrentLocation(query));
    }

    [Fact]
    public void ApplyTypedAreaToQuery_RewritesNearMeLanguage()
    {
        var rewritten = CompanionLocationGroundingParser.ApplyTypedAreaToQuery(
            "Find me coffee near me tonight",
            "Dublin city centre");

        Assert.Contains("Dublin city centre", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("near me", rewritten, StringComparison.OrdinalIgnoreCase);
    }
}
