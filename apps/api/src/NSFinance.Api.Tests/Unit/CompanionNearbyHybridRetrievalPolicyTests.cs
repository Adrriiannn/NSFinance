using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionNearbyHybridRetrievalPolicyTests
{
    private readonly CompanionNearbyHybridRetrievalPolicy sut = new();

    [Fact]
    public void Decide_GpsAndNearMe_UsesHybrid()
    {
        var decision = sut.Decide(
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 1500),
            BuildConstraints(hasNearMeLanguage: true));

        Assert.True(decision.UseHybridRetrieval);
        Assert.Equal("places_retrieval:hybrid_applicable_gps_near_me", decision.ReasonCode);
    }

    [Fact]
    public void Decide_NonGps_DoesNotUseHybrid()
    {
        var decision = sut.Decide(
            new PlaceSearchLocationContext(
                Source: "typed_area",
                TypedArea: "Dublin"),
            BuildConstraints(hasNearMeLanguage: true));

        Assert.False(decision.UseHybridRetrieval);
        Assert.Equal("places_retrieval:hybrid_not_applicable_non_gps", decision.ReasonCode);
    }

    [Fact]
    public void Decide_GpsWithoutNearMeLanguage_DoesNotUseHybrid()
    {
        var decision = sut.Decide(
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 1500),
            BuildConstraints(hasNearMeLanguage: false));

        Assert.False(decision.UseHybridRetrieval);
        Assert.Equal("places_retrieval:hybrid_not_applicable_non_near_me", decision.ReasonCode);
    }

    [Fact]
    public void Decide_GpsWithPlannerNearMeSemantic_UsesHybrid()
    {
        var decision = sut.Decide(
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 1500,
                HasNearMeSemantic: true),
            BuildConstraints(hasNearMeLanguage: false));

        Assert.True(decision.UseHybridRetrieval);
        Assert.Equal("places_retrieval:hybrid_applicable_gps_near_me", decision.ReasonCode);
    }

    [Fact]
    public void Decide_GpsWithCommerceImplicitLocalBias_UsesHybrid()
    {
        var decision = sut.Decide(
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 1500,
                PlannerIntentFamily: RealWorldIntentFamily.CommerceDiscovery,
                PlannerSelectedDomain: RealWorldDiscoveryDomain.ElectronicsRetail,
                ImplicitLocalBias: true),
            BuildConstraints(hasNearMeLanguage: false));

        Assert.True(decision.UseHybridRetrieval);
        Assert.Equal("places_retrieval:hybrid_applicable_gps_commerce_local_bias", decision.ReasonCode);
    }

    private static LocalDiscoveryConstraintExtractionResult BuildConstraints(
        bool hasNearMeLanguage)
    {
        return new LocalDiscoveryConstraintExtractionResult(
            IsLocalDiscoveryCandidate: true,
            Confidence: 0.8,
            HasNearMeLanguage: hasNearMeLanguage,
            HasExplicitLocality: false,
            LocalityHint: null,
            PlaceTypeHints: [],
            AudienceHints: [],
            TimeHints: [],
            PreferenceHints: [],
            ReasonCodes: []);
    }
}
