namespace NSFinance.Api.Modules.AI.Services;

public sealed record PlaceLocationSummary(
    double Latitude,
    double Longitude);

public sealed record PlaceOpeningHoursSummary(
    bool? OpenNow,
    IReadOnlyList<string> WeekdayDescriptions,
    DateTimeOffset? NextOpenTimeUtc);

public sealed record PlacePaymentOptionsSummary(
    bool? AcceptsCreditCards,
    bool? AcceptsDebitCards,
    bool? AcceptsCashOnly,
    bool? AcceptsNfc);

public sealed record PlaceAccessibilitySummary(
    bool? WheelchairAccessibleParking,
    bool? WheelchairAccessibleEntrance,
    bool? WheelchairAccessibleRestroom,
    bool? WheelchairAccessibleSeating);

public sealed record PlaceEditorialSummary(
    string? Text,
    string? LanguageCode);

public sealed record PlaceSearchMetadata(
    string UseCase,
    bool FromCache,
    int RequestedCandidateCount,
    int ReturnedCandidateCount,
    string FieldMaskVariant,
    TimeSpan Elapsed,
    bool TimedOut,
    string? ProviderErrorCode = null);

public sealed record CompanionPlaceCandidate(
    string PlaceId,
    string ResourceName,
    string DisplayName,
    string? PrimaryType,
    string? PrimaryTypeDisplayName,
    IReadOnlyList<string> Types,
    string? NationalPhoneNumber,
    string? FormattedAddress,
    string? ShortFormattedAddress,
    double? Rating,
    int? UserRatingCount,
    string? GoogleMapsUri,
    string? WebsiteUri,
    PlaceOpeningHoursSummary OpeningHours,
    string? BusinessStatus,
    string? PriceLevel,
    string? IconMaskBaseUri,
    string? IconBackgroundColor,
    bool? Takeout,
    bool? Delivery,
    bool? DineIn,
    bool? Reservable,
    bool? ServesBreakfast,
    bool? ServesLunch,
    bool? ServesDinner,
    bool? ServesBeer,
    bool? ServesWine,
    bool? ServesBrunch,
    bool? ServesVegetarianFood,
    bool? OutdoorSeating,
    bool? LiveMusic,
    bool? MenuForChildren,
    bool? ServesCocktails,
    bool? ServesDessert,
    bool? ServesCoffee,
    bool? AllowsDogs,
    bool? Restroom,
    bool? GoodForGroups,
    bool? GoodForWatchingSports,
    PlacePaymentOptionsSummary PaymentOptions,
    PlaceAccessibilitySummary AccessibilityOptions,
    PlaceEditorialSummary EditorialSummary,
    PlaceLocationSummary? Location);

public sealed record CompanionPlaceDiscoveryRequest(
    string Query,
    string? CountryCode,
    string? LanguageCode = null,
    double? Latitude = null,
    double? Longitude = null,
    int? RadiusMeters = null,
    int? MaxCandidates = null);

public sealed record CompanionNearbyDiscoveryRequest(
    double Latitude,
    double Longitude,
    int RadiusMeters,
    IReadOnlyList<string> IncludedTypes,
    string? CountryCode = null,
    string? LanguageCode = null,
    int? MaxCandidates = null);

public sealed record CompanionPlaceDiscoveryResult(
    bool Succeeded,
    IReadOnlyList<CompanionPlaceCandidate> Candidates,
    PlaceSearchMetadata Metadata,
    IReadOnlyList<string> Warnings);

public sealed record MerchantPlaceLookupRequest(
    string MerchantDescriptor,
    string? CountryCode,
    string? LanguageCode = null,
    int MaxCandidates = 3);

public sealed record MerchantPlaceMatch(
    string PlaceId,
    string ResourceName,
    string DisplayName,
    string? PrimaryType,
    string? PrimaryTypeDisplayName,
    IReadOnlyList<string> Types,
    string? FormattedAddress,
    string? ShortFormattedAddress,
    string? GoogleMapsUri,
    string? WebsiteUri,
    string? NationalPhoneNumber,
    string? BusinessStatus,
    double? Rating,
    int? UserRatingCount,
    PlaceLocationSummary? Location);

public sealed record MerchantPlaceLookupResult(
    bool Succeeded,
    IReadOnlyList<MerchantPlaceMatch> Matches,
    PlaceSearchMetadata Metadata,
    IReadOnlyList<string> Warnings);

public interface ICompanionPlaceDiscoveryService
{
    Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
        CompanionPlaceDiscoveryRequest request,
        CancellationToken cancellationToken);

    Task<CompanionPlaceDiscoveryResult> DiscoverNearbyAsync(
        CompanionNearbyDiscoveryRequest request,
        CancellationToken cancellationToken);
}

public interface IMerchantPlaceLookupService
{
    Task<MerchantPlaceLookupResult> LookupAsync(
        MerchantPlaceLookupRequest request,
        CancellationToken cancellationToken);
}
