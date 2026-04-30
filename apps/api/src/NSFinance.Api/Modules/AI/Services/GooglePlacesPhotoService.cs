using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IGooglePlacesPhotoService
{
    string? BuildAppPhotoUrl(
        string? photoResourceName,
        int? maxWidthPx = null,
        int? maxHeightPx = null);

    Task<GooglePlacesPhotoMediaResult> ResolvePhotoAsync(
        GooglePlacesPhotoMediaRequest request,
        CancellationToken cancellationToken);
}

public sealed record GooglePlacesPhotoMediaRequest(
    string PhotoResourceName,
    int? MaxWidthPx,
    int? MaxHeightPx,
    bool SkipHttpRedirect);

public sealed record GooglePlacesPhotoMediaResult(
    bool Succeeded,
    string? RedirectUri,
    byte[]? Content,
    string? ContentType,
    string? ErrorCode,
    string? ErrorMessage,
    TimeSpan Elapsed,
    bool TimedOut = false,
    HttpStatusCode? StatusCode = null);

public sealed class GooglePlacesPhotoService(
    HttpClient httpClient,
    IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesPhotoService> logger) : IGooglePlacesPhotoService
{
    private const int DefaultMaxWidthPx = 900;
    private const int DefaultMaxHeightPx = 520;
    private const int MinDimensionPx = 1;
    private const int MaxDimensionPx = 4800;
    private const int MaxPhotoNameLength = 512;

    private readonly GooglePlacesOptions placesOptions = options.Value;

    public string? BuildAppPhotoUrl(
        string? photoResourceName,
        int? maxWidthPx = null,
        int? maxHeightPx = null)
    {
        if (!IsValidPhotoResourceName(photoResourceName))
        {
            return null;
        }

        var width = ClampDimension(maxWidthPx, DefaultMaxWidthPx);
        var height = ClampDimension(maxHeightPx, DefaultMaxHeightPx);
        var relativeUrl =
            $"/api/ai/places/photos?name={Uri.EscapeDataString(photoResourceName!.Trim())}&maxWidthPx={width}&maxHeightPx={height}";

        if (string.IsNullOrWhiteSpace(placesOptions.PlacesPhotoPublicBaseUrl))
        {
            return relativeUrl;
        }

        return new Uri(
            new Uri(placesOptions.PlacesPhotoPublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            relativeUrl.TrimStart('/')).ToString();
    }

    public async Task<GooglePlacesPhotoMediaResult> ResolvePhotoAsync(
        GooglePlacesPhotoMediaRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();

        if (!IsValidPhotoResourceName(request.PhotoResourceName))
        {
            logger.LogWarning("Google Places photo rejected invalid resource name.");
            return Failure(
                "invalid_photo_name",
                "Google Places photo resource name is invalid.",
                StatusCodes.Status400BadRequest,
                stopwatch.Elapsed);
        }

        if (!placesOptions.Enabled || string.IsNullOrWhiteSpace(placesOptions.ApiKey))
        {
            return Failure(
                "places_disabled",
                "Google Places integration is disabled.",
                StatusCodes.Status503ServiceUnavailable,
                stopwatch.Elapsed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, placesOptions.TimeoutSeconds)));

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                BuildGooglePhotoMediaPath(request));
            httpRequest.Headers.Add("X-Goog-Api-Key", placesOptions.ApiKey);

            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Google Places photo provider failed statusCode={StatusCode} providerError={ProviderError}",
                    (int)response.StatusCode,
                    ExtractProviderErrorCode(errorPayload) ?? "unknown");
                return Failure(
                    ExtractProviderErrorCode(errorPayload) ?? "places_photo_provider_error",
                    response.ReasonPhrase ?? "Google Places photo provider failed.",
                    StatusCodes.Status502BadGateway,
                    stopwatch.Elapsed,
                    response.StatusCode);
            }

            if (request.SkipHttpRedirect)
            {
                var media = await response.Content.ReadFromJsonAsync<GooglePlacesPhotoMediaWire>(
                    cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(media?.PhotoUri))
                {
                    return Failure(
                        "places_photo_missing_uri",
                        "Google Places did not return a photo URI.",
                        StatusCodes.Status502BadGateway,
                        stopwatch.Elapsed,
                        response.StatusCode);
                }

                logger.LogInformation("Google Places photo redirect resolved elapsedMs={ElapsedMs}", stopwatch.Elapsed.TotalMilliseconds);
                return new GooglePlacesPhotoMediaResult(
                    Succeeded: true,
                    RedirectUri: media.PhotoUri.Trim(),
                    Content: null,
                    ContentType: null,
                    ErrorCode: null,
                    ErrorMessage: null,
                    Elapsed: stopwatch.Elapsed,
                    TimedOut: false,
                    StatusCode: response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new GooglePlacesPhotoMediaResult(
                Succeeded: true,
                RedirectUri: null,
                Content: content,
                ContentType: contentType,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: stopwatch.Elapsed,
                TimedOut: false,
                StatusCode: response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Google Places photo request timed out timeoutSeconds={TimeoutSeconds}", placesOptions.TimeoutSeconds);
            return Failure(
                "places_photo_timeout",
                "Google Places photo request timed out.",
                StatusCodes.Status504GatewayTimeout,
                stopwatch.Elapsed,
                timedOut: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Places photo request failed unexpectedly.");
            return Failure(
                "places_photo_provider_error",
                "Google Places photo provider failed.",
                StatusCodes.Status502BadGateway,
                stopwatch.Elapsed);
        }
    }

    private static GooglePlacesPhotoMediaResult Failure(
        string errorCode,
        string errorMessage,
        int statusCode,
        TimeSpan elapsed,
        HttpStatusCode? providerStatusCode = null,
        bool timedOut = false)
    {
        return new GooglePlacesPhotoMediaResult(
            Succeeded: false,
            RedirectUri: null,
            Content: null,
            ContentType: null,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            Elapsed: elapsed,
            TimedOut: timedOut,
            StatusCode: providerStatusCode ?? (HttpStatusCode)statusCode);
    }

    private static string BuildGooglePhotoMediaPath(GooglePlacesPhotoMediaRequest request)
    {
        var escapedName = EscapePhotoResourceName(request.PhotoResourceName);
        var width = ClampDimension(request.MaxWidthPx, DefaultMaxWidthPx);
        var height = ClampDimension(request.MaxHeightPx, DefaultMaxHeightPx);
        var skipRedirect = request.SkipHttpRedirect ? "true" : "false";
        return $"/v1/{escapedName}/media?maxWidthPx={width}&maxHeightPx={height}&skipHttpRedirect={skipRedirect}";
    }

    private static string EscapePhotoResourceName(string photoResourceName)
    {
        return string.Join(
            '/',
            photoResourceName.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
    }

    private static int ClampDimension(int? value, int fallback)
    {
        return Math.Clamp(value ?? fallback, MinDimensionPx, MaxDimensionPx);
    }

    public static bool IsValidPhotoResourceName(string? photoResourceName)
    {
        if (string.IsNullOrWhiteSpace(photoResourceName))
        {
            return false;
        }

        var trimmed = photoResourceName.Trim();
        if (trimmed.Length > MaxPhotoNameLength
            || trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
               && string.Equals(parts[0], "places", StringComparison.Ordinal)
               && string.Equals(parts[2], "photos", StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(parts[1])
               && !string.IsNullOrWhiteSpace(parts[3]);
    }

    private static string? ExtractProviderErrorCode(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        if (payload.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase))
        {
            return "invalid_argument";
        }

        if (payload.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return "permission_denied";
        }

        if (payload.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
        {
            return "resource_exhausted";
        }

        return null;
    }

    private sealed record GooglePlacesPhotoMediaWire(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("photoUri")] string? PhotoUri);
}
