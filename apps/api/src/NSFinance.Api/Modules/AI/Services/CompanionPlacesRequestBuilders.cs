using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionPlacesVocabularyNormalizationResult(
    string NormalizedQuery,
    IReadOnlyList<string> ReasonCodes);

public interface ICompanionPlacesVocabularyNormalizer
{
    CompanionPlacesVocabularyNormalizationResult Normalize(string? query);
}

public sealed class CompanionPlacesVocabularyNormalizer : ICompanionPlacesVocabularyNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> GenericCorrections =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cofee"] = "coffee",
            ["coffe"] = "coffee",
            ["cafee"] = "cafe",
            ["pleade"] = "please",
            ["plese"] = "please",
            ["pleas"] = "please",
            ["musuem"] = "museum",
            ["reastaurant"] = "restaurant",
            ["restarant"] = "restaurant",
            ["pharmcy"] = "pharmacy",
            ["playgroud"] = "playground",
            ["muesum"] = "museum"
        };

    public CompanionPlacesVocabularyNormalizationResult Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new CompanionPlacesVocabularyNormalizationResult(
                NormalizedQuery: string.Empty,
                ReasonCodes: ["places_request:text_query_preflight_failed"]);
        }

        var cleaned = Regex.Replace(
            query.Trim().ToLowerInvariant(),
            @"[^\p{L}\p{N}\s'\-]",
            " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return new CompanionPlacesVocabularyNormalizationResult(
                NormalizedQuery: string.Empty,
                ReasonCodes: ["places_request:text_query_preflight_failed"]);
        }

        var typoCorrected = false;
        var tokens = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token =>
            {
                if (!ShouldCorrectToken(token))
                {
                    return token;
                }

                if (!GenericCorrections.TryGetValue(token, out var corrected))
                {
                    return token;
                }

                typoCorrected = true;
                return corrected;
            })
            .ToArray();
        var normalized = string.Join(' ', tokens).Trim();
        var reasonCodes = new List<string>(2)
        {
            "places_request:text_query_normalized"
        };
        if (typoCorrected)
        {
            reasonCodes.Add("places_request:text_query_typo_normalized");
        }

        return new CompanionPlacesVocabularyNormalizationResult(
            NormalizedQuery: normalized,
            ReasonCodes: reasonCodes);
    }

    private static bool ShouldCorrectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 3 || token.Length > 16)
        {
            return false;
        }

        return token.All(char.IsLetter);
    }
}

public sealed record CompanionPlacesTextQueryBuildRequest(
    string UserQuery,
    LocalDiscoveryConstraintExtractionResult Constraints,
    PlaceSearchLocationContext? LocationContext,
    bool IsGpsNearMe,
    bool ForceSimplifiedFallback = false);

public sealed record CompanionPlacesTextQueryBuildResult(
    bool Succeeded,
    string? Query,
    IReadOnlyList<string> ReasonCodes,
    string? FailureReason = null);

public interface ICompanionPlacesTextQueryBuilder
{
    CompanionPlacesTextQueryBuildResult Build(CompanionPlacesTextQueryBuildRequest request);
}

public sealed class CompanionPlacesTextQueryBuilder(
    ICompanionPlacesVocabularyNormalizer vocabularyNormalizer) : ICompanionPlacesTextQueryBuilder
{
    private static readonly IReadOnlyDictionary<string, string> TypePhraseMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cafe"] = "coffee shops",
            ["restaurant"] = "restaurants",
            ["bar"] = "pubs",
            ["museum"] = "museums",
            ["park"] = "parks",
            ["playground"] = "playgrounds",
            ["tourist_attraction"] = "places to visit",
            ["zoo"] = "zoos",
            ["movie_theater"] = "cinemas",
            ["performing_arts_theater"] = "theatres",
            ["gas_station"] = "petrol stations",
            ["pharmacy"] = "pharmacies",
            ["gym"] = "gyms",
            ["electronics_store"] = "electronics stores",
            ["video_game_store"] = "video game stores",
            ["computer_store"] = "computer stores",
            ["mobile_phone_store"] = "mobile phone stores",
            ["convenience_store"] = "convenience stores",
            ["grocery_store"] = "grocery stores",
            ["supermarket"] = "supermarkets",
            ["store"] = "stores",
            ["shopping_mall"] = "shopping centers"
        };

    private static readonly HashSet<string> GenericLocalityStopWords =
    [
        "me",
        "here",
        "there",
        "where i am",
        "my area",
        "this area"
    ];

    private static readonly HashSet<string> ConversationalNoiseWords =
    [
        "can",
        "could",
        "would",
        "you",
        "please",
        "show",
        "some",
        "any",
        "me",
        "find",
        "look",
        "looking",
        "for",
        "tell"
    ];

    public CompanionPlacesTextQueryBuildResult Build(CompanionPlacesTextQueryBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var normalized = vocabularyNormalizer.Normalize(request.UserQuery);
        diagnostics.UnionWith(normalized.ReasonCodes);

        var intentPhrase = ResolveIntentPhrase(normalized.NormalizedQuery, request.Constraints);
        var preferencePrefix = ResolvePreferencePrefix(request.Constraints);
        var timeSuffix = request.Constraints.TimeHints.Any(value =>
                string.Equals(value, "open_now", StringComparison.OrdinalIgnoreCase))
            ? " open now"
            : string.Empty;

        var query = $"{preferencePrefix}{intentPhrase}{timeSuffix}".Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            query = SimplifyFallbackQuery(request.Constraints);
            diagnostics.Add("places_request:text_query_fallback_simplified");
        }

        var locality = ResolveLocalityCandidate(request.LocationContext, request.Constraints);
        if (!string.IsNullOrWhiteSpace(locality))
        {
            if (request.IsGpsNearMe)
            {
                diagnostics.Add("places_request:text_query_locality_skipped_low_confidence");
            }
            else if (TryNormalizeLocality(locality, out var normalizedLocality))
            {
                query = $"{query} in {normalizedLocality}";
                diagnostics.Add("places_request:text_query_locality_injected");
            }
            else
            {
                diagnostics.Add("places_request:text_query_locality_skipped_low_confidence");
            }
        }

        query = NormalizeFinalQuery(query);
        if (request.ForceSimplifiedFallback || !IsTextQueryPreflightValid(query))
        {
            query = NormalizeFinalQuery(SimplifyFallbackQuery(request.Constraints));
            diagnostics.Add("places_request:text_query_fallback_simplified");
            if (!request.IsGpsNearMe)
            {
                var fallbackLocality = ResolveLocalityCandidate(
                    request.LocationContext,
                    request.Constraints);
                if (!string.IsNullOrWhiteSpace(fallbackLocality)
                    && TryNormalizeLocality(fallbackLocality, out var normalizedFallbackLocality))
                {
                    query = NormalizeFinalQuery($"{query} in {normalizedFallbackLocality}");
                    diagnostics.Add("places_request:text_query_locality_injected");
                }
            }
        }

        if (!IsTextQueryPreflightValid(query))
        {
            diagnostics.Add("places_request:text_query_preflight_failed");
            return new CompanionPlacesTextQueryBuildResult(
                Succeeded: false,
                Query: null,
                ReasonCodes: diagnostics.ToArray(),
                FailureReason: "text_query_preflight_failed");
        }

        diagnostics.Add("places_request:text_query_preflight_valid");
        return new CompanionPlacesTextQueryBuildResult(
            Succeeded: true,
            Query: query,
            ReasonCodes: diagnostics.ToArray());
    }

    private static string ResolveIntentPhrase(
        string normalizedQuery,
        LocalDiscoveryConstraintExtractionResult constraints)
    {
        if (constraints.PlaceTypeHints.Count > 0
            && TypePhraseMap.TryGetValue(constraints.PlaceTypeHints[0], out var phraseFromHint))
        {
            return phraseFromHint;
        }

        var words = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => !ConversationalNoiseWords.Contains(word))
            .ToArray();
        if (words.Length > 0)
        {
            if (words.Any(word => word is "coffee" or "cafe" or "cafes"))
            {
                return "coffee shops";
            }

            if (words.Any(word => word is "pub" or "pubs" or "bar" or "bars" or "drink" or "drinking"))
            {
                return "pubs";
            }

            if (words.Any(word => word is "playground" or "playgrounds"))
            {
                return "playgrounds";
            }

            if (words.Any(word => word is "museum" or "museums"))
            {
                return "museums";
            }

            if (words.Any(word => word is "pharmacy" or "pharmacies" or "chemist"))
            {
                return "pharmacies";
            }

            if (words.Any(word => word is "restaurant" or "restaurants" or "dine" or "dining"))
            {
                return "restaurants";
            }

            if (words.Any(word => word is "ps5"
                    or "xbox"
                    or "playstation"
                    or "controller"
                    or "console"
                    or "laptop"
                    or "electronics"))
            {
                return "electronics stores";
            }

            if (words.Any(word => word is "redbull"
                    or "red"
                    or "bull"
                    or "snack"
                    or "snacks"
                    or "energy"
                    or "drink"))
            {
                return "convenience stores";
            }
        }

        if (constraints.AudienceHints.Any(value => string.Equals(value, "kids", StringComparison.OrdinalIgnoreCase)))
        {
            return "playgrounds";
        }

        if (constraints.AudienceHints.Any(value => string.Equals(value, "family", StringComparison.OrdinalIgnoreCase)))
        {
            return "family friendly places";
        }

        return "local places";
    }

    private static string ResolvePreferencePrefix(LocalDiscoveryConstraintExtractionResult constraints)
    {
        if (constraints.PreferenceHints.Any(value =>
                string.Equals(value, "dog_friendly", StringComparison.OrdinalIgnoreCase)))
        {
            return "dog friendly ";
        }

        if (constraints.PreferenceHints.Any(value =>
                string.Equals(value, "budget", StringComparison.OrdinalIgnoreCase)))
        {
            return "budget friendly ";
        }

        return string.Empty;
    }

    private static string SimplifyFallbackQuery(LocalDiscoveryConstraintExtractionResult constraints)
    {
        if (constraints.PlaceTypeHints.Count > 0
            && TypePhraseMap.TryGetValue(constraints.PlaceTypeHints[0], out var mapped))
        {
            return mapped;
        }

        if (constraints.AudienceHints.Any(value => string.Equals(value, "kids", StringComparison.OrdinalIgnoreCase)))
        {
            return "playgrounds";
        }

        if (constraints.AudienceHints.Any(value => string.Equals(value, "family", StringComparison.OrdinalIgnoreCase)))
        {
            return "family friendly places";
        }

        return "local places";
    }

    private static string? ResolveLocalityCandidate(
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult constraints)
    {
        if (!string.IsNullOrWhiteSpace(locationContext?.TypedArea))
        {
            return locationContext.TypedArea;
        }

        if (!string.IsNullOrWhiteSpace(constraints.LocalityHint))
        {
            return constraints.LocalityHint;
        }

        if (!string.IsNullOrWhiteSpace(locationContext?.LocalityLabel))
        {
            return locationContext.LocalityLabel;
        }

        return null;
    }

    private static bool TryNormalizeLocality(string locality, out string normalized)
    {
        normalized = string.Empty;
        var candidate = locality.Trim();
        if (candidate.Length < 3 || candidate.Length > 80)
        {
            return false;
        }

        candidate = Regex.Replace(candidate, @"[^\p{L}\p{N}\s'\-]", " ");
        candidate = Regex.Replace(candidate, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var lower = candidate.ToLowerInvariant();
        if (GenericLocalityStopWords.Contains(lower))
        {
            return false;
        }

        if (!candidate.Any(char.IsLetter))
        {
            return false;
        }

        if (Regex.IsMatch(candidate, @"^\d+[a-zA-Z]?$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var tokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 6)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static string NormalizeFinalQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(query.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}\s'\-]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length <= 120 ? cleaned : cleaned[..120].TrimEnd();
    }

    private static bool IsTextQueryPreflightValid(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return false;
        }

        if (!query.Any(char.IsLetter))
        {
            return false;
        }

        if (Regex.IsMatch(query, @"\bin\s+\d{1,5}\b", RegexOptions.CultureInvariant)
            && !Regex.IsMatch(query, @"\bin\s+[a-zA-Z]", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var tokenCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return tokenCount is > 0 and <= 12;
    }
}

public sealed record CompanionPlacesNearbyRequestBuildRequest(
    string? CountryCode,
    PlaceSearchLocationContext? LocationContext,
    IReadOnlyList<string> IncludedTypes,
    int MaxCandidates,
    int DefaultRadiusMeters);

public sealed record CompanionPlacesNearbyRequestBuildResult(
    bool Succeeded,
    CompanionNearbyDiscoveryRequest? Request,
    IReadOnlyList<string> ReasonCodes,
    string? FailureReason = null);

public interface ICompanionPlacesNearbyRequestBuilder
{
    CompanionPlacesNearbyRequestBuildResult Build(CompanionPlacesNearbyRequestBuildRequest request);
}

public sealed class CompanionPlacesNearbyRequestBuilder : ICompanionPlacesNearbyRequestBuilder
{
    private static readonly HashSet<string> SupportedNearbyTypes =
    [
        "bar",
        "cafe",
        "gas_station",
        "gym",
        "movie_theater",
        "museum",
        "park",
        "pharmacy",
        "playground",
        "performing_arts_theater",
        "restaurant",
        "store",
        "electronics_store",
        "convenience_store",
        "grocery_store",
        "tourist_attraction",
        "zoo"
    ];

    public CompanionPlacesNearbyRequestBuildResult Build(CompanionPlacesNearbyRequestBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var latitude = request.LocationContext?.Latitude;
        var longitude = request.LocationContext?.Longitude;
        if (!latitude.HasValue
            || !longitude.HasValue
            || latitude.Value is < -90d or > 90d
            || longitude.Value is < -180d or > 180d)
        {
            diagnostics.Add("places_request:nearby_preflight_failed");
            return new CompanionPlacesNearbyRequestBuildResult(
                Succeeded: false,
                Request: null,
                ReasonCodes: diagnostics.ToArray(),
                FailureReason: "nearby_invalid_coordinates");
        }

        var filteredTypes = request.IncludedTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim().ToLowerInvariant())
            .Where(type => Regex.IsMatch(type, "^[a-z_]+$", RegexOptions.CultureInvariant))
            .Where(type => SupportedNearbyTypes.Contains(type))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        diagnostics.Add($"places_request:nearby_types_selected:{filteredTypes.Length}");

        if (filteredTypes.Length == 0)
        {
            diagnostics.Add("places_request:nearby_preflight_failed");
            return new CompanionPlacesNearbyRequestBuildResult(
                Succeeded: false,
                Request: null,
                ReasonCodes: diagnostics.ToArray(),
                FailureReason: "nearby_no_supported_types");
        }

        var normalizedCountry = NormalizeCountryCode(request.CountryCode);
        if (!string.IsNullOrWhiteSpace(request.CountryCode) && normalizedCountry is null)
        {
            diagnostics.Add("places_request:nearby_simplified_before_send");
        }

        var requestedRadius = request.LocationContext?.RadiusMeters ?? request.DefaultRadiusMeters;
        var radiusMeters = Math.Clamp(requestedRadius <= 0 ? request.DefaultRadiusMeters : requestedRadius, 500, 15_000);
        if (requestedRadius != radiusMeters)
        {
            diagnostics.Add("places_request:nearby_simplified_before_send");
        }

        var maxCandidates = Math.Clamp(request.MaxCandidates, 4, 16);
        diagnostics.Add("places_request:nearby_preflight_valid");

        return new CompanionPlacesNearbyRequestBuildResult(
            Succeeded: true,
            Request: new CompanionNearbyDiscoveryRequest(
                Latitude: latitude.Value,
                Longitude: longitude.Value,
                RadiusMeters: radiusMeters,
                IncludedTypes: filteredTypes,
                CountryCode: normalizedCountry,
                MaxCandidates: maxCandidates),
            ReasonCodes: diagnostics.ToArray());
    }

    private static string? NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var normalized = countryCode.Trim().ToUpperInvariant();
        if (normalized == "ZZ")
        {
            return null;
        }

        return Regex.IsMatch(normalized, "^[A-Z]{2}$", RegexOptions.CultureInvariant)
            ? normalized
            : null;
    }
}
