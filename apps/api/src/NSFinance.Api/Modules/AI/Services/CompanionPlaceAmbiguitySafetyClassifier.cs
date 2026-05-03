using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

// This is not a general category fallback. It only guards known high-risk
// ambiguity where generic phrase fallback can produce harmful or visibly wrong results.
public sealed class CompanionPlaceAmbiguitySafetyClassifier(IChatTelemetry telemetry) : ICompanionPlaceAmbiguitySafetyClassifier
{
    public CompanionPlaceAmbiguitySafetyResult Apply(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(strategy);

        var normalized = Normalize($"{request.UserMessage} {strategy.CanonicalQuery} {intent.PlaceQuery}");
        var reasonCodes = new List<string>();
        var guarded = strategy;

        if (HasAny(normalized, "bike repair", "bicycle repair", "cycle repair"))
        {
            guarded = ApplyGuard(
                strategy,
                "bike repair",
                new CompanionPlaceRoleIntent("bicycle_store", ["bicycle_store", "store"], ["bicycle_store", "bike_shop", "cycle_shop", "bicycle_repair_shop"], [], ["repair"], "compatible"),
                ["bicycle repair shop", "bike repair", "cycle repair"],
                []);
            reasonCodes.Add("ambiguity_guard_bike_repair");
        }
        else if (HasAny(normalized, "bike shop", "bike shops", "bicycle shop", "bicycle shops", "cycle shop", "cycle shops"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent("bicycle_store", ["bicycle_store", "store"], ["bicycle_store", "bike_shop", "cycle_shop"], [], [], "compatible"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "bike shops").ToArray(),
                []);
            reasonCodes.Add("ambiguity_guard_bike_shop_not_brand");
        }
        else if (HasAny(normalized, "car park", "car parks", "parking", "parking lot", "parking garage"))
        {
            guarded = ApplyGuard(
                strategy,
                "car parks",
                new CompanionPlaceRoleIntent("parking", ["parking"], ["parking", "parking_lot", "parking_garage"], ["park", "tourist_attraction"], [], "strict"),
                ["car parks", "parking", "parking garage"],
                ["park"]);
            reasonCodes.Add("ambiguity_guard_parking_not_park");
        }
        else if (HasAny(normalized, "atm", "atms", "cash machine"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent("atm", ["atm"], ["atm"], ["bank"], [], "strict"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "ATM").ToArray(),
                ["bank"]);
            reasonCodes.Add("ambiguity_guard_atm_not_bank");
        }
        else if (HasAny(normalized, "bank", "banks", "bank branch", "bank branches"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent("bank_branch", ["bank", "financial_institution"], ["bank"], ["atm"], [], "strict"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "bank").ToArray(),
                ["atm"]);
            reasonCodes.Add("ambiguity_guard_bank_not_atm");
        }
        else if (HasAny(normalized, "post office", "post offices"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent("post_office", ["post_office"], ["post_office"], ["mailbox"], [], "strict"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "post office").ToArray(),
                []);
            reasonCodes.Add("ambiguity_guard_post_office_not_mailbox");
        }
        else if (HasAny(normalized, "hotel", "hotels"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent(
                    "hotel",
                    ["hotel"],
                    [],
                    ["motel", "lodging", "guesthouse", "bed_and_breakfast", "hostel", "private_accommodation", "vacation_rental", "student_accommodation", "campground", "restaurant", "bar"],
                    [],
                    "strict"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "hotels").ToArray(),
                []);
            reasonCodes.Add("ambiguity_guard_hotel_not_restaurant");
        }
        else if (HasAny(normalized, "petrol station", "petrol stations", "gas station", "fuel station", "service station"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent("gas_station", ["gas_station"], ["gas_station"], ["convenience_store_only", "car_wash"], [], "strict"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "petrol stations").ToArray(),
                []);
            reasonCodes.Add("ambiguity_guard_petrol_station_not_store");
        }
        else if (HasAny(normalized, "fine dining", "fancy restaurant", "upscale restaurant"))
        {
            guarded = ApplyGuard(
                strategy,
                "fine dining restaurants",
                new CompanionPlaceRoleIntent(
                    "restaurant",
                    ["restaurant"],
                    ["restaurant", "fine_dining_restaurant", "irish_restaurant", "french_restaurant", "asian_restaurant", "european_restaurant", "italian_restaurant", "seafood_restaurant"],
                    ["fast_food_restaurant", "meal_takeaway", "cafe"],
                    ["fine_dining", "upscale"],
                    "compatible"),
                ["fine dining restaurants", "upscale restaurants"],
                ["fast_food_restaurant", "meal_takeaway"]);
            reasonCodes.Add("ambiguity_guard_fine_dining_not_fast_food");
        }
        else if (HasAny(normalized, "pharmacy", "pharmacies"))
        {
            guarded = ApplyGuard(
                strategy,
                strategy.CanonicalQuery,
                new CompanionPlaceRoleIntent("pharmacy", ["pharmacy"], ["pharmacy"], ["hospital"], [], "strict"),
                strategy.SearchVariants.Select(static item => item.Query).DefaultIfEmpty(strategy.CanonicalQuery ?? "pharmacies").ToArray(),
                []);
            reasonCodes.Add("ambiguity_guard_pharmacy_not_hospital");
        }

        if (reasonCodes.Count == 0)
        {
            return new CompanionPlaceAmbiguitySafetyResult(strategy, false, []);
        }

        _ = telemetry.TrackAsync(
            "places.search_strategy.ambiguity_guard_applied",
            new Dictionary<string, object?>
            {
                ["originalMessage"] = request.UserMessage,
                ["canonicalQuery"] = guarded.CanonicalQuery,
                ["requestedRole"] = guarded.Role.RequestedRole,
                ["reasonCodes"] = reasonCodes.ToArray()
            },
            CancellationToken.None);

        return new CompanionPlaceAmbiguitySafetyResult(guarded, true, reasonCodes);
    }

    private static CompanionPlaceSearchStrategy ApplyGuard(
        CompanionPlaceSearchStrategy strategy,
        string? canonicalQuery,
        CompanionPlaceRoleIntent role,
        IReadOnlyList<string> queryVariants,
        IReadOnlyList<string> negativeRequirements)
    {
        var variants = queryVariants
            .Where(static query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select((query, index) => new CompanionPlaceSearchVariant(
                query,
                index == 0 ? "primary" : "role_disambiguation",
                strategy.Entity is not null,
                role.CategoryStrictness != "loose",
                index == 0 ? 0.72d : 0.62d))
            .ToArray();

        return strategy with
        {
            CanonicalQuery = string.IsNullOrWhiteSpace(canonicalQuery) ? strategy.CanonicalQuery : canonicalQuery,
            Role = role,
            SearchVariants = variants.Length == 0 ? strategy.SearchVariants : variants,
            NegativeRequirements = strategy.NegativeRequirements.Concat(negativeRequirements).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = strategy.Warnings.Concat(["ambiguity_safety_guard_applied"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static bool HasAny(string normalized, params string[] needles)
    {
        return needles.Any(needle => normalized.Contains(Normalize(needle), StringComparison.Ordinal));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }
}
