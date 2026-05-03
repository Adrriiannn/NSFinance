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
                     .Concat(candidate.RetrievalIncludedTypes)
                     .Concat(candidate.RetrievalRoleFamilies)
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

        if (normalized.Contains("mailbox", StringComparison.Ordinal)
            || normalized.Contains("mail box", StringComparison.Ordinal)
            || normalized.Contains("post box", StringComparison.Ordinal)
            || normalized.Contains("parcel locker", StringComparison.Ordinal))
        {
            families.Add("mailbox");
            if (normalized.Contains("post box", StringComparison.Ordinal))
            {
                families.Add("post_box");
            }

            if (normalized.Contains("parcel locker", StringComparison.Ordinal))
            {
                families.Add("parcel_locker");
            }
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

        AddAccommodationFamilies(families, normalized);

        if (normalized.Contains("electric vehicle charging station", StringComparison.Ordinal)
            || normalized.Contains("electric vehicle charging", StringComparison.Ordinal)
            || normalized.Contains("ev charging", StringComparison.Ordinal)
            || normalized.Contains("ev charger", StringComparison.Ordinal)
            || normalized.Contains("ev_charging", StringComparison.Ordinal)
            || normalized.Contains("electric_vehicle_charging_station", StringComparison.Ordinal))
        {
            families.Add("ev_charging");
            families.Add("electric_vehicle_charging_station");
        }

        if (normalized.Contains("bicycle", StringComparison.Ordinal)
            || normalized.Contains("bike shop", StringComparison.Ordinal)
            || normalized.Contains("cycle shop", StringComparison.Ordinal)
            || normalized.Contains("bike repair", StringComparison.Ordinal)
            || normalized.Contains("cycle repair", StringComparison.Ordinal))
        {
            families.Add("bicycle_store");
            families.Add("bike_shop");
            families.Add("cycle_shop");
            if (normalized.Contains("repair", StringComparison.Ordinal))
            {
                families.Add("bicycle_repair_shop");
            }
        }

        if (normalized.Contains("store", StringComparison.Ordinal) || normalized.Contains("shop", StringComparison.Ordinal))
        {
            families.Add("store");
        }

        if (normalized.Contains("office", StringComparison.Ordinal)
            || normalized.Contains("corporate office", StringComparison.Ordinal)
            || normalized.Contains("headquarters", StringComparison.Ordinal)
            || normalized.Contains("business center", StringComparison.Ordinal)
            || normalized.Contains("business centre", StringComparison.Ordinal))
        {
            families.Add("office");
            families.Add("corporate_office");
            if (normalized.Contains("headquarters", StringComparison.Ordinal))
            {
                families.Add("headquarters");
            }

            if (normalized.Contains("business center", StringComparison.Ordinal)
                || normalized.Contains("business centre", StringComparison.Ordinal))
            {
                families.Add("business_center");
            }
        }

        if (normalized.Contains("establishment", StringComparison.Ordinal)
            || normalized.Contains("point of interest", StringComparison.Ordinal)
            || normalized.Contains("point_of_interest", StringComparison.Ordinal))
        {
            families.Add("establishment");
            families.Add("point_of_interest");
        }
    }

    private static void AddAccommodationFamilies(HashSet<string> families, string normalized)
    {
        if (HasPhrase(normalized, "student accommodation"))
        {
            families.Add("student_accommodation");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "aparthotel") || HasPhrase(normalized, "apartment hotel"))
        {
            families.Add("aparthotel");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "serviced apartment") || HasPhrase(normalized, "serviced apartments"))
        {
            families.Add("serviced_apartment");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "guesthouse") || HasPhrase(normalized, "guesthouses") || HasPhrase(normalized, "guest house") || HasPhrase(normalized, "guest houses"))
        {
            families.Add("guesthouse");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "bed and breakfast") || HasPhrase(normalized, "b&b") || HasPhrase(normalized, "b b"))
        {
            families.Add("bed_and_breakfast");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "hostel") || HasPhrase(normalized, "hostels"))
        {
            families.Add("hostel");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "motel") || HasPhrase(normalized, "motels"))
        {
            families.Add("motel");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "vacation rental")
            || HasPhrase(normalized, "holiday rental")
            || HasPhrase(normalized, "short term rental")
            || HasPhrase(normalized, "private room")
            || HasPhrase(normalized, "room rental")
            || HasPhrase(normalized, "airbnb"))
        {
            families.Add("private_accommodation");
            families.Add("vacation_rental");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "campground") || HasPhrase(normalized, "camp site") || HasPhrase(normalized, "campsite"))
        {
            families.Add("campground");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "resort"))
        {
            families.Add("resort");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "lodging"))
        {
            families.Add("lodging");
            families.Add("accommodation");
        }

        if (HasPhrase(normalized, "accommodation"))
        {
            families.Add("accommodation");
        }

        if ((HasPhrase(normalized, "hotel") || HasPhrase(normalized, "hotels")) && !HasPhrase(normalized, "aparthotel") && !HasPhrase(normalized, "apartment hotel"))
        {
            families.Add("hotel");
            families.Add("accommodation");
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

        if (normalized.Contains("office", StringComparison.Ordinal)
            || normalized.Contains("headquarters", StringComparison.Ordinal)
            || normalized.Contains(" hq", StringComparison.Ordinal))
        {
            families.Add("office");
        }
    }

    private static bool HasPhrase(string normalized, string phrase)
    {
        return Regex.IsMatch(normalized, $@"(^|\s){Regex.Escape(phrase)}($|\s)", RegexOptions.IgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }
}
