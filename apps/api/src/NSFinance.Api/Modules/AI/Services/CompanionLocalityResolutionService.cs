using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionLocalityResolutionResult(
    bool HasCoordinates,
    double? Latitude,
    double? Longitude,
    string? ResolvedLocalityLabel,
    string? ReasonCode);

public interface ICompanionLocalityResolutionService
{
    Task<CompanionLocalityResolutionResult> ResolveAsync(
        string? locality,
        string? countryCode,
        string? languageCode,
        CancellationToken cancellationToken);
}

public sealed class CompanionLocalityResolutionService(
    IGooglePlacesClient placesClient,
    IGooglePlacesCache cache,
    IOptions<GooglePlacesOptions> options,
    ILogger<CompanionLocalityResolutionService> logger) : ICompanionLocalityResolutionService
{
    private const string LocalityResolutionFieldMask = "places.id,places.displayName,places.formattedAddress,places.types,places.location";
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<CompanionLocalityResolutionResult> ResolveAsync(
        string? locality,
        string? countryCode,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedLocality = Normalize(locality);
        if (string.IsNullOrWhiteSpace(normalizedLocality))
        {
            return new CompanionLocalityResolutionResult(
                HasCoordinates: false,
                Latitude: null,
                Longitude: null,
                ResolvedLocalityLabel: null,
                ReasonCode: "locality_resolution_missing_input");
        }

        var nowUtc = DateTime.UtcNow;
        var cacheKey = BuildCacheKey(normalizedLocality, countryCode, languageCode);
        if (cache.TryGet(cacheKey, nowUtc, out CompanionLocalityResolutionResult cached))
        {
            return cached;
        }

        var providerResult = await placesClient.SearchTextAsync(
            new GooglePlacesSearchTextRequest(
                Query: normalizedLocality,
                MaxResultCount: 3,
                RegionCode: Normalize(countryCode),
                LanguageCode: Normalize(languageCode),
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                FieldMask: LocalityResolutionFieldMask,
                UseCaseTag: "locality_resolution"),
            cancellationToken);

        if (!providerResult.Succeeded)
        {
            var failed = new CompanionLocalityResolutionResult(
                HasCoordinates: false,
                Latitude: null,
                Longitude: null,
                ResolvedLocalityLabel: null,
                ReasonCode: "locality_resolution_provider_failure");
            cache.Set(
                cacheKey,
                failed,
                nowUtc,
                TimeSpan.FromSeconds(Math.Max(1, placesOptions.FailureCacheTtlSeconds)));
            logger.LogInformation(
                "Companion locality resolution failed locality={Locality} providerError={ProviderError}",
                normalizedLocality,
                providerResult.ErrorCode ?? "unknown");
            return failed;
        }

        var best = SelectBestMatch(providerResult.Value ?? []);
        if (best?.Location is not { } location)
        {
            var noData = new CompanionLocalityResolutionResult(
                HasCoordinates: false,
                Latitude: null,
                Longitude: null,
                ResolvedLocalityLabel: null,
                ReasonCode: "locality_resolution_no_coordinates");
            cache.Set(
                cacheKey,
                noData,
                nowUtc,
                TimeSpan.FromSeconds(Math.Max(1, placesOptions.FailureCacheTtlSeconds)));
            return noData;
        }

        var success = new CompanionLocalityResolutionResult(
            HasCoordinates: true,
            Latitude: location.Latitude,
            Longitude: location.Longitude,
            ResolvedLocalityLabel: Normalize(best.DisplayName)
                                  ?? Normalize(best.FormattedAddress)
                                  ?? normalizedLocality,
            ReasonCode: "locality_resolution_succeeded");
        cache.Set(
            cacheKey,
            success,
            nowUtc,
            TimeSpan.FromSeconds(Math.Max(1, placesOptions.CompanionCacheTtlSeconds)));
        return success;
    }

    private static GooglePlacesClientPlace? SelectBestMatch(
        IReadOnlyList<GooglePlacesClientPlace> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .Where(candidate => candidate.Location is not null)
            .OrderByDescending(ScoreCandidate)
            .FirstOrDefault();
    }

    private static int ScoreCandidate(GooglePlacesClientPlace candidate)
    {
        var score = 0;
        if (candidate.Types.Count == 0)
        {
            return score;
        }

        if (candidate.Types.Any(type => string.Equals(type, "locality", StringComparison.OrdinalIgnoreCase)))
        {
            score += 5;
        }

        if (candidate.Types.Any(type => string.Equals(type, "postal_town", StringComparison.OrdinalIgnoreCase)))
        {
            score += 4;
        }

        if (candidate.Types.Any(type => string.Equals(type, "administrative_area_level_2", StringComparison.OrdinalIgnoreCase)))
        {
            score += 3;
        }

        if (candidate.Types.Any(type => string.Equals(type, "neighborhood", StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(candidate.DisplayName))
        {
            score += 1;
        }

        return score;
    }

    private static string BuildCacheKey(
        string locality,
        string? countryCode,
        string? languageCode)
    {
        var normalized = $"{locality}|{Normalize(countryCode)}|{Normalize(languageCode)}";
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(hashBytes);
        return $"locality_resolution:{hash}";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLower(CultureInfo.InvariantCulture);
    }
}
