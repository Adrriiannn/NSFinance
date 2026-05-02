using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceTypeFamilyClassifier(IChatTelemetry telemetry) : ICompanionPlaceTypeFamilyClassifier
{
    public IReadOnlySet<string> ClassifyFamilies(CompanionPlacePoolCandidate candidate)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in candidate.Types
                     .Append(candidate.PrimaryType)
                     .Append(candidate.PrimaryTypeDisplayName)
                     .Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            AddFamiliesFromSource(families, source!);
        }

        AddWeakDisplayNameFamilies(families, candidate.DisplayName);

        _ = telemetry.TrackAsync(
            "places.type_family.classified",
            new Dictionary<string, object?>
            {
                ["placeId"] = candidate.PlaceId,
                ["familyCount"] = families.Count,
                ["families"] = families.ToArray()
            },
            CancellationToken.None);

        return families;
    }

    private static void AddFamiliesFromSource(HashSet<string> families, string source)
    {
        var normalized = Normalize(source);
        if (normalized.Contains("atm", StringComparison.Ordinal))
        {
            families.Add("atm");
        }

        if (normalized.Contains("bank", StringComparison.Ordinal) || normalized.Contains("financial institution", StringComparison.Ordinal))
        {
            families.Add("bank");
            families.Add("financial_institution");
        }

        if (normalized.Contains("parking", StringComparison.Ordinal)
            || normalized.Contains("car park", StringComparison.Ordinal)
            || normalized.Contains("parking lot", StringComparison.Ordinal)
            || normalized.Contains("parking garage", StringComparison.Ordinal))
        {
            families.Add("parking");
        }

        if (normalized.Contains("restaurant", StringComparison.Ordinal)
            || normalized.Contains("dining", StringComparison.Ordinal))
        {
            families.Add("restaurant");
        }

        if (normalized.Contains("fast food", StringComparison.Ordinal))
        {
            families.Add("fast_food_restaurant");
        }

        if (normalized.Contains("takeaway", StringComparison.Ordinal) || normalized.Contains("meal takeaway", StringComparison.Ordinal))
        {
            families.Add("meal_takeaway");
        }

        if (normalized.Contains("cafe", StringComparison.Ordinal)
            || normalized.Contains("café", StringComparison.Ordinal)
            || normalized.Contains("coffee shop", StringComparison.Ordinal))
        {
            families.Add("cafe");
            families.Add("coffee_shop");
        }

        if (normalized.Contains("gas station", StringComparison.Ordinal)
            || normalized.Contains("petrol station", StringComparison.Ordinal)
            || normalized.Contains("fuel station", StringComparison.Ordinal))
        {
            families.Add("gas_station");
        }

        if (normalized.Contains("post office", StringComparison.Ordinal))
        {
            families.Add("post_office");
        }

        if (normalized.Contains("pharmacy", StringComparison.Ordinal))
        {
            families.Add("pharmacy");
        }

        if (normalized.Contains("museum", StringComparison.Ordinal))
        {
            families.Add("museum");
        }

        if (normalized.Contains("park", StringComparison.Ordinal) && !normalized.Contains("parking", StringComparison.Ordinal) && !normalized.Contains("car park", StringComparison.Ordinal))
        {
            families.Add("park");
        }

        if (normalized.Contains("tourist attraction", StringComparison.Ordinal))
        {
            families.Add("tourist_attraction");
        }

        if (normalized.Contains("car wash", StringComparison.Ordinal))
        {
            families.Add("car_wash");
        }

        if (normalized.Contains("hotel", StringComparison.Ordinal) || normalized.Contains("lodging", StringComparison.Ordinal))
        {
            families.Add("hotel");
            families.Add("lodging");
        }
    }

    private static void AddWeakDisplayNameFamilies(HashSet<string> families, string displayName)
    {
        var normalized = Normalize(displayName);
        if (normalized.Contains("car park", StringComparison.Ordinal) || normalized.Contains("parking", StringComparison.Ordinal))
        {
            families.Add("parking");
        }

        if (normalized.Contains("atm", StringComparison.Ordinal))
        {
            families.Add("atm");
        }
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }
}
