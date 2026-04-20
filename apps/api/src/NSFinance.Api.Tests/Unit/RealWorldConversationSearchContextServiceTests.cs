using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldConversationSearchContextServiceTests
{
    [Fact]
    public void WriteAndRead_FreshContext_ReusesContextWithMarker()
    {
        var now = new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero);
        var sut = new RealWorldConversationSearchContextService();
        sut.Write(
            "session-1",
            BuildWriteInput(
                grounding: new CompanionLocationGrounding(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 1800,
                    TypedArea: null,
                    LocalityLabel: "Lucan",
                    AccuracyBucket: "high",
                    CapturedAtUtc: now),
                localDiscovery: BuildLocalDiscovery(localityHint: null)),
            now);

        var read = sut.Read("session-1", now.AddMinutes(5));

        Assert.True(read.ContextReused);
        Assert.False(read.ContextExpired);
        Assert.False(read.DeviceLocationExpired);
        Assert.Contains("real_world_search_context_reused", read.ReasonCodes);
        Assert.NotNull(read.Context);
        Assert.True(read.Context!.HasDeviceCoordinates);
        Assert.Equal(53.3570, read.Context.DeviceLatitude);
        Assert.Equal(-6.4486, read.Context.DeviceLongitude);
    }

    [Fact]
    public void Read_ExpiredDeviceLocation_StripsCoordinatesButKeepsExplicitArea()
    {
        var now = new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero);
        var sut = new RealWorldConversationSearchContextService();
        sut.Write(
            "session-2",
            BuildWriteInput(
                grounding: new CompanionLocationGrounding(
                    Source: "explicit_area_over_device",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 1600,
                    TypedArea: "Dublin 2",
                    LocalityLabel: "Dublin",
                    AccuracyBucket: "high",
                    CapturedAtUtc: now),
                localDiscovery: BuildLocalDiscovery(localityHint: "Dublin 2")),
            now);

        var read = sut.Read("session-2", now.AddMinutes(20));

        Assert.True(read.ContextReused);
        Assert.False(read.ContextExpired);
        Assert.True(read.DeviceLocationExpired);
        Assert.Contains("real_world_search_context_location_expired", read.ReasonCodes);
        Assert.NotNull(read.Context);
        Assert.Equal("Dublin 2", read.Context!.ExplicitArea);
        Assert.False(read.Context.HasDeviceCoordinates);
        Assert.Null(read.Context.DeviceLatitude);
        Assert.Null(read.Context.DeviceLongitude);
    }

    [Fact]
    public void Read_ExpiredContext_DropsContextAndReturnsExpiryMarker()
    {
        var now = new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero);
        var sut = new RealWorldConversationSearchContextService();
        sut.Write(
            "session-3",
            BuildWriteInput(
                grounding: new CompanionLocationGrounding(
                    Source: "typed_area",
                    Latitude: null,
                    Longitude: null,
                    RadiusMeters: null,
                    TypedArea: "Dublin 9",
                    LocalityLabel: "Dublin 9",
                    AccuracyBucket: null,
                    CapturedAtUtc: null),
                localDiscovery: BuildLocalDiscovery(localityHint: "Dublin 9")),
            now);

        var read = sut.Read("session-3", now.AddMinutes(50));

        Assert.False(read.ContextReused);
        Assert.True(read.ContextExpired);
        Assert.Null(read.Context);
        Assert.Contains("real_world_search_context_expired", read.ReasonCodes);
    }

    private static RealWorldConversationSearchContextWriteInput BuildWriteInput(
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.ExploratoryAssistance,
            RecommendedExecutionMode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: true,
            ClarificationNeeded: false,
            HasNearMeLanguage: true,
            HasExplicitLocality: localDiscovery.HasExplicitLocality,
            Confidence: 0.82d,
            CandidateDomains:
            [
                RealWorldDiscoveryDomain.MovieTheater,
                RealWorldDiscoveryDomain.PubBar
            ],
            ClarificationPrompt: null,
            ReasonCodes: ["test_interpretation"],
            Warnings: [])
        {
            CandidateConcepts = ["movie_theater"]
        };
        var plan = new RealWorldExecutionPlan(
            Mode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            IntentFamily: interpretation.IntentFamily,
            ShouldHandoffToCompanion: true,
            ShouldUsePlaces: true,
            UseDirectPlacesExecution: true,
            RequiresLocationGrounding: true,
            SelectedDomains: interpretation.CandidateDomains,
            ClarificationPrompt: null,
            ReasonCodes: ["test_plan"]);

        return new RealWorldConversationSearchContextWriteInput(
            Grounding: grounding,
            LocalDiscovery: localDiscovery,
            Interpretation: interpretation,
            Plan: plan);
    }

    private static LocalDiscoveryConstraintExtractionResult BuildLocalDiscovery(string? localityHint)
    {
        return new LocalDiscoveryConstraintExtractionResult(
            IsLocalDiscoveryCandidate: true,
            Confidence: 0.86d,
            HasNearMeLanguage: true,
            HasExplicitLocality: !string.IsNullOrWhiteSpace(localityHint),
            LocalityHint: localityHint,
            PlaceTypeHints: [],
            AudienceHints: [],
            TimeHints: ["tonight"],
            PreferenceHints: [],
            ReasonCodes: ["test_constraints"]);
    }
}
