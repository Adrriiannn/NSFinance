using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NSFinance.Api.Modules.AI.Services;

public interface IGooglePlacesCache
{
    bool TryGet<T>(string key, DateTime nowUtc, out T value)
        where T : class;

    void Set<T>(string key, T value, DateTime nowUtc, TimeSpan ttl)
        where T : class;
}

public sealed class InMemoryGooglePlacesCache : IGooglePlacesCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries =
        new(StringComparer.Ordinal);

    public bool TryGet<T>(string key, DateTime nowUtc, out T value)
        where T : class
    {
        value = default!;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (entry.ExpiresUtc <= nowUtc)
        {
            entries.TryRemove(key, out _);
            return false;
        }

        if (entry.Value is not T typed)
        {
            return false;
        }

        value = typed;
        return true;
    }

    public void Set<T>(string key, T value, DateTime nowUtc, TimeSpan ttl)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(key) || value is null || ttl <= TimeSpan.Zero)
        {
            return;
        }

        entries[key] = new CacheEntry(value, nowUtc.Add(ttl));
        if (entries.Count > 10_000)
        {
            CompactExpiredEntries(nowUtc);
        }
    }

    private void CompactExpiredEntries(DateTime nowUtc)
    {
        var expiredKeys = entries
            .Where(item => item.Value.ExpiresUtc <= nowUtc)
            .Select(item => item.Key)
            .ToArray();

        foreach (var key in expiredKeys)
        {
            entries.TryRemove(key, out _);
        }
    }

    private sealed record CacheEntry(object Value, DateTime ExpiresUtc);
}

public interface IGooglePlacesCacheKeyBuilder
{
    string BuildCompanionDiscoveryKey(CompanionPlaceDiscoveryRequest request, int maxCandidates);
    string BuildCompanionNearbyKey(CompanionNearbyDiscoveryRequest request, int maxCandidates);
    string BuildMerchantLookupKey(MerchantPlaceLookupRequest request, int maxCandidates);
    string BuildPlaceDetailsKey(string placeId);
}

public sealed class GooglePlacesCacheKeyBuilder : IGooglePlacesCacheKeyBuilder
{
    public string BuildCompanionDiscoveryKey(CompanionPlaceDiscoveryRequest request, int maxCandidates)
    {
        var query = NormalizeText(request.Query);
        var region = NormalizeText(request.CountryCode);
        var language = NormalizeText(request.LanguageCode);
        var latitude = request.Latitude.HasValue
            ? request.Latitude.Value.ToString("0.#######", CultureInfo.InvariantCulture)
            : string.Empty;
        var longitude = request.Longitude.HasValue
            ? request.Longitude.Value.ToString("0.#######", CultureInfo.InvariantCulture)
            : string.Empty;
        var radius = request.RadiusMeters?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var fieldMaskVariant = NormalizeText(request.FieldMaskVariant);
        return BuildHashedKey(
            "companion_discovery",
            $"{query}|{region}|{language}|{maxCandidates}|{latitude}|{longitude}|{radius}|{fieldMaskVariant}");
    }

    public string BuildCompanionNearbyKey(CompanionNearbyDiscoveryRequest request, int maxCandidates)
    {
        var region = NormalizeText(request.CountryCode);
        var language = NormalizeText(request.LanguageCode);
        var latitude = request.Latitude.ToString("0.#######", CultureInfo.InvariantCulture);
        var longitude = request.Longitude.ToString("0.#######", CultureInfo.InvariantCulture);
        var radius = request.RadiusMeters.ToString(CultureInfo.InvariantCulture);
        var fieldMaskVariant = NormalizeText(request.FieldMaskVariant);
        var types = request.IncludedTypes.Count == 0
            ? string.Empty
            : string.Join(
                ",",
                request.IncludedTypes
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Select(NormalizeText)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(type => type, StringComparer.Ordinal));

        return BuildHashedKey(
            "companion_nearby",
            $"{region}|{language}|{maxCandidates}|{latitude}|{longitude}|{radius}|{types}|{fieldMaskVariant}");
    }

    public string BuildMerchantLookupKey(MerchantPlaceLookupRequest request, int maxCandidates)
    {
        var descriptor = NormalizeText(request.MerchantDescriptor);
        var region = NormalizeText(request.CountryCode);
        var language = NormalizeText(request.LanguageCode);
        return BuildHashedKey(
            "merchant_lookup",
            $"{descriptor}|{region}|{language}|{maxCandidates}");
    }

    public string BuildPlaceDetailsKey(string placeId)
    {
        return BuildHashedKey("place_details", NormalizeText(placeId));
    }

    private static string BuildHashedKey(string prefix, string plain)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(plain));
        var hash = Convert.ToHexString(hashBytes);
        return $"{prefix}:{hash}";
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }
}
