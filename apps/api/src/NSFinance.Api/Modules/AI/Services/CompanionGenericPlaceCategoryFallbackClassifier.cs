using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionGenericPlaceCategoryFallbackClassifier : ICompanionGenericPlaceCategoryFallbackClassifier
{
    private static readonly CompanionFallbackCategoryClassification NoMatch = new(
        false,
        null,
        new CompanionPlaceRoleIntent(null, [], [], [], [], "loose"),
        [],
        []);

    public CompanionFallbackCategoryClassification Classify(string userMessage, CompanionSemanticIntent intent)
    {
        var normalized = Normalize($"{userMessage} {intent.PlaceQuery}");
        if (HasAny(normalized, "bike repair", "bicycle repair", "cycle repair"))
        {
            return Category(
                "bike repair",
                new CompanionPlaceRoleIntent("bicycle_store", ["bicycle_store", "store"], ["bicycle_store", "bike_shop", "cycle_shop", "bicycle_repair_shop"], [], ["repair"], "compatible"),
                ["bicycle repair shop", "bike repair", "cycle repair"]);
        }

        if (HasAny(normalized, "bike shop", "bike shops", "bicycle shop", "bicycle shops", "cycle shop", "cycle shops", "bicycle store"))
        {
            return Category(
                "bike shops",
                new CompanionPlaceRoleIntent("bicycle_store", ["bicycle_store", "store"], ["bicycle_store", "bike_shop", "cycle_shop", "bicycle_repair_shop"], [], [], "compatible"),
                ["bike shop", "bicycle store", "cycle shop"]);
        }

        if (HasAny(normalized, "coffee shop", "coffee shops", "cafe", "cafes"))
        {
            return Category(
                "coffee shops",
                new CompanionPlaceRoleIntent("coffee_shop", ["coffee_shop", "cafe"], ["coffee_shop", "cafe"], [], [], "compatible"),
                ["coffee shops", "cafe", "coffee"]);
        }

        if (HasAny(normalized, "fine dining", "fancy restaurant", "upscale restaurant"))
        {
            return Category(
                "fine dining restaurants",
                new CompanionPlaceRoleIntent("restaurant", ["restaurant"], ["restaurant", "fine_dining_restaurant", "irish_restaurant", "french_restaurant", "asian_restaurant", "european_restaurant", "italian_restaurant", "seafood_restaurant"], ["fast_food_restaurant", "meal_takeaway", "cafe"], ["fine_dining", "upscale"], "compatible"),
                ["fine dining restaurants", "upscale restaurants"],
                ["fast_food_restaurant", "meal_takeaway"]);
        }

        if (HasAny(normalized, "restaurant", "restaurants"))
        {
            return Category(
                "restaurants",
                new CompanionPlaceRoleIntent("restaurant", ["restaurant"], ["restaurant"], ["fast_food_restaurant", "meal_takeaway"], [], "compatible"),
                ["restaurants"]);
        }

        if (HasAny(normalized, "car park", "car parks", "parking", "parking lot", "parking garage"))
        {
            return Category(
                "car parks",
                new CompanionPlaceRoleIntent("parking", ["parking"], ["parking", "parking_lot", "parking_garage"], ["park", "tourist_attraction"], [], "strict"),
                ["car parks", "parking", "parking garage"],
                ["park"]);
        }

        if (HasAny(normalized, "post office", "post offices"))
        {
            return Category("post offices", new CompanionPlaceRoleIntent("post_office", ["post_office"], ["post_office"], ["mailbox"], [], "strict"), ["post offices"]);
        }

        if (HasAny(normalized, "pharmacy", "pharmacies"))
        {
            return Category("pharmacies", new CompanionPlaceRoleIntent("pharmacy", ["pharmacy"], ["pharmacy"], ["hospital"], [], "strict"), ["pharmacies"]);
        }

        if (HasAny(normalized, "gym", "gyms", "fitness centre", "fitness center"))
        {
            return Category("gyms", new CompanionPlaceRoleIntent("gym", ["gym"], ["gym", "fitness_center"], [], [], "compatible"), ["gyms", "fitness centre"]);
        }

        if (HasAny(normalized, "museum", "museums"))
        {
            return Category("museums", new CompanionPlaceRoleIntent("museum", ["museum"], ["museum"], [], [], "compatible"), ["museums"]);
        }

        if (HasAny(normalized, "hotel", "hotels"))
        {
            return Category("hotels", new CompanionPlaceRoleIntent("hotel", ["lodging"], ["hotel", "lodging"], ["restaurant", "bar"], [], "strict"), ["hotels"]);
        }

        if (HasAny(normalized, "petrol station", "petrol stations", "gas station", "fuel station"))
        {
            return Category("petrol stations", new CompanionPlaceRoleIntent("gas_station", ["gas_station"], ["gas_station"], ["car_wash"], [], "strict"), ["petrol stations", "fuel stations"]);
        }

        if (HasAny(normalized, "library", "libraries"))
        {
            return Category("libraries", new CompanionPlaceRoleIntent("library", ["library"], ["library"], [], [], "compatible"), ["libraries"]);
        }

        if (HasAny(normalized, "book shop", "book shops", "bookstore", "bookstores"))
        {
            return Category("book shops", new CompanionPlaceRoleIntent("book_store", ["book_store", "store"], ["book_store"], [], [], "compatible"), ["book shops", "bookstore"]);
        }

        if (HasAny(normalized, "barber", "barbers", "hairdresser", "hairdressers"))
        {
            return Category("barbers", new CompanionPlaceRoleIntent("hair_care", ["hair_care"], ["hair_care", "barber_shop"], [], [], "compatible"), ["barbers", "hairdressers"]);
        }

        if (HasAny(normalized, "vet", "vets", "veterinary"))
        {
            return Category("vets", new CompanionPlaceRoleIntent("veterinary_care", ["veterinary_care"], ["veterinary_care"], [], [], "strict"), ["vets", "veterinary clinic"]);
        }

        if (HasAny(normalized, "dentist", "dentists"))
        {
            return Category("dentists", new CompanionPlaceRoleIntent("dentist", ["dentist"], ["dentist"], [], [], "strict"), ["dentists"]);
        }

        if (HasAny(normalized, "doctor", "doctors", "clinic", "clinics"))
        {
            return Category("clinics", new CompanionPlaceRoleIntent("doctor", ["doctor"], ["doctor", "clinic"], ["hospital"], [], "compatible"), ["doctors", "clinics"]);
        }

        return NoMatch;
    }

    private static CompanionFallbackCategoryClassification Category(
        string canonicalQuery,
        CompanionPlaceRoleIntent role,
        IReadOnlyList<string> variants,
        IReadOnlyList<string>? negativeRequirements = null)
    {
        return new CompanionFallbackCategoryClassification(
            true,
            canonicalQuery,
            role,
            variants.Select((query, index) => new CompanionPlaceSearchVariant(
                query,
                index == 0 ? "primary" : "role_disambiguation",
                false,
                role.CategoryStrictness != "loose",
                index == 0 ? 0.86d : 0.72d)).ToArray(),
            negativeRequirements ?? []);
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
