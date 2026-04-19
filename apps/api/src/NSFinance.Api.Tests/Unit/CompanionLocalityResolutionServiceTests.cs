using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionLocalityResolutionServiceTests
{
    [Fact]
    public async Task ResolveAsync_ProviderResultWithCoordinates_ReturnsResolvedCoordinates()
    {
        var client = new FakePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: true,
                Value:
                [
                    BuildPlace(
                        placeId: "place-1",
                        displayName: "Dublin",
                        types: ["locality"],
                        latitude: 53.3498,
                        longitude: -6.2603)
                ],
                TimedOut: false,
                StatusCode: HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(15))
        };
        var sut = CreateSut(client);

        var result = await sut.ResolveAsync("Dublin", "IE", null, CancellationToken.None);

        Assert.True(result.HasCoordinates);
        Assert.Equal(53.3498, result.Latitude);
        Assert.Equal(-6.2603, result.Longitude);
        Assert.Equal("locality_resolution_succeeded", result.ReasonCode);
    }

    [Fact]
    public async Task ResolveAsync_UsesCacheForRepeatedLocality()
    {
        var client = new FakePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: true,
                Value:
                [
                    BuildPlace(
                        placeId: "place-1",
                        displayName: "Lucan",
                        types: ["locality"],
                        latitude: 53.3570,
                        longitude: -6.4486)
                ],
                TimedOut: false,
                StatusCode: HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.FromMilliseconds(10))
        };
        var sut = CreateSut(client);

        var first = await sut.ResolveAsync("Lucan", "IE", null, CancellationToken.None);
        var second = await sut.ResolveAsync("Lucan", "IE", null, CancellationToken.None);

        Assert.True(first.HasCoordinates);
        Assert.True(second.HasCoordinates);
        Assert.Single(client.SearchRequests);
    }

    [Fact]
    public async Task ResolveAsync_ProviderFailure_ReturnsFailureReason()
    {
        var client = new FakePlacesClient
        {
            SearchResult = new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: false,
                Value: [],
                TimedOut: false,
                StatusCode: HttpStatusCode.BadGateway,
                ErrorCode: "places_provider_error",
                ErrorMessage: "provider failed",
                Elapsed: TimeSpan.FromMilliseconds(10))
        };
        var sut = CreateSut(client);

        var result = await sut.ResolveAsync("Dublin", "IE", null, CancellationToken.None);

        Assert.False(result.HasCoordinates);
        Assert.Equal("locality_resolution_provider_failure", result.ReasonCode);
    }

    private static CompanionLocalityResolutionService CreateSut(FakePlacesClient client)
    {
        return new CompanionLocalityResolutionService(
            client,
            new InMemoryGooglePlacesCache(),
            Options.Create(new GooglePlacesOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                CompanionCacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 60
            }),
            NullLogger<CompanionLocalityResolutionService>.Instance);
    }

    private static GooglePlacesClientPlace BuildPlace(
        string placeId,
        string displayName,
        IReadOnlyList<string> types,
        double latitude,
        double longitude)
    {
        return new GooglePlacesClientPlace(
            PlaceId: placeId,
            ResourceName: $"places/{placeId}",
            DisplayName: displayName,
            PrimaryType: types.FirstOrDefault(),
            PrimaryTypeDisplayName: types.FirstOrDefault(),
            Types: types,
            NationalPhoneNumber: null,
            FormattedAddress: displayName,
            ShortFormattedAddress: displayName,
            Rating: null,
            UserRatingCount: null,
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

    private sealed class FakePlacesClient : IGooglePlacesClient
    {
        public GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>> SearchResult { get; set; } =
            new(
                Succeeded: true,
                Value: [],
                TimedOut: false,
                StatusCode: HttpStatusCode.OK,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.Zero);

        public List<GooglePlacesSearchTextRequest> SearchRequests { get; } = [];

        public Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchTextAsync(
            GooglePlacesSearchTextRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchRequests.Add(request);
            return Task.FromResult(SearchResult);
        }

        public Task<GooglePlacesClientResult<GooglePlacesClientPlace?>> GetPlaceDetailsAsync(
            string placeId,
            string fieldMask,
            string useCaseTag,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new GooglePlacesClientResult<GooglePlacesClientPlace?>(
                    Succeeded: false,
                    Value: null,
                    TimedOut: false,
                    StatusCode: HttpStatusCode.BadRequest,
                    ErrorCode: "not_used",
                    ErrorMessage: "not_used",
                    Elapsed: TimeSpan.Zero));
        }
    }
}
