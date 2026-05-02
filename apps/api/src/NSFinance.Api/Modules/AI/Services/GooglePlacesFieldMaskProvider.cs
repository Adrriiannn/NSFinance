namespace NSFinance.Api.Modules.AI.Services;

public interface IGooglePlacesFieldMaskProvider
{
    string CompanionDiscoverySearchMask { get; }
    string CompanionNearbySearchMask { get; }
    string MerchantLookupSearchMask { get; }
    string PlaceDetailsMask { get; }
}

public sealed class GooglePlacesFieldMaskProvider : IGooglePlacesFieldMaskProvider
{
    public string CompanionDiscoverySearchMask { get; } = string.Join(
        ",",
        [
            "places.id",
            "places.name",
            "places.displayName",
            "places.primaryType",
            "places.primaryTypeDisplayName",
            "places.types",
            "places.shortFormattedAddress",
            "places.rating",
            "places.userRatingCount",
            "places.regularOpeningHours.openNow",
            "places.businessStatus",
            "places.priceLevel",
            "places.location"
        ]);

    public string CompanionNearbySearchMask { get; } = string.Join(
        ",",
        [
            "places.id",
            "places.name",
            "places.displayName",
            "places.primaryType",
            "places.primaryTypeDisplayName",
            "places.types",
            "places.shortFormattedAddress",
            "places.rating",
            "places.userRatingCount",
            "places.regularOpeningHours.openNow",
            "places.businessStatus",
            "places.priceLevel",
            "places.location"
        ]);

    public string MerchantLookupSearchMask { get; } = string.Join(
        ",",
        [
            "places.id",
            "places.name",
            "places.displayName",
            "places.primaryType",
            "places.primaryTypeDisplayName",
            "places.types",
            "places.formattedAddress",
            "places.shortFormattedAddress",
            "places.location",
            "places.googleMapsUri",
            "places.websiteUri",
            "places.nationalPhoneNumber",
            "places.businessStatus",
            "places.rating",
            "places.userRatingCount"
        ]);

    public string PlaceDetailsMask { get; } = string.Join(
        ",",
        [
            "id",
            "name",
            "displayName",
            "primaryType",
            "primaryTypeDisplayName",
            "types",
            "nationalPhoneNumber",
            "formattedAddress",
            "shortFormattedAddress",
            "rating",
            "userRatingCount",
            "googleMapsUri",
            "websiteUri",
            "regularOpeningHours.openNow",
            "regularOpeningHours.weekdayDescriptions",
            "regularOpeningHours.nextOpenTime",
            "businessStatus",
            "priceLevel",
            "iconMaskBaseUri",
            "iconBackgroundColor",
            "takeout",
            "delivery",
            "dineIn",
            "reservable",
            "servesBreakfast",
            "servesLunch",
            "servesDinner",
            "servesBeer",
            "servesWine",
            "servesBrunch",
            "servesVegetarianFood",
            "editorialSummary",
            "outdoorSeating",
            "liveMusic",
            "menuForChildren",
            "servesCocktails",
            "servesDessert",
            "servesCoffee",
            "allowsDogs",
            "restroom",
            "goodForGroups",
            "goodForWatchingSports",
            "paymentOptions",
            "accessibilityOptions",
            "photos.name",
            "photos.widthPx",
            "photos.heightPx",
            "location"
        ]);
}
