using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlacesRequestBuildersTests
{
    private readonly LocalDiscoveryConstraintExtractor extractor = new();
    private readonly CompanionPlacesVocabularyNormalizer normalizer = new();

    [Fact]
    public void Normalize_GenericTypos_AreCorrected_WithoutDestroyingNamedTokens()
    {
        var result = normalizer.Normalize("can you pleade show cofee in ranelaghx");

        Assert.Contains("coffee", result.NormalizedQuery);
        Assert.Contains("ranelaghx", result.NormalizedQuery);
        Assert.Contains("places_request:text_query_typo_normalized", result.ReasonCodes);
    }

    [Fact]
    public void BuildTextQuery_GpsNearMe_ProducesCleanSemanticQuery()
    {
        var builder = new CompanionPlacesTextQueryBuilder(normalizer);
        var constraints = extractor.Extract("can you pleade show me some coffee shops near me?");

        var result = builder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: "can you pleade show me some coffee shops near me?",
                Constraints: constraints,
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 2000),
                IsGpsNearMe: true));

        Assert.True(result.Succeeded);
        Assert.Equal("coffee shops", result.Query);
        Assert.Contains("places_request:text_query_typo_normalized", result.ReasonCodes);
        Assert.DoesNotContain("pleade", result.Query!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTextQuery_LocalityInjection_RequiresTrustedLocality()
    {
        var builder = new CompanionPlacesTextQueryBuilder(normalizer);
        var trustedConstraints = extractor.Extract("museums around Dublin");

        var trustedResult = builder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: "museums around Dublin",
                Constraints: trustedConstraints,
                LocationContext: new PlaceSearchLocationContext(
                    Source: "typed_area",
                    TypedArea: "Dublin"),
                IsGpsNearMe: false));

        Assert.True(trustedResult.Succeeded);
        Assert.Contains("in dublin", trustedResult.Query!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("places_request:text_query_locality_injected", trustedResult.ReasonCodes);

        var noisyConstraints = trustedConstraints with
        {
            LocalityHint = "46",
            HasExplicitLocality = true
        };

        var noisyResult = builder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: "coffee shops in 46",
                Constraints: noisyConstraints,
                LocationContext: null,
                IsGpsNearMe: false));

        Assert.True(noisyResult.Succeeded);
        Assert.DoesNotContain("in 46", noisyResult.Query!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("places_request:text_query_locality_skipped_low_confidence", noisyResult.ReasonCodes);
    }

    [Fact]
    public void BuildTextQuery_AuthoritativeElectronicsHint_DoesNotFallBackToTourismQuery()
    {
        var builder = new CompanionPlacesTextQueryBuilder(normalizer);
        var constraints = new LocalDiscoveryConstraintExtractionResult(
            IsLocalDiscoveryCandidate: true,
            Confidence: 0.95d,
            HasNearMeLanguage: false,
            HasExplicitLocality: false,
            LocalityHint: null,
            PlaceTypeHints: ["electronics_store"],
            AudienceHints: [],
            TimeHints: [],
            PreferenceHints: [],
            ReasonCodes: ["real_world_retrieval_planner_constraints_applied"]);

        var result = builder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: "electronics stores",
                Constraints: constraints,
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 2000),
                IsGpsNearMe: false));

        Assert.True(result.Succeeded);
        Assert.Equal("electronics stores", result.Query);
        Assert.DoesNotContain("places to visit", result.Query!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTextQuery_PlannerBrandTerm_IsPreservedForBrandFirstSearch()
    {
        var builder = new CompanionPlacesTextQueryBuilder(normalizer);
        var constraints = extractor.Extract("starbucks near me");

        var result = builder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: "starbucks near me",
                Constraints: constraints,
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 2000,
                    PlannerBrandTerm: "starbucks",
                    PlannerCanonicalConcept: "cafe",
                    SearchScope: "brand_first"),
                IsGpsNearMe: true));

        Assert.True(result.Succeeded);
        Assert.Equal("starbucks", result.Query);
        Assert.Contains("places_request:text_query_planner_brand_preserved", result.ReasonCodes);
    }

    [Fact]
    public void BuildTextQuery_CarParksNearMe_PreservesParkingIntent()
    {
        var builder = new CompanionPlacesTextQueryBuilder(normalizer);
        var constraints = extractor.Extract("car parks near me");

        var result = builder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: "car parks near me",
                Constraints: constraints,
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 2000),
                IsGpsNearMe: true));

        Assert.True(result.Succeeded);
        Assert.Equal("car parks", result.Query);
    }

    [Fact]
    public void BuildNearbyRequest_ValidInput_IsSanitizedAndClamped()
    {
        var builder = new CompanionPlacesNearbyRequestBuilder();

        var result = builder.Build(
            new CompanionPlacesNearbyRequestBuildRequest(
                CountryCode: "Ireland",
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 50_000),
                IncludedTypes: ["cafe", "invalid-type", "cafe"],
                MaxCandidates: 32,
                DefaultRadiusMeters: 2_500));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Request);
        Assert.Equal(15_000, result.Request!.RadiusMeters);
        Assert.Equal(["cafe"], result.Request.IncludedTypes);
        Assert.Null(result.Request.CountryCode);
        Assert.Contains("places_request:nearby_simplified_before_send", result.ReasonCodes);
        Assert.Contains("places_request:nearby_preflight_valid", result.ReasonCodes);
    }

    [Fact]
    public void BuildNearbyRequest_InvalidCoordinates_FailsPreflight()
    {
        var builder = new CompanionPlacesNearbyRequestBuilder();

        var result = builder.Build(
            new CompanionPlacesNearbyRequestBuildRequest(
                CountryCode: "IE",
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 133.0,
                    Longitude: -6.0,
                    RadiusMeters: 2000),
                IncludedTypes: ["cafe"],
                MaxCandidates: 8,
                DefaultRadiusMeters: 2_500));

        Assert.False(result.Succeeded);
        Assert.Null(result.Request);
        Assert.Equal("nearby_invalid_coordinates", result.FailureReason);
        Assert.Contains("places_request:nearby_preflight_failed", result.ReasonCodes);
    }

    [Fact]
    public void BuildNearbyRequest_WithoutSupportedTypes_FailsPreflight()
    {
        var builder = new CompanionPlacesNearbyRequestBuilder();

        var result = builder.Build(
            new CompanionPlacesNearbyRequestBuildRequest(
                CountryCode: "IE",
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.3570,
                    Longitude: -6.4486,
                    RadiusMeters: 2000),
                IncludedTypes: ["unknown_type"],
                MaxCandidates: 8,
                DefaultRadiusMeters: 2_500));

        Assert.False(result.Succeeded);
        Assert.Null(result.Request);
        Assert.Equal("nearby_no_supported_types", result.FailureReason);
        Assert.Contains("places_request:nearby_preflight_failed", result.ReasonCodes);
    }
}
