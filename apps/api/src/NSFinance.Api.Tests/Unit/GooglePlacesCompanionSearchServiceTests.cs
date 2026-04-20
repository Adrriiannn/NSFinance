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
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("playground-1")])
            ],
            nearbyResults:
            [
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
    public async Task SearchAsync_PlannerAuthoritativeMovieTheater_PreservesDomainAndNearMeSemantics()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("cinema-1")])
            ],
            nearbyResults:
            [
                BuildResult(candidates: [BuildCandidate("cinema-2")])
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
            "movie theaters",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 2000,
                PlannerSelectedDomain: RealWorldDiscoveryDomain.MovieTheater,
                PlannerSelectedConcept: "movietheater",
                PlannerAuthoritative: true,
                HasNearMeSemantic: true,
                PlannerExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
                PlannerMaxShortlist: 8),
            CancellationToken.None);

        var textRequest = Assert.Single(discovery.Requests);
        Assert.Equal("cinemas", textRequest.Query);
        Assert.Single(discovery.NearbyRequests);
        Assert.Contains("real_world_retrieval_plan_authoritative:true", result.Warnings ?? []);
        Assert.Contains("real_world_retrieval_plan_domain:movietheater", result.Warnings ?? []);
        Assert.Contains("places_retrieval:hybrid_applicable_gps_near_me", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_PlannerAuthoritativeCommerce_WithImplicitLocalBias_UsesHybridWithoutNearMePhrase()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("store-1")])
            ],
            nearbyResults:
            [
                BuildResult(candidates: [BuildCandidate("store-2")])
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
            "where can i buy a ps5",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 2000,
                PlannerSelectedDomain: RealWorldDiscoveryDomain.ElectronicsRetail,
                PlannerSelectedConcept: "ps5",
                PlannerIntentFamily: RealWorldIntentFamily.CommerceDiscovery,
                PlannerAuthoritative: true,
                HasNearMeSemantic: false,
                ImplicitLocalBias: true,
                PlannerExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
                PlannerMaxShortlist: 8),
            CancellationToken.None);

        var textRequest = Assert.Single(discovery.Requests);
        Assert.Equal("electronics stores", textRequest.Query);
        Assert.Single(discovery.NearbyRequests);
        Assert.Contains("places_retrieval:hybrid_applicable_gps_commerce_local_bias", result.Warnings ?? []);
        Assert.Contains("real_world_commerce_local_bias_enabled", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_TypoPrompt_BuildsCleanSemanticTextQuery()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("cafe-1")])
            ],
            nearbyResults:
            [
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
            "can you pleade show me some coffee shops near me?",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 2000),
            CancellationToken.None);

        var request = Assert.Single(discovery.Requests);
        Assert.Equal("coffee shops", request.Query);
        Assert.Contains("places_request:text_query_typo_normalized", result.Warnings ?? []);
        Assert.DoesNotContain("46", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_InvalidCountryCode_SimplifiesRegionBeforeProviderCalls()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("cafe-1")])
            ],
            nearbyResults:
            [
                BuildResult(candidates: [BuildCandidate("cafe-2")])
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
            "Ireland",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 2000),
            CancellationToken.None);

        var textRequest = Assert.Single(discovery.Requests);
        var nearbyRequest = Assert.Single(discovery.NearbyRequests);
        Assert.Null(textRequest.CountryCode);
        Assert.Null(nearbyRequest.CountryCode);
        Assert.Contains(
            "places_request:region_code_simplified_invalid_country_code",
            result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_NoisyNumericLocality_DoesNotLeakIntoTextQuery()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("cafe-1")])
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
            "can you pleade show me some coffee shops in 46?",
            "IE",
            new PlaceSearchLocationContext(
                Source: "typed_area",
                TypedArea: null),
            CancellationToken.None);

        var request = Assert.Single(discovery.Requests);
        Assert.Equal("coffee shops", request.Query);
        Assert.DoesNotContain("in 46", request.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_WithTypedArea_ResolvesLocalityToCoordinates()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
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
            textResults:
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
            textResults:
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
            textResults:
            [
                BuildResult(candidates: [farther, closer])
            ],
            nearbyResults:
            [
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
            textResults:
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

    [Fact]
    public async Task SearchAsync_GpsNearMe_UsesHybridRetrieval_AndDedupesByPlaceId()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates:
                [
                    BuildCandidate("text-only", latitude: 53.3572, longitude: -6.4488),
                    BuildCandidate("overlap", latitude: 53.3580, longitude: -6.4500)
                ])
            ],
            nearbyResults:
            [
                BuildResult(candidates:
                [
                    BuildCandidate("overlap", latitude: 53.3580, longitude: -6.4500),
                    BuildCandidate("nearby-only", latitude: 53.3571, longitude: -6.4487)
                ])
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

        Assert.Single(discovery.Requests);
        Assert.Single(discovery.NearbyRequests);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.Items.Select(item => item.PlaceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("places_retrieval:text_search_used", result.Warnings ?? []);
        Assert.Contains("places_retrieval:nearby_search_used", result.Warnings ?? []);
        Assert.Contains("places_retrieval:hybrid_merge_applied", result.Warnings ?? []);
        Assert.Contains("places_retrieval:deduped_overlap_count:1", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_NonGpsQuery_DoesNotUseHybridNearbySearch()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("museum-1")])
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
                Source: "query_locality",
                TypedArea: "Dublin"),
            CancellationToken.None);

        Assert.Single(discovery.Requests);
        Assert.Empty(discovery.NearbyRequests);
        Assert.Contains("places_retrieval:hybrid_not_applicable_non_gps", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_GpsNearMe_NoNearbyTypeMapping_SkipsNearbySearchSafely()
    {
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [BuildCandidate("generic-1")])
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
            "somewhere near me",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3570,
                Longitude: -6.4486,
                RadiusMeters: 2000),
            CancellationToken.None);

        Assert.Single(discovery.Requests);
        Assert.Empty(discovery.NearbyRequests);
        Assert.Contains("places_retrieval:nearby_search_skipped_no_type_mapping", result.Warnings ?? []);
    }

    [Fact]
    public async Task SearchAsync_HybridMerge_AppliesDistanceRankingOnMergedCandidates()
    {
        var farther = BuildCandidate("far-text", latitude: 53.3900, longitude: -6.5000);
        var closerFromNearby = BuildCandidate("near-nearby", latitude: 53.3571, longitude: -6.4487);
        var discovery = new TrackingCompanionDiscoveryService(
            textResults:
            [
                BuildResult(candidates: [farther])
            ],
            nearbyResults:
            [
                BuildResult(candidates: [closerFromNearby])
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

        Assert.Equal("near-nearby", result.Items[0].PlaceId);
        Assert.Contains("places_ranking:gps_distance_applied", result.Warnings ?? []);
    }

    private static GooglePlacesCompanionSearchService CreateSut(
        TrackingCompanionDiscoveryService discovery,
        ICompanionLocalityResolutionService resolver)
    {
        var constraintExtractor = new LocalDiscoveryConstraintExtractor();
        return new GooglePlacesCompanionSearchService(
            discovery,
            constraintExtractor,
            new CompanionPlacesTextQueryBuilder(new CompanionPlacesVocabularyNormalizer()),
            new CompanionPlacesNearbyRequestBuilder(),
            resolver,
            new CompanionNearbyTypeMapper(),
            new CompanionNearbyHybridRetrievalPolicy(),
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
        IReadOnlyList<CompanionPlaceDiscoveryResult> textResults,
        IReadOnlyList<CompanionPlaceDiscoveryResult>? nearbyResults = null) : ICompanionPlaceDiscoveryService
    {
        private int textIndex;
        private int nearbyIndex;
        public List<CompanionPlaceDiscoveryRequest> Requests { get; } = [];
        public List<CompanionNearbyDiscoveryRequest> NearbyRequests { get; } = [];

        public Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
            CompanionPlaceDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var current = textIndex < textResults.Count
                ? textResults[textIndex]
                : textResults[^1];
            textIndex += 1;
            return Task.FromResult(current);
        }

        public Task<CompanionPlaceDiscoveryResult> DiscoverNearbyAsync(
            CompanionNearbyDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NearbyRequests.Add(request);
            var sequence = nearbyResults ?? [];
            if (sequence.Count == 0)
            {
                return Task.FromResult(BuildResult(candidates: []));
            }

            var current = nearbyIndex < sequence.Count
                ? sequence[nearbyIndex]
                : sequence[^1];
            nearbyIndex += 1;
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
