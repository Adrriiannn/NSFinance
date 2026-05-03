using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

internal sealed class TestGuardCatalogueProvider : ICompanionAmbiguityGuardCatalogueProvider
{
    public IReadOnlyList<CompanionAmbiguityGuardDefinition> GetAll()
    {
        return TestGuardCatalogueProviderShared.Guards;
    }
}

internal static class TestGuardCatalogueProviderShared
{
    public static readonly IReadOnlyList<CompanionAmbiguityGuardDefinition> Guards =
    [
        Guard("bank_branch_vs_atm", "finance", ["bank branch", "bank", "bank_branch"], ["atm", "cash machine"], ["bank", "financial institution"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("atm_vs_bank_branch", "finance", ["atm"], ["bank branch", "bank"], ["atm"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("post_office_vs_mailbox", "postal", ["post office", "post_office"], ["mailbox", "post box", "parcel locker"], ["post office"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("hotel_vs_hotel_restaurant", "accommodation", ["hotel", "lodging"], ["restaurant", "bar"], ["hotel", "lodging"], ["types", "primaryType"], "soft_penalty_only"),
        Guard("car_park_vs_public_park", "transport", ["car park", "parking"], ["park", "tourist attraction"], ["parking", "parking lot", "parking garage"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("car_park_vs_park", "transport", ["car park", "parking"], ["park", "tourist attraction"], ["parking", "parking lot", "parking garage"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("fine_dining_vs_fast_food", "food", ["fine dining", "upscale"], ["fast food", "takeaway", "cafe"], ["restaurant"], ["types", "primaryType", "priceLevel"], "ranking_guard"),
        Guard("restaurant_delivery_vs_dine_in_only", "food", ["delivery", "delivery restaurant", "food delivery"], ["dine in only", "takeaway not available"], ["restaurant", "meal delivery"], ["delivery", "takeout", "dineIn"], "enrich_before_reject", requiresDetails: true),
        Guard("takeaway_vs_dine_in_only", "food", ["takeaway", "takeout"], ["dine in only", "takeout false"], ["meal takeaway", "restaurant"], ["takeout", "delivery", "dineIn"], "enrich_before_reject", requiresDetails: true),
        Guard("dog_friendly_policy", "accessibility", ["dog friendly", "dogs allowed", "dog_friendly"], ["dogs not allowed"], ["allows dogs"], ["allowsDogs"], "enrich_before_reject", requiresDetails: true),
        Guard("wheelchair_accessibility", "accessibility", ["wheelchair", "wheelchair accessible", "accessible"], ["not accessible"], ["wheelchair accessible"], ["accessibilityOptions"], "enrich_before_reject", requiresDetails: true),
        Guard("outdoor_seating", "food", ["outdoor seating", "outside seating"], ["no outdoor seating"], ["outdoor seating"], ["outdoorSeating"], "enrich_before_reject", requiresDetails: true),
        Guard("card_payments", "payments", ["card", "card payments", "cashless"], ["cash only"], ["credit cards", "debit cards", "nfc"], ["paymentOptions"], "enrich_before_reject", requiresDetails: true),
        Guard("parking_availability", "parking", ["parking", "free parking", "parking available"], ["no parking"], ["parking", "nearby parking"], ["parkingOptions", "nearbyParking"], "enrich_before_reject", requiresDetails: true)
    ];

    private static CompanionAmbiguityGuardDefinition Guard(
        string guardId,
        string domain,
        IReadOnlyList<string> requested,
        IReadOnlyList<string> dangerous,
        IReadOnlyList<string> compatible,
        IReadOnlyList<string> fields,
        string action,
        bool requiresDetails = false)
    {
        return new CompanionAmbiguityGuardDefinition(guardId, domain, requested, dangerous, compatible, fields, action, requiresDetails, 0.9d, [], null);
    }
}
