using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlaceDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_CapsResultsToEight_AndUsesCompanionFieldMask()
    {
        var fakeClient = new FakeGooglePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: true,
                Value: Enumerable.Range(1, 12).Select(BuildPlace).ToArray(),
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(25))
        };
        var fieldMasks = new GooglePlacesFieldMaskProvider();
        var options = CreateOptions(maxCompanionCandidates: 12);
        var sut = new CompanionPlaceDiscoveryService(
            fakeClient,
            fieldMasks,
            new InMemoryGooglePlacesCache(),
            new GooglePlacesCacheKeyBuilder(),
            Options.Create(options),
            NullLogger<CompanionPlaceDiscoveryService>.Instance);

        var result = await sut.DiscoverAsync(
            new CompanionPlaceDiscoveryRequest(
                Query: "coffee shops",
                CountryCode: "IE"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(8, result.Candidates.Count);
        Assert.Equal(8, result.Metadata.RequestedCandidateCount);
        Assert.Equal("companion_discovery_v1", result.Metadata.FieldMaskVariant);
        Assert.Equal(fieldMasks.CompanionDiscoverySearchMask, Assert.Single(fakeClient.SearchRequests).FieldMask);
    }

    [Fact]
    public async Task DiscoverAsync_UsesCache_ForIdenticalRequest()
    {
        var fakeClient = new FakeGooglePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: true,
                Value: [BuildPlace(1)],
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(10))
        };
        var sut = new CompanionPlaceDiscoveryService(
            fakeClient,
            new GooglePlacesFieldMaskProvider(),
            new InMemoryGooglePlacesCache(),
            new GooglePlacesCacheKeyBuilder(),
            Options.Create(CreateOptions()),
            NullLogger<CompanionPlaceDiscoveryService>.Instance);
        var request = new CompanionPlaceDiscoveryRequest(
            Query: "pizza",
            CountryCode: "IE");

        var first = await sut.DiscoverAsync(request, CancellationToken.None);
        var second = await sut.DiscoverAsync(request, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(first.Metadata.FromCache);
        Assert.True(second.Metadata.FromCache);
        Assert.Single(fakeClient.SearchRequests);
    }

    [Fact]
    public async Task DiscoverAsync_ProviderFailure_ReturnsSafeFailure_AndCachesFailure()
    {
        var fakeClient = new FakeGooglePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: false,
                Value: [],
                TimedOut: true,
                StatusCode: null,
                ErrorCode: "places_timeout",
                ErrorMessage: "timeout",
                Elapsed: TimeSpan.FromSeconds(1))
        };
        var sut = new CompanionPlaceDiscoveryService(
            fakeClient,
            new GooglePlacesFieldMaskProvider(),
            new InMemoryGooglePlacesCache(),
            new GooglePlacesCacheKeyBuilder(),
            Options.Create(CreateOptions()),
            NullLogger<CompanionPlaceDiscoveryService>.Instance);
        var request = new CompanionPlaceDiscoveryRequest(
            Query: "late night food",
            CountryCode: "IE");

        var first = await sut.DiscoverAsync(request, CancellationToken.None);
        var second = await sut.DiscoverAsync(request, CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Contains("places_provider_unavailable", first.Warnings);
        Assert.Contains("places_timeout", first.Warnings);
        Assert.False(first.Metadata.FromCache);
        Assert.False(second.Succeeded);
        Assert.True(second.Metadata.FromCache);
        Assert.Single(fakeClient.SearchRequests);
    }

    [Fact]
    public async Task MerchantLookup_UsesDedicatedPathAndMask()
    {
        var fakeClient = new FakeGooglePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: true,
                Value: Enumerable.Range(1, 7).Select(BuildPlace).ToArray(),
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(30))
        };
        var fieldMasks = new GooglePlacesFieldMaskProvider();
        var options = CreateOptions(maxMerchantLookupCandidates: 4);
        var sut = new MerchantPlaceLookupService(
            fakeClient,
            fieldMasks,
            new InMemoryGooglePlacesCache(),
            new GooglePlacesCacheKeyBuilder(),
            Options.Create(options),
            NullLogger<MerchantPlaceLookupService>.Instance);

        var result = await sut.LookupAsync(
            new MerchantPlaceLookupRequest(
                MerchantDescriptor: "Acme Bistro",
                CountryCode: "IE",
                MaxCandidates: 10),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Matches.Count);
        Assert.Equal("merchant_lookup_v1", result.Metadata.FieldMaskVariant);
        var searchRequest = Assert.Single(fakeClient.SearchRequests);
        Assert.Equal("merchant_lookup", searchRequest.UseCaseTag);
        Assert.Equal(fieldMasks.MerchantLookupSearchMask, searchRequest.FieldMask);
        Assert.Equal(4, searchRequest.MaxResultCount);
    }

    [Fact]
    public async Task PlaceDetailsService_UsesCache_AfterFirstFetch()
    {
        var fakeClient = new FakeGooglePlacesClient
        {
            PlaceDetailsResult = new GooglePlacesClientResult<GooglePlacesClientPlace?>(
                Succeeded: true,
                Value: BuildPlace(42),
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(20))
        };
        var sut = new GooglePlacesPlaceDetailsService(
            fakeClient,
            new GooglePlacesFieldMaskProvider(),
            new InMemoryGooglePlacesCache(),
            new GooglePlacesCacheKeyBuilder(),
            Options.Create(CreateOptions()),
            NullLogger<GooglePlacesPlaceDetailsService>.Instance);

        var first = await sut.GetDetailsAsync("place-42", CancellationToken.None);
        var second = await sut.GetDetailsAsync("place-42", CancellationToken.None);

        Assert.Equal("Place 42", first.Name);
        Assert.Equal("Place 42", second.Name);
        Assert.Single(fakeClient.PlaceDetailsRequests);
    }

    [Fact]
    public async Task DiscoverNearbyAsync_UsesNearbyMaskAndCache()
    {
        var fakeClient = new FakeGooglePlacesClient
        {
            NearbySearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: true,
                Value: [BuildPlace(7)],
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(18))
        };
        var fieldMasks = new GooglePlacesFieldMaskProvider();
        var sut = new CompanionPlaceDiscoveryService(
            fakeClient,
            fieldMasks,
            new InMemoryGooglePlacesCache(),
            new GooglePlacesCacheKeyBuilder(),
            Options.Create(CreateOptions()),
            NullLogger<CompanionPlaceDiscoveryService>.Instance);
        var request = new CompanionNearbyDiscoveryRequest(
            Latitude: 53.3570,
            Longitude: -6.4486,
            RadiusMeters: 1500,
            IncludedTypes: ["cafe"]);

        var first = await sut.DiscoverNearbyAsync(request, CancellationToken.None);
        var second = await sut.DiscoverNearbyAsync(request, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal("companion_nearby_v1", first.Metadata.FieldMaskVariant);
        Assert.False(first.Metadata.FromCache);
        Assert.True(second.Metadata.FromCache);
        var nearbyRequest = Assert.Single(fakeClient.NearbyRequests);
        Assert.Equal(fieldMasks.CompanionNearbySearchMask, nearbyRequest.FieldMask);
    }

    private static GooglePlacesOptions CreateOptions(
        int maxCompanionCandidates = 8,
        int maxMerchantLookupCandidates = 5)
    {
        return new GooglePlacesOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            MaxCompanionCandidates = maxCompanionCandidates,
            MaxMerchantLookupCandidates = maxMerchantLookupCandidates,
            CompanionCacheTtlSeconds = 300,
            MerchantLookupCacheTtlSeconds = 300,
            PlaceDetailsCacheTtlSeconds = 300,
            FailureCacheTtlSeconds = 30
        };
    }

    private static GooglePlacesClientPlace BuildPlace(int index)
    {
        return new GooglePlacesClientPlace(
            PlaceId: $"place-{index}",
            ResourceName: $"places/place-{index}",
            DisplayName: $"Place {index}",
            PrimaryType: "restaurant",
            PrimaryTypeDisplayName: "Restaurant",
            Types: ["restaurant", "food"],
            NationalPhoneNumber: "+3531234567",
            FormattedAddress: $"Address {index}",
            ShortFormattedAddress: $"Short {index}",
            Rating: 4.2,
            UserRatingCount: 99 + index,
            GoogleMapsUri: $"https://maps.google.com/?q={index}",
            WebsiteUri: $"https://place{index}.example.com",
            OpeningHours: new PlaceOpeningHoursSummary(true, ["Monday: 9:00 AM - 5:00 PM"], null),
            BusinessStatus: "OPERATIONAL",
            PriceLevel: "PRICE_LEVEL_MODERATE",
            IconMaskBaseUri: "https://maps.gstatic.com/icon",
            IconBackgroundColor: "#FFAA00",
            Takeout: true,
            Delivery: false,
            DineIn: true,
            Reservable: true,
            ServesBreakfast: true,
            ServesLunch: true,
            ServesDinner: true,
            ServesBeer: true,
            ServesWine: true,
            ServesBrunch: true,
            ServesVegetarianFood: true,
            OutdoorSeating: true,
            LiveMusic: false,
            MenuForChildren: true,
            ServesCocktails: true,
            ServesDessert: true,
            ServesCoffee: true,
            AllowsDogs: false,
            Restroom: true,
            GoodForGroups: true,
            GoodForWatchingSports: false,
            PaymentOptions: new PlacePaymentOptionsSummary(
                AcceptsCreditCards: true,
                AcceptsDebitCards: true,
                AcceptsCashOnly: false,
                AcceptsNfc: true),
            AccessibilityOptions: new PlaceAccessibilitySummary(
                WheelchairAccessibleParking: true,
                WheelchairAccessibleEntrance: true,
                WheelchairAccessibleRestroom: true,
                WheelchairAccessibleSeating: true),
            EditorialSummary: new PlaceEditorialSummary(
                Text: "Local favorite spot",
                LanguageCode: "en"),
            Location: new PlaceLocationSummary(53.3498, -6.2603));
    }

    private sealed class FakeGooglePlacesClient : IGooglePlacesClient
    {
        public List<GooglePlacesSearchTextRequest> SearchRequests { get; } = [];
        public List<GooglePlacesSearchNearbyRequest> NearbyRequests { get; } = [];
        public List<(string PlaceId, string FieldMask, string UseCaseTag)> PlaceDetailsRequests { get; } = [];

        public GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>> SearchResult { get; set; } =
            new(
                Succeeded: true,
                Value: [],
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.Zero);

        public GooglePlacesClientResult<GooglePlacesClientPlace?> PlaceDetailsResult { get; set; } =
            new(
                Succeeded: true,
                Value: null,
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.Zero);

        public GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>> NearbySearchResult { get; set; } =
            new(
                Succeeded: true,
                Value: [],
                TimedOut: false,
                StatusCode: System.Net.HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.Zero);

        public Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchTextAsync(
            GooglePlacesSearchTextRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchRequests.Add(request);
            return Task.FromResult(SearchResult);
        }

        public Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchNearbyAsync(
            GooglePlacesSearchNearbyRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NearbyRequests.Add(request);
            return Task.FromResult(NearbySearchResult);
        }

        public Task<GooglePlacesClientResult<GooglePlacesClientPlace?>> GetPlaceDetailsAsync(
            string placeId,
            string fieldMask,
            string useCaseTag,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlaceDetailsRequests.Add((placeId, fieldMask, useCaseTag));
            return Task.FromResult(PlaceDetailsResult);
        }
    }
}
