using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record GooglePlacesSearchTextRequest(
    string Query,
    int MaxResultCount,
    string? RegionCode,
    string? LanguageCode,
    double? Latitude,
    double? Longitude,
    int? RadiusMeters,
    string FieldMask,
    string UseCaseTag);

public sealed record GooglePlacesSearchNearbyRequest(
    double Latitude,
    double Longitude,
    int RadiusMeters,
    IReadOnlyList<string> IncludedTypes,
    int MaxResultCount,
    string? RegionCode,
    string? LanguageCode,
    string FieldMask,
    string UseCaseTag);

public sealed record GooglePlacesClientPlace(
    string PlaceId,
    string ResourceName,
    string DisplayName,
    string? PrimaryType,
    string? PrimaryTypeDisplayName,
    IReadOnlyList<string> Types,
    string? NationalPhoneNumber,
    string? FormattedAddress,
    string? ShortFormattedAddress,
    double? Rating,
    int? UserRatingCount,
    string? GoogleMapsUri,
    string? WebsiteUri,
    PlaceOpeningHoursSummary OpeningHours,
    string? BusinessStatus,
    string? PriceLevel,
    string? IconMaskBaseUri,
    string? IconBackgroundColor,
    bool? Takeout,
    bool? Delivery,
    bool? DineIn,
    bool? Reservable,
    bool? ServesBreakfast,
    bool? ServesLunch,
    bool? ServesDinner,
    bool? ServesBeer,
    bool? ServesWine,
    bool? ServesBrunch,
    bool? ServesVegetarianFood,
    bool? OutdoorSeating,
    bool? LiveMusic,
    bool? MenuForChildren,
    bool? ServesCocktails,
    bool? ServesDessert,
    bool? ServesCoffee,
    bool? AllowsDogs,
    bool? Restroom,
    bool? GoodForGroups,
    bool? GoodForWatchingSports,
    PlacePaymentOptionsSummary PaymentOptions,
    PlaceAccessibilitySummary AccessibilityOptions,
    PlaceEditorialSummary EditorialSummary,
    PlaceLocationSummary? Location,
    IReadOnlyList<PlacePhotoSummary>? Photos = null);

public sealed record GooglePlacesClientResult<T>(
    bool Succeeded,
    T? Value,
    bool TimedOut,
    HttpStatusCode? StatusCode,
    string? ErrorCode,
    string? ErrorMessage,
    TimeSpan Elapsed);

public interface IGooglePlacesClient
{
    Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchTextAsync(
        GooglePlacesSearchTextRequest request,
        CancellationToken cancellationToken);

    Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchNearbyAsync(
        GooglePlacesSearchNearbyRequest request,
        CancellationToken cancellationToken);

    Task<GooglePlacesClientResult<GooglePlacesClientPlace?>> GetPlaceDetailsAsync(
        string placeId,
        string fieldMask,
        string useCaseTag,
        CancellationToken cancellationToken);
}

public sealed class GooglePlacesClient(
    HttpClient httpClient,
    IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesClient> logger) : IGooglePlacesClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchTextAsync(
        GooglePlacesSearchTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!placesOptions.Enabled)
        {
            return new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: false,
                Value: [],
                TimedOut: false,
                StatusCode: null,
                ErrorCode: "places_disabled",
                ErrorMessage: "Google Places integration is disabled.",
                Elapsed: TimeSpan.Zero);
        }

        var payload = BuildSearchPayload(request);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/places:searchText")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };
        ApplyHeaders(httpRequest, request.FieldMask);

        return await SendAndParseAsync<IReadOnlyList<GooglePlacesClientPlace>>(
            request.UseCaseTag,
            httpRequest,
            responseParser: ParseSearchResponseAsync,
            fallbackValue: [],
            cancellationToken);
    }

    public async Task<GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>> SearchNearbyAsync(
        GooglePlacesSearchNearbyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!placesOptions.Enabled)
        {
            return new GooglePlacesClientResult<IReadOnlyList<GooglePlacesClientPlace>>(
                Succeeded: false,
                Value: [],
                TimedOut: false,
                StatusCode: null,
                ErrorCode: "places_disabled",
                ErrorMessage: "Google Places integration is disabled.",
                Elapsed: TimeSpan.Zero);
        }

        var payload = BuildNearbyPayload(request);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/places:searchNearby")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };
        ApplyHeaders(httpRequest, request.FieldMask);

        return await SendAndParseAsync<IReadOnlyList<GooglePlacesClientPlace>>(
            request.UseCaseTag,
            httpRequest,
            responseParser: ParseSearchResponseAsync,
            fallbackValue: [],
            cancellationToken);
    }

    public async Task<GooglePlacesClientResult<GooglePlacesClientPlace?>> GetPlaceDetailsAsync(
        string placeId,
        string fieldMask,
        string useCaseTag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return new GooglePlacesClientResult<GooglePlacesClientPlace?>(
                Succeeded: false,
                Value: null,
                TimedOut: false,
                StatusCode: null,
                ErrorCode: "invalid_place_id",
                ErrorMessage: "Place id is required.",
                Elapsed: TimeSpan.Zero);
        }

        if (!placesOptions.Enabled)
        {
            return new GooglePlacesClientResult<GooglePlacesClientPlace?>(
                Succeeded: false,
                Value: null,
                TimedOut: false,
                StatusCode: null,
                ErrorCode: "places_disabled",
                ErrorMessage: "Google Places integration is disabled.",
                Elapsed: TimeSpan.Zero);
        }

        var normalizedPlaceId = NormalizePlaceId(placeId);
        var escapedId = Uri.EscapeDataString(normalizedPlaceId);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/places/{escapedId}");
        ApplyHeaders(httpRequest, fieldMask);

        return await SendAndParseAsync<GooglePlacesClientPlace?>(
            useCaseTag,
            httpRequest,
            responseParser: ParsePlaceDetailsResponseAsync,
            fallbackValue: null,
            cancellationToken);
    }

    private async Task<GooglePlacesClientResult<T>> SendAndParseAsync<T>(
        string useCaseTag,
        HttpRequestMessage request,
        Func<HttpContent, Task<T?>> responseParser,
        T? fallbackValue,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, placesOptions.TimeoutSeconds)));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken);
                var providerErrorCode = ExtractProviderErrorCode(errorPayload);
                logger.LogWarning(
                    "Google Places request failed useCase={UseCase} statusCode={StatusCode} providerErrorCode={ProviderErrorCode}",
                    useCaseTag,
                    (int)response.StatusCode,
                    providerErrorCode ?? "unknown");

                return new GooglePlacesClientResult<T>(
                    Succeeded: false,
                    Value: fallbackValue,
                    TimedOut: false,
                    StatusCode: response.StatusCode,
                    ErrorCode: providerErrorCode ?? "places_provider_error",
                    ErrorMessage: response.ReasonPhrase,
                    Elapsed: stopwatch.Elapsed);
            }

            var parsedValue = await responseParser(response.Content);
            return new GooglePlacesClientResult<T>(
                Succeeded: true,
                Value: parsedValue ?? fallbackValue,
                TimedOut: false,
                StatusCode: response.StatusCode,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Google Places request timed out useCase={UseCase} timeoutSeconds={TimeoutSeconds}",
                useCaseTag,
                placesOptions.TimeoutSeconds);
            return new GooglePlacesClientResult<T>(
                Succeeded: false,
                Value: fallbackValue,
                TimedOut: true,
                StatusCode: null,
                ErrorCode: "places_timeout",
                ErrorMessage: "Google Places request timed out.",
                Elapsed: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Google Places request failed unexpectedly useCase={UseCase}",
                useCaseTag);
            return new GooglePlacesClientResult<T>(
                Succeeded: false,
                Value: fallbackValue,
                TimedOut: false,
                StatusCode: null,
                ErrorCode: "places_unhandled_error",
                ErrorMessage: ex.Message,
                Elapsed: stopwatch.Elapsed);
        }
    }

    private async Task<IReadOnlyList<GooglePlacesClientPlace>?> ParseSearchResponseAsync(
        HttpContent content)
    {
        var response = await content.ReadFromJsonAsync<GooglePlacesSearchTextResponseWire>(SerializerOptions);
        var places = response?.Places ?? [];
        return places
            .Select(MapPlace)
            .Where(place => !string.IsNullOrWhiteSpace(place.PlaceId))
            .ToArray();
    }

    private async Task<GooglePlacesClientPlace?> ParsePlaceDetailsResponseAsync(HttpContent content)
    {
        var place = await content.ReadFromJsonAsync<GooglePlacesPlaceWire>(SerializerOptions);
        return place is null ? null : MapPlace(place);
    }

    private object BuildSearchPayload(GooglePlacesSearchTextRequest request)
    {
        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            return new
            {
                textQuery = request.Query,
                maxResultCount = request.MaxResultCount,
                languageCode = NormalizeOptional(request.LanguageCode)
                               ?? NormalizeOptional(placesOptions.DefaultLanguageCode),
                regionCode = NormalizeOptional(request.RegionCode)
                             ?? NormalizeOptional(placesOptions.DefaultRegionCode),
                locationBias = new
                {
                    circle = new
                    {
                        center = new
                        {
                            latitude = request.Latitude.Value,
                            longitude = request.Longitude.Value
                        },
                        radius = request.RadiusMeters.HasValue
                            ? Math.Max(100, request.RadiusMeters.Value)
                            : Math.Max(100, placesOptions.DefaultSearchRadiusMeters)
                    }
                }
            };
        }

        return new
        {
            textQuery = request.Query,
            maxResultCount = request.MaxResultCount,
            languageCode = NormalizeOptional(request.LanguageCode)
                           ?? NormalizeOptional(placesOptions.DefaultLanguageCode),
            regionCode = NormalizeOptional(request.RegionCode)
                         ?? NormalizeOptional(placesOptions.DefaultRegionCode)
        };
    }

    private object BuildNearbyPayload(GooglePlacesSearchNearbyRequest request)
    {
        var includedTypes = request.IncludedTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        return new
        {
            maxResultCount = request.MaxResultCount,
            languageCode = NormalizeOptional(request.LanguageCode)
                           ?? NormalizeOptional(placesOptions.DefaultLanguageCode),
            regionCode = NormalizeOptional(request.RegionCode)
                         ?? NormalizeOptional(placesOptions.DefaultRegionCode),
            includedTypes,
            rankPreference = "DISTANCE",
            locationRestriction = new
            {
                circle = new
                {
                    center = new
                    {
                        latitude = request.Latitude,
                        longitude = request.Longitude
                    },
                    radius = Math.Max(100, request.RadiusMeters)
                }
            }
        };
    }

    private void ApplyHeaders(HttpRequestMessage request, string fieldMask)
    {
        request.Headers.Add("X-Goog-Api-Key", placesOptions.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", fieldMask);
    }

    private static GooglePlacesClientPlace MapPlace(GooglePlacesPlaceWire place)
    {
        var placeId = !string.IsNullOrWhiteSpace(place.Id)
            ? place.Id.Trim()
            : NormalizePlaceId(place.Name);

        return new GooglePlacesClientPlace(
            PlaceId: placeId,
            ResourceName: place.Name?.Trim() ?? string.Empty,
            DisplayName: place.DisplayName?.Text?.Trim() ?? string.Empty,
            PrimaryType: place.PrimaryType?.Trim(),
            PrimaryTypeDisplayName: place.PrimaryTypeDisplayName?.Text?.Trim(),
            Types: place.Types?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [],
            NationalPhoneNumber: NormalizeOptional(place.NationalPhoneNumber),
            FormattedAddress: NormalizeOptional(place.FormattedAddress),
            ShortFormattedAddress: NormalizeOptional(place.ShortFormattedAddress),
            Rating: place.Rating,
            UserRatingCount: place.UserRatingCount,
            GoogleMapsUri: NormalizeOptional(place.GoogleMapsUri),
            WebsiteUri: NormalizeOptional(place.WebsiteUri),
            OpeningHours: new PlaceOpeningHoursSummary(
                OpenNow: place.RegularOpeningHours?.OpenNow,
                WeekdayDescriptions: place.RegularOpeningHours?.WeekdayDescriptions
                                     ?.Where(value => !string.IsNullOrWhiteSpace(value))
                                     .ToArray()
                                     ?? [],
                NextOpenTimeUtc: place.RegularOpeningHours?.NextOpenTime),
            BusinessStatus: NormalizeOptional(place.BusinessStatus),
            PriceLevel: NormalizeOptional(place.PriceLevel),
            IconMaskBaseUri: NormalizeOptional(place.IconMaskBaseUri),
            IconBackgroundColor: NormalizeOptional(place.IconBackgroundColor),
            Takeout: place.Takeout,
            Delivery: place.Delivery,
            DineIn: place.DineIn,
            Reservable: place.Reservable,
            ServesBreakfast: place.ServesBreakfast,
            ServesLunch: place.ServesLunch,
            ServesDinner: place.ServesDinner,
            ServesBeer: place.ServesBeer,
            ServesWine: place.ServesWine,
            ServesBrunch: place.ServesBrunch,
            ServesVegetarianFood: place.ServesVegetarianFood,
            OutdoorSeating: place.OutdoorSeating,
            LiveMusic: place.LiveMusic,
            MenuForChildren: place.MenuForChildren,
            ServesCocktails: place.ServesCocktails,
            ServesDessert: place.ServesDessert,
            ServesCoffee: place.ServesCoffee,
            AllowsDogs: place.AllowsDogs,
            Restroom: place.Restroom,
            GoodForGroups: place.GoodForGroups,
            GoodForWatchingSports: place.GoodForWatchingSports,
            PaymentOptions: new PlacePaymentOptionsSummary(
                AcceptsCreditCards: place.PaymentOptions?.AcceptsCreditCards,
                AcceptsDebitCards: place.PaymentOptions?.AcceptsDebitCards,
                AcceptsCashOnly: place.PaymentOptions?.AcceptsCashOnly,
                AcceptsNfc: place.PaymentOptions?.AcceptsNfc),
            AccessibilityOptions: new PlaceAccessibilitySummary(
                WheelchairAccessibleParking: place.AccessibilityOptions?.WheelchairAccessibleParking,
                WheelchairAccessibleEntrance: place.AccessibilityOptions?.WheelchairAccessibleEntrance,
                WheelchairAccessibleRestroom: place.AccessibilityOptions?.WheelchairAccessibleRestroom,
                WheelchairAccessibleSeating: place.AccessibilityOptions?.WheelchairAccessibleSeating),
            EditorialSummary: new PlaceEditorialSummary(
                Text: NormalizeOptional(place.EditorialSummary?.Text),
                LanguageCode: NormalizeOptional(place.EditorialSummary?.LanguageCode)),
            Location: place.Location?.Latitude.HasValue == true
                      && place.Location.Longitude.HasValue
                ? new PlaceLocationSummary(
                    place.Location.Latitude.Value,
                    place.Location.Longitude.Value)
                : null,
            Photos: place.Photos?
                .Where(photo => !string.IsNullOrWhiteSpace(photo.Name))
                .Select(photo => new PlacePhotoSummary(
                    photo.Name!.Trim(),
                    photo.WidthPx,
                    photo.HeightPx))
                .Take(8)
                .ToArray()
                ?? []);
    }

    private static string? ExtractProviderErrorCode(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String)
            {
                return status.GetString();
            }
        }
        catch
        {
            // Ignore invalid payloads for error parsing.
        }

        return null;
    }

    private static string NormalizePlaceId(string? placeIdOrResourceName)
    {
        if (string.IsNullOrWhiteSpace(placeIdOrResourceName))
        {
            return string.Empty;
        }

        var trimmed = placeIdOrResourceName.Trim();
        const string prefix = "places/";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal sealed record GooglePlacesSearchTextResponseWire(
    [property: JsonPropertyName("places")] IReadOnlyList<GooglePlacesPlaceWire>? Places);

internal sealed record GooglePlacesPlaceWire(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("displayName")] GooglePlacesLocalizedTextWire? DisplayName,
    [property: JsonPropertyName("primaryType")] string? PrimaryType,
    [property: JsonPropertyName("primaryTypeDisplayName")] GooglePlacesLocalizedTextWire? PrimaryTypeDisplayName,
    [property: JsonPropertyName("types")] IReadOnlyList<string>? Types,
    [property: JsonPropertyName("nationalPhoneNumber")] string? NationalPhoneNumber,
    [property: JsonPropertyName("formattedAddress")] string? FormattedAddress,
    [property: JsonPropertyName("shortFormattedAddress")] string? ShortFormattedAddress,
    [property: JsonPropertyName("rating")] double? Rating,
    [property: JsonPropertyName("userRatingCount")] int? UserRatingCount,
    [property: JsonPropertyName("googleMapsUri")] string? GoogleMapsUri,
    [property: JsonPropertyName("websiteUri")] string? WebsiteUri,
    [property: JsonPropertyName("regularOpeningHours")] GooglePlacesOpeningHoursWire? RegularOpeningHours,
    [property: JsonPropertyName("businessStatus")] string? BusinessStatus,
    [property: JsonPropertyName("priceLevel")] string? PriceLevel,
    [property: JsonPropertyName("iconMaskBaseUri")] string? IconMaskBaseUri,
    [property: JsonPropertyName("iconBackgroundColor")] string? IconBackgroundColor,
    [property: JsonPropertyName("takeout")] bool? Takeout,
    [property: JsonPropertyName("delivery")] bool? Delivery,
    [property: JsonPropertyName("dineIn")] bool? DineIn,
    [property: JsonPropertyName("reservable")] bool? Reservable,
    [property: JsonPropertyName("servesBreakfast")] bool? ServesBreakfast,
    [property: JsonPropertyName("servesLunch")] bool? ServesLunch,
    [property: JsonPropertyName("servesDinner")] bool? ServesDinner,
    [property: JsonPropertyName("servesBeer")] bool? ServesBeer,
    [property: JsonPropertyName("servesWine")] bool? ServesWine,
    [property: JsonPropertyName("servesBrunch")] bool? ServesBrunch,
    [property: JsonPropertyName("servesVegetarianFood")] bool? ServesVegetarianFood,
    [property: JsonPropertyName("outdoorSeating")] bool? OutdoorSeating,
    [property: JsonPropertyName("liveMusic")] bool? LiveMusic,
    [property: JsonPropertyName("menuForChildren")] bool? MenuForChildren,
    [property: JsonPropertyName("servesCocktails")] bool? ServesCocktails,
    [property: JsonPropertyName("servesDessert")] bool? ServesDessert,
    [property: JsonPropertyName("servesCoffee")] bool? ServesCoffee,
    [property: JsonPropertyName("allowsDogs")] bool? AllowsDogs,
    [property: JsonPropertyName("restroom")] bool? Restroom,
    [property: JsonPropertyName("goodForGroups")] bool? GoodForGroups,
    [property: JsonPropertyName("goodForWatchingSports")] bool? GoodForWatchingSports,
    [property: JsonPropertyName("paymentOptions")] GooglePlacesPaymentOptionsWire? PaymentOptions,
    [property: JsonPropertyName("accessibilityOptions")] GooglePlacesAccessibilityOptionsWire? AccessibilityOptions,
    [property: JsonPropertyName("editorialSummary")] GooglePlacesLocalizedTextWire? EditorialSummary,
    [property: JsonPropertyName("photos")] IReadOnlyList<GooglePlacesPhotoWire>? Photos,
    [property: JsonPropertyName("location")] GooglePlacesLocationWire? Location);

internal sealed record GooglePlacesLocalizedTextWire(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("languageCode")] string? LanguageCode);

internal sealed record GooglePlacesOpeningHoursWire(
    [property: JsonPropertyName("openNow")] bool? OpenNow,
    [property: JsonPropertyName("weekdayDescriptions")] IReadOnlyList<string>? WeekdayDescriptions,
    [property: JsonPropertyName("nextOpenTime")] DateTimeOffset? NextOpenTime);

internal sealed record GooglePlacesPaymentOptionsWire(
    [property: JsonPropertyName("acceptsCreditCards")] bool? AcceptsCreditCards,
    [property: JsonPropertyName("acceptsDebitCards")] bool? AcceptsDebitCards,
    [property: JsonPropertyName("acceptsCashOnly")] bool? AcceptsCashOnly,
    [property: JsonPropertyName("acceptsNfc")] bool? AcceptsNfc);

internal sealed record GooglePlacesAccessibilityOptionsWire(
    [property: JsonPropertyName("wheelchairAccessibleParking")] bool? WheelchairAccessibleParking,
    [property: JsonPropertyName("wheelchairAccessibleEntrance")] bool? WheelchairAccessibleEntrance,
    [property: JsonPropertyName("wheelchairAccessibleRestroom")] bool? WheelchairAccessibleRestroom,
    [property: JsonPropertyName("wheelchairAccessibleSeating")] bool? WheelchairAccessibleSeating);

internal sealed record GooglePlacesLocationWire(
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude);

internal sealed record GooglePlacesPhotoWire(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("widthPx")] int? WidthPx,
    [property: JsonPropertyName("heightPx")] int? HeightPx);
