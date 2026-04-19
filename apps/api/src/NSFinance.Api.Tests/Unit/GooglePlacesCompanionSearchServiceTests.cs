using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesCompanionSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_WithGpsGrounding_ForwardsCoordinatesToDiscoveryRequest()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            [
                BuildResult(candidates: [BuildCandidate("playground-1")])
            ]);
        var sut = CreateSut(
            discovery,
            new FixedLocalityResolver(
                new CompanionLocalityResolutionResult(
                    HasCoordinates: false,
                    Latitude: null,
                    Longitude: null,
                    ResolvedLocalityLabel: null,
                    ReasonCode: "unused")));

        await sut.SearchAsync(
            "kids playgrounds near me",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3498,
                Longitude: -6.2603,
                RadiusMeters: 2500),
            CancellationToken.None);

        var request = Assert.Single(discovery.Requests);
        Assert.Equal(53.3498, request.Latitude);
        Assert.Equal(-6.2603, request.Longitude);
        Assert.Equal(2500, request.RadiusMeters);
    }

    [Fact]
    public async Task SearchAsync_WithTypedArea_ResolvesLocalityToCoordinates()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            [
                BuildResult(candidates: [BuildCandidate("museum-1")])
            ]);
        var sut = CreateSut(
            discovery,
            new FixedLocalityResolver(
                new CompanionLocalityResolutionResult(
                    HasCoordinates: true,
                    Latitude: 53.3498,
                    Longitude: -6.2603,
                    ResolvedLocalityLabel: "Dublin",
                    ReasonCode: "locality_resolution_succeeded")));

        var result = await sut.SearchAsync(
            "museums around Dublin",
            "IE",
            new PlaceSearchLocationContext(
                Source: "query_locality",
                TypedArea: "Dublin"),
            CancellationToken.None);

        var request = Assert.Single(discovery.Requests);
        Assert.Equal(53.3498, request.Latitude);
        Assert.Equal(-6.2603, request.Longitude);
        Assert.Contains("places_query_shape:locality_resolution_succeeded", result.Warnings ?? []);
        Assert.Contains("places_query_shape:results_found_primary", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_PrimaryNoResults_FallbackReturnsResults()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            [
                BuildResult(candidates: []),
                BuildResult(candidates: [BuildCandidate("museum-2")])
            ]);
        var sut = CreateSut(
            discovery,
            new FixedLocalityResolver(
                new CompanionLocalityResolutionResult(
                    HasCoordinates: false,
                    Latitude: null,
                    Longitude: null,
                    ResolvedLocalityLabel: null,
                    ReasonCode: "unused")));

        var result = await sut.SearchAsync(
            "museums around Dublin",
            "IE",
            new PlaceSearchLocationContext(
                Source: "typed_area",
                TypedArea: "Dublin"),
            CancellationToken.None);

        Assert.Equal(2, discovery.Requests.Count);
        Assert.Contains("places_query_shape:no_results_primary", result.Warnings ?? []);
        Assert.Contains("places_query_shape:fallback_text_search", result.Warnings ?? []);
        Assert.Contains("places_query_shape:results_found_fallback", result.Warnings ?? []);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task SearchAsync_PrimaryAndFallbackNoResults_ReturnsBoundedNoData()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            [
                BuildResult(candidates: []),
                BuildResult(candidates: [])
            ]);
        var sut = CreateSut(
            discovery,
            new FixedLocalityResolver(
                new CompanionLocalityResolutionResult(
                    HasCoordinates: false,
                    Latitude: null,
                    Longitude: null,
                    ResolvedLocalityLabel: null,
                    ReasonCode: "unused")));

        var result = await sut.SearchAsync(
            "family places in Dublin",
            "IE",
            new PlaceSearchLocationContext(
                Source: "typed_area",
                TypedArea: "Dublin"),
            CancellationToken.None);

        Assert.Equal(2, discovery.Requests.Count);
        Assert.Empty(result.Items);
        Assert.Contains("places_query_shape:no_results_primary", result.Warnings ?? []);
        Assert.Contains("places_query_shape:no_results_fallback", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_GpsNearMe_AppliesDistanceAwareReranking()
    {
        var farther = BuildCandidate(
            placeId: "farther",
            latitude: 53.3900,
            longitude: -6.5000);
        var closer = BuildCandidate(
            placeId: "closer",
            latitude: 53.3571,
            longitude: -6.4487);
        var discovery = new TrackingCompanionDiscoveryService(
            [
                BuildResult(candidates: [farther, closer])
            ]);
        var sut = CreateSut(
            discovery,
            new FixedLocalityResolver(
                new CompanionLocalityResolutionResult(
                    HasCoordinates: false,
                    Latitude: null,
                    Longitude: null,
                    ResolvedLocalityLabel: null,
                    ReasonCode: "unused")));

        var result = await sut.SearchAsync(
            "coffee shops near me",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 2000),
            CancellationToken.None);

        Assert.Equal("closer", result.Items[0].PlaceId);
        Assert.Contains("places_ranking:gps_distance_applied", result.Warnings ?? []);
        Assert.Contains("places_ranking:near_me_distance_ranked", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_NonGpsLocality_DoesNotApplyDistanceRanking()
    {
        var first = BuildCandidate("first", latitude: 53.3900, longitude: -6.5000);
        var second = BuildCandidate("second", latitude: 53.3571, longitude: -6.4487);
        var discovery = new TrackingCompanionDiscoveryService(
            [
                BuildResult(candidates: [first, second])
            ]);
        var sut = CreateSut(
            discovery,
            new FixedLocalityResolver(
                new CompanionLocalityResolutionResult(
                    HasCoordinates: false,
                    Latitude: null,
                    Longitude: null,
                    ResolvedLocalityLabel: null,
                    ReasonCode: "unused")));

        var result = await sut.SearchAsync(
            "museums around Dublin",
            "IE",
            new PlaceSearchLocationContext(
                Source: "typed_area",
                TypedArea: "Dublin"),
            CancellationToken.None);

        Assert.Equal("first", result.Items[0].PlaceId);
        Assert.Contains("places_ranking:distance_not_applicable_non_gps", result.Warnings ?? []);
    }

    private static GooglePlacesCompanionSearchService CreateSut(
        TrackingCompanionDiscoveryService discovery,
        ICompanionLocalityResolutionService resolver)
    {
        return new GooglePlacesCompanionSearchService(
            discovery,
            new LocalDiscoveryQueryShaper(new LocalDiscoveryConstraintExtractor()),
            resolver,
            new CompanionPlaceRankingPolicy(),
            Options.Create(new GooglePlacesOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                DefaultSearchRadiusMeters = 2500
            }),
            NullLogger<GooglePlacesCompanionSearchService>.Instance);
    }

    private static CompanionPlaceDiscoveryResult BuildResult(
        IReadOnlyList<CompanionPlaceCandidate> candidates)
    {
        return new CompanionPlaceDiscoveryResult(
            Succeeded: true,
            Candidates: candidates,
            Metadata: new PlaceSearchMetadata(
                UseCase: "companion_discovery",
                FromCache: false,
                RequestedCandidateCount: 8,
                ReturnedCandidateCount: candidates.Count,
                FieldMaskVariant: "companion_discovery_v1",
                Elapsed: TimeSpan.FromMilliseconds(5),
                TimedOut: false,
                ProviderErrorCode: null),
            Warnings: []);
    }

    private static CompanionPlaceCandidate BuildCandidate(
        string placeId,
        double latitude = 53.3498,
        double longitude = -6.2603)
    {
        return new CompanionPlaceCandidate(
            PlaceId: placeId,
            ResourceName: $"places/{placeId}",
            DisplayName: $"Place {placeId}",
            PrimaryType: "museum",
            PrimaryTypeDisplayName: "Museum",
            Types: ["museum"],
            NationalPhoneNumber: null,
            FormattedAddress: "Dublin",
            ShortFormattedAddress: "Dublin",
            Rating: 4.4,
            UserRatingCount: 100,
            GoogleMapsUri: null,
            WebsiteUri: null,
            OpeningHours: new PlaceOpeningHoursSummary(null, [], null),
            BusinessStatus: null,
            PriceLevel: null,
            IconMaskBaseUri: null,
            IconBackgroundColor: null,
            Takeout: null,
            Delivery: null,
            DineIn: null,
            Reservable: null,
            ServesBreakfast: null,
            ServesLunch: null,
            ServesDinner: null,
            ServesBeer: null,
            ServesWine: null,
            ServesBrunch: null,
            ServesVegetarianFood: null,
            OutdoorSeating: null,
            LiveMusic: null,
            MenuForChildren: null,
            ServesCocktails: null,
            ServesDessert: null,
            ServesCoffee: null,
            AllowsDogs: null,
            Restroom: null,
            GoodForGroups: null,
            GoodForWatchingSports: null,
            PaymentOptions: new PlacePaymentOptionsSummary(null, null, null, null),
            AccessibilityOptions: new PlaceAccessibilitySummary(null, null, null, null),
            EditorialSummary: new PlaceEditorialSummary(null, null),
            Location: new PlaceLocationSummary(latitude, longitude));
    }

    private sealed class TrackingCompanionDiscoveryService(
        IReadOnlyList<CompanionPlaceDiscoveryResult> results) : ICompanionPlaceDiscoveryService
    {
        private int index;
        public List<CompanionPlaceDiscoveryRequest> Requests { get; } = [];

        public Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
            CompanionPlaceDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var current = index < results.Count
                ? results[index]
                : results[^1];
            index += 1;
            return Task.FromResult(current);
        }
    }

    private sealed class FixedLocalityResolver(
        CompanionLocalityResolutionResult result) : ICompanionLocalityResolutionService
    {
        public Task<CompanionLocalityResolutionResult> ResolveAsync(
            string? locality,
            string? countryCode,
            string? languageCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
