using System.Globalization;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public static class CompanionLocationMetadataKeys
{
    public const string Source = "chat_location_source";
    public const string Latitude = "chat_location_latitude";
    public const string Longitude = "chat_location_longitude";
    public const string RadiusMeters = "chat_location_radius_meters";
    public const string TypedArea = "chat_location_typed_area";
    public const string LocalityLabel = "chat_location_locality_label";
    public const string AccuracyBucket = "chat_location_accuracy_bucket";
    public const string CapturedAtUtc = "chat_location_captured_at_utc";
    public const string PermissionState = "chat_location_permission_state";
    public const string NearbyPrompt = "chat_location_nearby_prompt";
    public const string RefreshAttempted = "chat_location_refresh_attempted";
    public const string RefreshOutcome = "chat_location_refresh_outcome";
}

public sealed record CompanionLocationGrounding(
    string? Source,
    double? Latitude,
    double? Longitude,
    int? RadiusMeters,
    string? TypedArea,
    string? LocalityLabel,
    string? AccuracyBucket,
    DateTimeOffset? CapturedAtUtc)
{
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;
    public bool HasTypedArea => !string.IsNullOrWhiteSpace(TypedArea);
}

public static class CompanionLocationGroundingParser
{
    private static readonly string[] CurrentLocationPhrases =
    [
        "near me",
        "nearby",
        "around me",
        "around here",
        "close to me",
        "close by",
        "near where i am",
        "where i am"
    ];

    private static readonly Regex NearMeRegex = new(
        @"\b(near me|nearby|around me|around here|close to me|close by|near where i am|where i am)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CompanionLocationGrounding Parse(
        IReadOnlyDictionary<string, string>? metadata,
        ConversationStateSnapshot? state)
    {
        var source = NormalizeOptional(GetValue(metadata, state, CompanionLocationMetadataKeys.Source));
        var latitude = ParseDouble(GetValue(metadata, state, CompanionLocationMetadataKeys.Latitude));
        var longitude = ParseDouble(GetValue(metadata, state, CompanionLocationMetadataKeys.Longitude));
        var radiusMeters = ParseInt(GetValue(metadata, state, CompanionLocationMetadataKeys.RadiusMeters));
        var typedArea = SanitizeAreaHint(GetValue(metadata, state, CompanionLocationMetadataKeys.TypedArea));
        var localityLabel = NormalizeOptional(GetValue(metadata, state, CompanionLocationMetadataKeys.LocalityLabel));
        var accuracyBucket = NormalizeOptional(GetValue(metadata, state, CompanionLocationMetadataKeys.AccuracyBucket));
        var capturedAtUtc = ParseDateTimeOffset(GetValue(metadata, state, CompanionLocationMetadataKeys.CapturedAtUtc));

        if (string.IsNullOrWhiteSpace(typedArea))
        {
            typedArea = NormalizeStateLocationPreference(state?.LocationPreference);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = latitude.HasValue && longitude.HasValue
                ? "gps"
                : !string.IsNullOrWhiteSpace(typedArea)
                    ? "typed_area"
                    : "none";
        }

        return new CompanionLocationGrounding(
            Source: source,
            Latitude: latitude,
            Longitude: longitude,
            RadiusMeters: radiusMeters,
            TypedArea: typedArea,
            LocalityLabel: localityLabel,
            AccuracyBucket: accuracyBucket,
            CapturedAtUtc: capturedAtUtc);
    }

    public static bool RequiresCurrentLocation(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var normalized = userMessage.Trim().ToLowerInvariant();
        return CurrentLocationPhrases.Any(phrase =>
            normalized.Contains(phrase, StringComparison.Ordinal));
    }

    public static string ApplyTypedAreaToQuery(string query, string? typedArea)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        var normalizedArea = SanitizeAreaHint(typedArea);
        if (string.IsNullOrWhiteSpace(normalizedArea))
        {
            return query.Trim();
        }

        if (query.Contains(normalizedArea, StringComparison.OrdinalIgnoreCase))
        {
            return query.Trim();
        }

        var replaced = NearMeRegex.Replace(query, $"in {normalizedArea}");
        if (!string.Equals(replaced, query, StringComparison.Ordinal))
        {
            return replaced.Trim();
        }

        return $"{query.Trim()} in {normalizedArea}";
    }

    public static bool IsValidAreaHint(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Length is < 3 or > 80)
        {
            return false;
        }

        if (!normalized.Any(char.IsLetter))
        {
            return false;
        }

        if (Regex.IsMatch(normalized, @"^\d+[a-zA-Z]?$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var tokens = normalized.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length is 0 or > 6)
        {
            return false;
        }

        if (tokens[0].All(char.IsDigit))
        {
            return false;
        }

        var numericTokens = tokens.Count(token => token.All(char.IsDigit));
        return numericTokens <= 1;
    }

    private static string? GetValue(
        IReadOnlyDictionary<string, string>? metadata,
        ConversationStateSnapshot? state,
        string key)
    {
        var metadataValue = GetDictionaryValue(metadata, key);
        if (!string.IsNullOrWhiteSpace(metadataValue))
        {
            return metadataValue;
        }

        return GetDictionaryValue(state?.Constraints, key);
    }

    private static string? GetDictionaryValue(
        IReadOnlyDictionary<string, string>? values,
        string key)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        if (values.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return null;
        }

        return Math.Clamp(parsed, 100, 50_000);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeStateLocationPreference(string? locationPreference)
    {
        var normalized = SanitizeAreaHint(locationPreference);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (string.Equals(normalized, "current_location", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "gps", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    private static string? SanitizeAreaHint(string? value)
    {
        var normalized = NormalizeOptional(value);
        return IsValidAreaHint(normalized) ? normalized : null;
    }
}
