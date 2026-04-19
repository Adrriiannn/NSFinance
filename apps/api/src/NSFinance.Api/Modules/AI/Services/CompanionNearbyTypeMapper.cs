using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionNearbyTypeMappingResult(
    IReadOnlyList<string> IncludedTypes,
    IReadOnlyList<string> ReasonCodes);

public interface ICompanionNearbyTypeMapper
{
    CompanionNearbyTypeMappingResult Map(
        string? userQuery,
        LocalDiscoveryConstraintExtractionResult constraints);
}

public sealed class CompanionNearbyTypeMapper : ICompanionNearbyTypeMapper
{
    private static readonly IReadOnlyDictionary<string, string> HintToNearbyType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cafe"] = "cafe",
            ["restaurant"] = "restaurant",
            ["bar"] = "bar",
            ["museum"] = "museum",
            ["park"] = "park",
            ["playground"] = "playground",
            ["tourist_attraction"] = "tourist_attraction",
            ["zoo"] = "zoo",
            ["movie_theater"] = "movie_theater",
            ["performing_arts_theater"] = "performing_arts_theater",
            ["gas_station"] = "gas_station",
            ["pharmacy"] = "pharmacy",
            ["gym"] = "gym"
        };

    public CompanionNearbyTypeMappingResult Map(
        string? userQuery,
        LocalDiscoveryConstraintExtractionResult constraints)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hint in constraints.PlaceTypeHints)
        {
            if (HintToNearbyType.TryGetValue(hint, out var mapped))
            {
                types.Add(mapped);
                reasons.Add("nearby_type_from_place_hint");
            }
        }

        var normalizedQuery = Normalize(userQuery);
        if (types.Count == 0 && normalizedQuery.Length > 0)
        {
            if (ContainsAnyPhrase(normalizedQuery, "coffee", "coffee shop", "cafes", "cafe"))
            {
                types.Add("cafe");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "pub", "bar", "drink", "drinking"))
            {
                types.Add("bar");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "petrol", "fuel", "gas station"))
            {
                types.Add("gas_station");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "pharmacy", "chemist"))
            {
                types.Add("pharmacy");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "gym", "fitness"))
            {
                types.Add("gym");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "playground"))
            {
                types.Add("playground");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "park", "parks"))
            {
                types.Add("park");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "museum", "museums"))
            {
                types.Add("museum");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "restaurant", "dine", "food"))
            {
                types.Add("restaurant");
                reasons.Add("nearby_type_from_query_phrase");
            }
            else if (ContainsAnyPhrase(normalizedQuery, "fun", "things to do", "places to visit", "attraction"))
            {
                types.Add("tourist_attraction");
                reasons.Add("nearby_type_from_query_phrase");
            }
        }

        if (types.Count == 0)
        {
            if (constraints.AudienceHints.Any(hint => string.Equals(hint, "kids", StringComparison.OrdinalIgnoreCase)))
            {
                types.Add("playground");
                reasons.Add("nearby_type_from_audience_default");
            }
            else if (constraints.AudienceHints.Any(hint => string.Equals(hint, "family", StringComparison.OrdinalIgnoreCase)))
            {
                types.Add("tourist_attraction");
                reasons.Add("nearby_type_from_audience_default");
            }
        }

        return new CompanionNearbyTypeMappingResult(
            IncludedTypes: types
                .Take(4)
                .OrderBy(type => type, StringComparer.Ordinal)
                .ToArray(),
            ReasonCodes: reasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray());
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim().ToLowerInvariant(), "\\s+", " ");
    }

    private static bool ContainsAnyPhrase(string value, params string[] phrases)
    {
        return phrases.Any(phrase => value.Contains(phrase, StringComparison.Ordinal));
    }
}
