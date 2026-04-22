using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record LocalDiscoveryConstraintExtractionResult(
    bool IsLocalDiscoveryCandidate,
    double Confidence,
    bool HasNearMeLanguage,
    bool HasExplicitLocality,
    string? LocalityHint,
    IReadOnlyList<string> PlaceTypeHints,
    IReadOnlyList<string> AudienceHints,
    IReadOnlyList<string> TimeHints,
    IReadOnlyList<string> PreferenceHints,
    IReadOnlyList<string> ReasonCodes);

public sealed record LocalDiscoveryShapedQueryResult(
    string Query,
    LocalDiscoveryConstraintExtractionResult Constraints,
    IReadOnlyList<string> ReasonCodes);

public interface ILocalDiscoveryConstraintExtractor
{
    LocalDiscoveryConstraintExtractionResult Extract(string? userQuery);
}

public interface ILocalDiscoveryQueryShaper
{
    LocalDiscoveryShapedQueryResult Shape(
        string userQuery,
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult? constraints = null);
}

public sealed partial class LocalDiscoveryConstraintExtractor : ILocalDiscoveryConstraintExtractor
{
    private static readonly string[] NearMePhrases =
    [
        "near me",
        "nearby",
        "around me",
        "around here",
        "close to me",
        "close by",
        "where i am",
        "near where i am"
    ];

    private static readonly string[] DiscoveryPhrases =
    [
        "places to visit",
        "places to go",
        "where can i go",
        "where should i go",
        "things to do",
        "something fun",
        "somewhere nice",
        "somewhere to go",
        "what can we do",
        "what should we do"
    ];

    private static readonly string[] TimePhrases =
    [
        "open now",
        "tonight",
        "this weekend",
        "weekend"
    ];

    private static readonly Dictionary<string, string> PlaceTypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["restaurant"] = "restaurant",
            ["restaurants"] = "restaurant",
            ["dine"] = "restaurant",
            ["dining"] = "restaurant",
            ["brunch"] = "brunch",
            ["cafe"] = "cafe",
            ["cafes"] = "cafe",
            ["coffee"] = "cafe",
            ["museum"] = "museum",
            ["museums"] = "museum",
            ["playground"] = "playground",
            ["playgrounds"] = "playground",
            ["beach"] = "beach",
            ["park"] = "park",
            ["parks"] = "park",
            ["attraction"] = "tourist_attraction",
            ["attractions"] = "tourist_attraction",
            ["visit"] = "tourist_attraction",
            ["zoo"] = "zoo",
            ["cinema"] = "movie_theater",
            ["theatre"] = "performing_arts_theater",
            ["theater"] = "performing_arts_theater",
            ["pub"] = "bar",
            ["pubs"] = "bar",
            ["bar"] = "bar",
            ["petrol"] = "gas_station",
            ["fuel"] = "gas_station",
            ["gas"] = "gas_station",
            ["pharmacy"] = "pharmacy",
            ["chemist"] = "pharmacy",
            ["gym"] = "gym",
            ["fitness"] = "gym"
        };

    private static readonly Dictionary<string, string> AudienceMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["kids"] = "kids",
            ["child"] = "kids",
            ["children"] = "kids",
            ["family"] = "family",
            ["couple"] = "couple",
            ["group"] = "group",
            ["friends"] = "group"
        };

    private static readonly Dictionary<string, string> PreferenceMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["budget"] = "budget",
            ["cheap"] = "budget",
            ["affordable"] = "budget",
            ["indoor"] = "indoor",
            ["outdoor"] = "outdoor",
            ["quiet"] = "quiet",
            ["calm"] = "quiet",
            ["lively"] = "lively",
            ["safe"] = "safe",
            ["scenic"] = "scenic",
            ["dog"] = "dog_friendly",
            ["friendly"] = "friendly"
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

    public LocalDiscoveryConstraintExtractionResult Extract(string? userQuery)
    {
        var normalized = Normalize(userQuery);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: false,
                Confidence: 0d,
                HasNearMeLanguage: false,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: [],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["local_discovery_empty_query"]);
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var placeTypeHints = ResolveMappedHints(tokens, PlaceTypeMap);
        var audienceHints = ResolveMappedHints(tokens, AudienceMap);
        var preferenceHints = ResolveMappedHints(tokens, PreferenceMap);
        var timeHints = ResolveTimeHints(normalized, tokens);
        var hasNearMeLanguage = ContainsAnyPhrase(normalized, NearMePhrases);
        var localityHint = ExtractLocalityHint(normalized);
        var hasExplicitLocality = !string.IsNullOrWhiteSpace(localityHint);
        var hasDiscoveryPhrase = ContainsAnyPhrase(normalized, DiscoveryPhrases);
        var hasDiscoveryToken = tokens.Contains("places")
                                || tokens.Contains("place")
                                || tokens.Contains("nearby")
                                || tokens.Contains("near")
                                || tokens.Contains("around")
                                || tokens.Contains("visit")
                                || tokens.Contains("go")
                                || tokens.Contains("fun")
                                || tokens.Contains("activity")
                                || tokens.Contains("activities");

        var confidence = 0d;
        var reasonCodes = new List<string>(8);

        if (placeTypeHints.Count > 0)
        {
            confidence += 0.45d;
            reasonCodes.Add("local_discovery_place_type_hint");
        }

        if (hasDiscoveryPhrase)
        {
            confidence += 0.35d;
            reasonCodes.Add("local_discovery_discovery_phrase");
        }

        if (hasNearMeLanguage)
        {
            confidence += 0.40d;
            reasonCodes.Add("local_discovery_nearby_phrase");
        }

        if (hasExplicitLocality)
        {
            confidence += 0.30d;
            reasonCodes.Add("local_discovery_explicit_locality");
        }

        if (audienceHints.Count > 0)
        {
            confidence += 0.20d;
            reasonCodes.Add("local_discovery_audience_hint");
        }

        if (timeHints.Count > 0)
        {
            confidence += 0.15d;
            reasonCodes.Add("local_discovery_time_hint");
        }

        if (preferenceHints.Count > 0)
        {
            confidence += 0.10d;
            reasonCodes.Add("local_discovery_preference_hint");
        }

        if (hasDiscoveryToken)
        {
            confidence += 0.12d;
            reasonCodes.Add("local_discovery_discovery_token");
        }

        confidence = Math.Round(Math.Clamp(confidence, 0d, 0.98d), 4, MidpointRounding.AwayFromZero);
        var isCandidate = confidence >= 0.55d
                          || (hasNearMeLanguage && (hasDiscoveryPhrase || placeTypeHints.Count > 0))
                          || (hasExplicitLocality && (hasDiscoveryPhrase || placeTypeHints.Count > 0));
        if (!isCandidate)
        {
            reasonCodes.Add("local_discovery_not_confident");
        }

        return new LocalDiscoveryConstraintExtractionResult(
            IsLocalDiscoveryCandidate: isCandidate,
            Confidence: confidence,
            HasNearMeLanguage: hasNearMeLanguage,
            HasExplicitLocality: hasExplicitLocality,
            LocalityHint: localityHint,
            PlaceTypeHints: placeTypeHints,
            AudienceHints: audienceHints,
            TimeHints: timeHints,
            PreferenceHints: preferenceHints,
            ReasonCodes: reasonCodes.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant(), "\\s+", " ");
    }

    private static IReadOnlyList<string> ResolveMappedHints(
        IReadOnlySet<string> tokens,
        IReadOnlyDictionary<string, string> mapping)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (mapping.TryGetValue(token, out var mapped))
            {
                hints.Add(mapped);
            }
        }

        return hints.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ResolveTimeHints(
        string normalizedText,
        IReadOnlySet<string> tokens)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ContainsAnyPhrase(normalizedText, TimePhrases))
        {
            if (normalizedText.Contains("open now", StringComparison.Ordinal))
            {
                result.Add("open_now");
            }

            if (normalizedText.Contains("tonight", StringComparison.Ordinal))
            {
                result.Add("tonight");
            }

            if (normalizedText.Contains("weekend", StringComparison.Ordinal))
            {
                result.Add("weekend");
            }
        }

        if (tokens.Contains("today"))
        {
            result.Add("today");
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ExtractLocalityHint(string normalizedText)
    {
        var match = LocalityPattern().Match(normalizedText);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        var locality = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(locality))
        {
            return null;
        }

        locality = TrimLocalityTail(locality);
        locality = Regex.Replace(
            locality,
            @"[^\p{L}\p{N}\s'\-]",
            " ");
        locality = Regex.Replace(locality, @"\s+", " ").Trim();
        locality = locality.TrimEnd('.', ',', ';', ':', '!', '?');
        if (GenericLocalityStopWords.Contains(locality))
        {
            return null;
        }

        if (!IsTrustedLocality(locality))
        {
            return null;
        }

        return locality.Length <= 80 ? locality : locality[..80].TrimEnd();
    }

    private static bool ContainsAnyPhrase(string source, IReadOnlyList<string> phrases)
    {
        return phrases.Any(phrase => source.Contains(phrase, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\b(?:in|around|near)\s+([a-z0-9][a-z0-9\s'\-]{1,60})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex LocalityPattern();

    private static string TrimLocalityTail(string locality)
    {
        var cutTokens = new[]
        {
            " with ",
            " for ",
            " that ",
            " which ",
            " open ",
            " tonight",
            " this weekend",
            " weekend"
        };

        var trimmed = locality;
        foreach (var token in cutTokens)
        {
            var index = trimmed.IndexOf(token, StringComparison.Ordinal);
            if (index > 0)
            {
                trimmed = trimmed[..index];
            }
        }

        return trimmed.Trim();
    }

    private static bool IsTrustedLocality(string locality)
    {
        if (string.IsNullOrWhiteSpace(locality)
            || locality.Length < 3
            || locality.Length > 80)
        {
            return false;
        }

        if (!locality.Any(char.IsLetter))
        {
            return false;
        }

        if (Regex.IsMatch(locality, @"^\d+[a-zA-Z]?$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var tokens = locality.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Length > 6)
        {
            return false;
        }

        var numericTokenCount = tokens.Count(token => token.All(char.IsDigit));
        if (numericTokenCount > 1)
        {
            return false;
        }

        if (tokens[0].All(char.IsDigit))
        {
            return false;
        }

        return true;
    }
}

public sealed class LocalDiscoveryQueryShaper(
    ILocalDiscoveryConstraintExtractor constraintExtractor) : ILocalDiscoveryQueryShaper
{
    public LocalDiscoveryShapedQueryResult Shape(
        string userQuery,
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult? constraints = null)
    {
        var extracted = constraints ?? constraintExtractor.Extract(userQuery);
        var baseQuery = Normalize(userQuery);
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (baseQuery.Length == 0)
        {
            return new LocalDiscoveryShapedQueryResult(
                Query: baseQuery,
                Constraints: extracted,
                ReasonCodes: ["local_discovery_query_empty"]);
        }

        var query = baseQuery;
        var effectiveTypedArea = !string.IsNullOrWhiteSpace(locationContext?.TypedArea)
            ? Normalize(locationContext?.TypedArea)
            : Normalize(extracted.LocalityHint);
        if (CompanionLocationGroundingParser.IsValidAreaHint(effectiveTypedArea))
        {
            query = CompanionLocationGroundingParser.ApplyTypedAreaToQuery(query, effectiveTypedArea);
            reasonCodes.Add("local_discovery_query_locality_applied");
        }
        else if (!string.IsNullOrWhiteSpace(effectiveTypedArea))
        {
            reasonCodes.Add("local_discovery_query_locality_skipped_low_confidence");
        }

        var placeTypePhrases = extracted.PlaceTypeHints.Select(ToQueryPhrase).ToArray();
        if (placeTypePhrases.Length > 0
            && !ContainsAny(query, placeTypePhrases))
        {
            query = $"{query} {string.Join(' ', placeTypePhrases.Take(2))}";
            reasonCodes.Add("local_discovery_query_place_types_appended");
        }
        else if (placeTypePhrases.Length == 0
                 && extracted.IsLocalDiscoveryCandidate)
        {
            var inferredDiscoveryType = InferFallbackDiscoveryType(extracted.AudienceHints);
            if (!ContainsAny(query, [inferredDiscoveryType]))
            {
                query = $"{query} {inferredDiscoveryType}";
                reasonCodes.Add("local_discovery_query_default_type_appended");
            }
        }

        var audiencePhrases = extracted.AudienceHints.Select(ToQueryPhrase).ToArray();
        if (audiencePhrases.Length > 0
            && !ContainsAny(query, audiencePhrases))
        {
            query = $"{query} {string.Join(' ', audiencePhrases.Take(1))}";
            reasonCodes.Add("local_discovery_query_audience_appended");
        }

        var timePhrases = extracted.TimeHints.Select(ToQueryPhrase).ToArray();
        if (timePhrases.Length > 0
            && !ContainsAny(query, timePhrases))
        {
            query = $"{query} {string.Join(' ', timePhrases.Take(1))}";
            reasonCodes.Add("local_discovery_query_time_appended");
        }

        var preferencePhrases = extracted.PreferenceHints.Select(ToQueryPhrase).ToArray();
        if (preferencePhrases.Length > 0
            && !ContainsAny(query, preferencePhrases))
        {
            query = $"{query} {string.Join(' ', preferencePhrases.Take(1))}";
            reasonCodes.Add("local_discovery_query_preference_appended");
        }

        query = query.Length <= 180
            ? query
            : query[..180].TrimEnd();
        reasonCodes.Add("local_discovery_query_shaped");

        return new LocalDiscoveryShapedQueryResult(
            Query: query,
            Constraints: extracted,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim(), "\\s+", " ");
    }

    private static bool ContainsAny(string source, IReadOnlyList<string> values)
    {
        return values.Any(value =>
            source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToQueryPhrase(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "tourist_attraction" => "tourist attractions",
            "movie_theater" => "cinema",
            "performing_arts_theater" => "theatre",
            "pet_friendly" => "pet friendly",
            "dog_friendly" => "dog friendly",
            "open_now" => "open now",
            _ => value.Replace('_', ' ')
        };
    }

    private static string InferFallbackDiscoveryType(IReadOnlyList<string> audienceHints)
    {
        if (audienceHints.Any(value => string.Equals(value, "kids", StringComparison.OrdinalIgnoreCase)))
        {
            return "playgrounds";
        }

        if (audienceHints.Any(value => string.Equals(value, "family", StringComparison.OrdinalIgnoreCase)))
        {
            return "family friendly attractions";
        }

        return "tourist attractions";
    }
}
