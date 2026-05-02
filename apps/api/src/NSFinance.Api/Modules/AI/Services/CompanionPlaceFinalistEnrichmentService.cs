using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceFinalistEnrichmentService(
    IPlaceDetailsService placeDetailsService,
    IGooglePlacesPhotoService? photoService,
    IPlacesShortLivedCache cache,
    IOptions<GooglePlacesOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceFinalistEnrichmentService
{
    public async Task<CompanionPlaceFinalistResult> EnrichAsync(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> rankedCandidates,
        int maxCards,
        CancellationToken cancellationToken)
    {
        var finalists = rankedCandidates.Take(Math.Clamp(maxCards, 1, 10)).ToArray();
        var cards = new List<CompanionPlaceCardResult>(finalists.Length);
        var enrichedCount = 0;
        foreach (var candidate in finalists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = await GetDetailsCachedAsync(candidate.PlaceId, cancellationToken);
            if (details is not null)
            {
                enrichedCount++;
            }

            cards.Add(BuildCard(candidate, details));
        }

        await telemetry.TrackAsync(
            "places.finalists.enriched_count",
            new Dictionary<string, object?>
            {
                ["enrichedCount"] = enrichedCount,
                ["visibleCardCount"] = cards.Count
            },
            cancellationToken);

        return new CompanionPlaceFinalistResult(
            StructuredResults: cards.Count == 0 ? null : new CompanionStructuredResults("places", cards),
            Finalists: finalists,
            Diagnostics: ["places_finalists_enriched"],
            EnrichedCount: enrichedCount);
    }

    private async Task<PlaceDetailsResult?> GetDetailsCachedAsync(string placeId, CancellationToken cancellationToken)
    {
        var cacheKey = Hash("place_details_v2");
        var cached = await cache.GetAsync<PlaceDetailsResult>("google_places", placeId, cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var details = await placeDetailsService.GetDetailsAsync(placeId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(details.PlaceId))
            {
                await cache.SetAsync(
                    "google_places",
                    placeId,
                    cacheKey,
                    details,
                    TimeSpan.FromSeconds(Math.Max(30, options.Value.PlaceDetailsCacheTtlSeconds)),
                    cancellationToken);
                return details;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return null;
    }

    private CompanionPlaceCardResult BuildCard(CompanionPlacePoolCandidate candidate, PlaceDetailsResult? details)
    {
        var photoUrls = BuildPhotoUrls(details);
        var address = details?.Address ?? candidate.ShortFormattedAddress;
        return new CompanionPlaceCardResult(
            Id: candidate.PlaceId,
            Name: details?.Name ?? candidate.DisplayName,
            DistanceMeters: candidate.DistanceMeters,
            PhotoUrl: photoUrls.FirstOrDefault(),
            PhotoUrls: photoUrls,
            FormattedAddress: address,
            ShortFormattedAddress: candidate.ShortFormattedAddress,
            Rating: details?.Rating ?? candidate.Rating,
            OpenNow: details?.OpeningHours?.OpenNow ?? candidate.OpenNow,
            PriceLevel: details?.PriceLevel ?? candidate.PriceLevel,
            WebsiteUrl: details?.Website,
            Category: details?.PrimaryTypeDisplayName ?? candidate.PrimaryTypeDisplayName ?? Humanize(candidate.PrimaryType),
            PrimaryTypeDisplayName: details?.PrimaryTypeDisplayName ?? candidate.PrimaryTypeDisplayName,
            ClosesInMinutes: null,
            OpensInMinutes: details?.OpeningHours?.OpenNow == false ? TryComputeFutureMinutes(details.OpeningHours.NextOpenTimeUtc) : null,
            PhoneNumber: details?.NationalPhoneNumber,
            MenuUrl: TryResolveMenuUrl(details?.Website),
            GoogleMapsUri: details?.GoogleMapsUri,
            Latitude: details?.Location?.Latitude ?? candidate.Latitude,
            Longitude: details?.Location?.Longitude ?? candidate.Longitude);
    }

    private IReadOnlyList<string> BuildPhotoUrls(PlaceDetailsResult? details)
    {
        if (photoService is null
            || details?.Photos is not { Count: > 0 })
        {
            return [];
        }

        return details.Photos
            .Select(photo => photoService.BuildAppPhotoUrl(photo.Name, 900, 520))
            .Where(static url => !string.IsNullOrWhiteSpace(url))
            .Select(static url => url!)
            .Take(8)
            .ToArray();
    }

    private static int? TryComputeFutureMinutes(DateTimeOffset? futureUtc)
    {
        if (!futureUtc.HasValue)
        {
            return null;
        }

        var minutes = (int)Math.Ceiling((futureUtc.Value - DateTimeOffset.UtcNow).TotalMinutes);
        return minutes > 0 && minutes < (7 * 24 * 60) ? minutes : null;
    }

    private static string? TryResolveMenuUrl(string? websiteUri)
    {
        return string.IsNullOrWhiteSpace(websiteUri) || !websiteUri.Contains("menu", StringComparison.OrdinalIgnoreCase)
            ? null
            : websiteUri.Trim();
    }

    private static string? Humanize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((word, index) => index == 0
                    ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())
                    : word.ToLowerInvariant()));
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
