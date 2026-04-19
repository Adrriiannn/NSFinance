using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesClientTests
{
    [Fact]
    public async Task SearchTextAsync_MapsRichPayload_AndSendsExplicitHeaders()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/places:searchText", request.RequestUri?.AbsolutePath);
            Assert.Equal("test-api-key", request.Headers.GetValues("X-Goog-Api-Key").Single());
            Assert.Equal(
                "places.displayName,places.paymentOptions",
                request.Headers.GetValues("X-Goog-FieldMask").Single());

            var json = """
                {
                  "places": [
                    {
                      "id": "abc123",
                      "name": "places/abc123",
                      "displayName": { "text": "Cafe North", "languageCode": "en" },
                      "primaryType": "cafe",
                      "primaryTypeDisplayName": { "text": "Cafe", "languageCode": "en" },
                      "types": ["cafe", "food"],
                      "nationalPhoneNumber": "+3531234567",
                      "formattedAddress": "1 Main Street, Dublin",
                      "shortFormattedAddress": "1 Main St",
                      "rating": 4.6,
                      "userRatingCount": 211,
                      "googleMapsUri": "https://maps.google.com/?q=abc123",
                      "websiteUri": "https://cafenorth.example.com",
                      "regularOpeningHours": {
                        "openNow": true,
                        "weekdayDescriptions": ["Monday: 8:00 AM - 6:00 PM"],
                        "nextOpenTime": "2026-04-20T08:00:00Z"
                      },
                      "businessStatus": "OPERATIONAL",
                      "priceLevel": "PRICE_LEVEL_MODERATE",
                      "iconMaskBaseUri": "https://maps.gstatic.com/icon",
                      "iconBackgroundColor": "#AABBCC",
                      "takeout": true,
                      "delivery": false,
                      "dineIn": true,
                      "reservable": false,
                      "servesBreakfast": true,
                      "servesLunch": true,
                      "servesDinner": false,
                      "servesBeer": false,
                      "servesWine": false,
                      "servesBrunch": true,
                      "servesVegetarianFood": true,
                      "outdoorSeating": true,
                      "liveMusic": false,
                      "menuForChildren": false,
                      "servesCocktails": false,
                      "servesDessert": true,
                      "servesCoffee": true,
                      "allowsDogs": true,
                      "restroom": true,
                      "goodForGroups": true,
                      "goodForWatchingSports": false,
                      "paymentOptions": {
                        "acceptsCreditCards": true,
                        "acceptsDebitCards": true,
                        "acceptsCashOnly": false,
                        "acceptsNfc": true
                      },
                      "accessibilityOptions": {
                        "wheelchairAccessibleParking": true,
                        "wheelchairAccessibleEntrance": true,
                        "wheelchairAccessibleRestroom": true,
                        "wheelchairAccessibleSeating": true
                      },
                      "editorialSummary": {
                        "text": "Neighborhood favorite.",
                        "languageCode": "en"
                      },
                      "location": {
                        "latitude": 53.3498,
                        "longitude": -6.2603
                      }
                    }
                  ]
                }
                """;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = new GooglePlacesClient(
            httpClient,
            Options.Create(CreateOptions(timeoutSeconds: 8)),
            NullLogger<GooglePlacesClient>.Instance);

        var result = await sut.SearchTextAsync(
            new GooglePlacesSearchTextRequest(
                Query: "cafe dublin",
                MaxResultCount: 8,
                RegionCode: "IE",
                LanguageCode: "en",
                Latitude: 53.3498,
                Longitude: -6.2603,
                RadiusMeters: 1500,
                FieldMask: "places.displayName,places.paymentOptions",
                UseCaseTag: "companion_discovery"),
            CancellationToken.None);

        var place = Assert.Single(result.Value ?? []);
        Assert.True(result.Succeeded);
        Assert.Equal("abc123", place.PlaceId);
        Assert.Equal("places/abc123", place.ResourceName);
        Assert.Equal("Cafe North", place.DisplayName);
        Assert.Equal("cafe", place.PrimaryType);
        Assert.True(place.PaymentOptions.AcceptsCreditCards);
        Assert.True(place.AccessibilityOptions.WheelchairAccessibleEntrance);
        Assert.True(place.OpeningHours.OpenNow);
        Assert.Equal("Neighborhood favorite.", place.EditorialSummary.Text);
        Assert.NotNull(place.Location);
        Assert.Equal(53.3498, place.Location!.Latitude, 4);
    }

    [Fact]
    public async Task SearchNearbyAsync_UsesNearbyEndpoint_AndDistancePreference()
    {
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/places:searchNearby", request.RequestUri?.AbsolutePath);
            Assert.Equal("test-api-key", request.Headers.GetValues("X-Goog-Api-Key").Single());
            Assert.Equal(
                "places.displayName,places.location",
                request.Headers.GetValues("X-Goog-FieldMask").Single());

            var payload = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"includedTypes\":[\"cafe\",\"restaurant\"]", payload, StringComparison.Ordinal);
            Assert.Contains("\"rankPreference\":\"DISTANCE\"", payload, StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "places": [
                        {
                          "id": "nearby-1",
                          "name": "places/nearby-1",
                          "displayName": { "text": "Nearby Cafe", "languageCode": "en" },
                          "location": { "latitude": 53.35, "longitude": -6.26 }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = new GooglePlacesClient(
            httpClient,
            Options.Create(CreateOptions(timeoutSeconds: 8)),
            NullLogger<GooglePlacesClient>.Instance);

        var result = await sut.SearchNearbyAsync(
            new GooglePlacesSearchNearbyRequest(
                Latitude: 53.3498,
                Longitude: -6.2603,
                RadiusMeters: 1800,
                IncludedTypes: ["cafe", "restaurant"],
                MaxResultCount: 6,
                RegionCode: "IE",
                LanguageCode: "en",
                FieldMask: "places.displayName,places.location",
                UseCaseTag: "companion_nearby"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value ?? []);
    }

    [Fact]
    public async Task SearchTextAsync_MapsProviderErrorCode()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var json = """
                {
                  "error": {
                    "code": 429,
                    "message": "Quota exceeded",
                    "status": "RESOURCE_EXHAUSTED"
                  }
                }
                """;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = new GooglePlacesClient(
            httpClient,
            Options.Create(CreateOptions(timeoutSeconds: 8)),
            NullLogger<GooglePlacesClient>.Instance);

        var result = await sut.SearchTextAsync(
            new GooglePlacesSearchTextRequest(
                Query: "test",
                MaxResultCount: 3,
                RegionCode: "IE",
                LanguageCode: "en",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                FieldMask: "places.displayName",
                UseCaseTag: "merchant_lookup"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("RESOURCE_EXHAUSTED", result.ErrorCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
    }

    [Fact]
    public async Task SearchTextAsync_TimesOut_ReturnsTimedOutResult()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"places":[]}""", Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = new GooglePlacesClient(
            httpClient,
            Options.Create(CreateOptions(timeoutSeconds: 1)),
            NullLogger<GooglePlacesClient>.Instance);

        var result = await sut.SearchTextAsync(
            new GooglePlacesSearchTextRequest(
                Query: "timeout-test",
                MaxResultCount: 2,
                RegionCode: "IE",
                LanguageCode: "en",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                FieldMask: "places.displayName",
                UseCaseTag: "companion_discovery"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal("places_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task GetPlaceDetailsAsync_NormalizesPlaceIdFromResourceName_WhenIdMissing()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/places/abc123", request.RequestUri?.AbsolutePath);

            var json = """
                {
                  "name": "places/abc123",
                  "displayName": { "text": "Cafe North", "languageCode": "en" }
                }
                """;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = new GooglePlacesClient(
            httpClient,
            Options.Create(CreateOptions(timeoutSeconds: 8)),
            NullLogger<GooglePlacesClient>.Instance);

        var result = await sut.GetPlaceDetailsAsync(
            placeId: "places/abc123",
            fieldMask: "displayName,name",
            useCaseTag: "place_details",
            cancellationToken: CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("abc123", result.Value!.PlaceId);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://places.googleapis.com")
        };
    }

    private static GooglePlacesOptions CreateOptions(int timeoutSeconds)
    {
        return new GooglePlacesOptions
        {
            Enabled = true,
            ApiKey = "test-api-key",
            TimeoutSeconds = timeoutSeconds,
            BaseUrl = "https://places.googleapis.com"
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handlerFunc(request, cancellationToken);
        }
    }
}
