using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlaceRankingPolicyTests
{
    private readonly CompanionPlaceRankingPolicy sut = new();

    [Fact]
    public void Rank_GpsNearMe_PrefersCloserWhenRelevanceIsSimilar()
    {
        var close = BuildCandidate(
            placeId: "close",
            displayName: "Close Cafe",
            primaryType: "cafe",
            rating: 4.4,
            userRatingCount: 160,
            latitude: 53.3571,
            longitude: -6.4487);
        var far = BuildCandidate(
            placeId: "far",
            displayName: "Far Cafe",
            primaryType: "cafe",
            rating: 4.4,
            userRatingCount: 165,
            latitude: 53.3850,
            longitude: -6.5000);

        var ranked = sut.Rank(
            [far, close],
            new CompanionPlaceRankingContext(
                ApplyDistanceRanking: true,
                UserLatitude: 53.3570,
                UserLongitude: -6.4486,
                PlaceTypeHints: ["cafe"]));

        Assert.Equal("close", ranked.RankedCandidates[0].PlaceId);
        Assert.Contains("places_ranking:near_me_distance_ranked", ranked.Diagnostics);
        Assert.Contains("places_ranking:gps_distance_applied", ranked.Diagnostics);
    }

    [Fact]
    public void Rank_GpsNearMe_BalancesDistanceAndFit_NotDistanceOnly()
    {
        var veryCloseButWeak = BuildCandidate(
            placeId: "close-weak",
            displayName: "Close Weak Option",
            primaryType: "museum",
            rating: 3.2,
            userRatingCount: 4,
            latitude: 53.35702,
            longitude: -6.44861,
            openNow: false);
        var slightlyFartherStrong = BuildCandidate(
            placeId: "far-strong",
            displayName: "Far Strong Cafe",
            primaryType: "cafe",
            rating: 4.8,
            userRatingCount: 600,
            latitude: 53.3650,
            longitude: -6.4600,
            openNow: true);

        var ranked = sut.Rank(
            [veryCloseButWeak, slightlyFartherStrong],
            new CompanionPlaceRankingContext(
                ApplyDistanceRanking: true,
                UserLatitude: 53.3570,
                UserLongitude: -6.4486,
                PlaceTypeHints: ["cafe"]));

        Assert.Equal("far-strong", ranked.RankedCandidates[0].PlaceId);
    }

    [Fact]
    public void Rank_NonGpsNearbyFlow_NotAppliedAndOrderPreserved()
    {
        var first = BuildCandidate("first", "First", "museum", 4.3, 110, 53.3498, -6.2603);
        var second = BuildCandidate("second", "Second", "museum", 4.2, 90, 53.3600, -6.2700);

        var ranked = sut.Rank(
            [first, second],
            new CompanionPlaceRankingContext(
                ApplyDistanceRanking: false,
                UserLatitude: null,
                UserLongitude: null,
                PlaceTypeHints: ["museum"]));

        Assert.Equal("first", ranked.RankedCandidates[0].PlaceId);
        Assert.Contains("places_ranking:distance_not_applicable_non_gps", ranked.Diagnostics);
    }

    [Fact]
    public void Rank_GpsNearMe_MissingCoordinates_EmitsDiagnostic()
    {
        var missingCoordinates = BuildCandidate(
            placeId: "missing-location",
            displayName: "No Location",
            primaryType: "cafe",
            rating: 4.5,
            userRatingCount: 120,
            latitude: null,
            longitude: null);
        var withCoordinates = BuildCandidate(
            placeId: "with-location",
            displayName: "With Location",
            primaryType: "cafe",
            rating: 4.0,
            userRatingCount: 80,
            latitude: 53.3571,
            longitude: -6.4487);

        var ranked = sut.Rank(
            [missingCoordinates, withCoordinates],
            new CompanionPlaceRankingContext(
                ApplyDistanceRanking: true,
                UserLatitude: 53.3570,
                UserLongitude: -6.4486,
                PlaceTypeHints: ["cafe"]));

        Assert.Contains("places_ranking:distance_missing_for_some_candidates", ranked.Diagnostics);
        Assert.Equal("with-location", ranked.RankedCandidates[0].PlaceId);
    }

    private static CompanionPlaceCandidate BuildCandidate(
        string placeId,
        string displayName,
        string primaryType,
        double rating,
        int userRatingCount,
        double? latitude,
        double? longitude,
        bool? openNow = true)
    {
        return new CompanionPlaceCandidate(
            PlaceId: placeId,
            ResourceName: $"places/{placeId}",
            DisplayName: displayName,
            PrimaryType: primaryType,
            PrimaryTypeDisplayName: primaryType,
            Types: [primaryType],
            NationalPhoneNumber: null,
            FormattedAddress: "Address",
            ShortFormattedAddress: "Address",
            Rating: rating,
            UserRatingCount: userRatingCount,
            GoogleMapsUri: null,
            WebsiteUri: null,
            OpeningHours: new PlaceOpeningHoursSummary(
                OpenNow: openNow,
                WeekdayDescriptions: [],
                NextOpenTimeUtc: null),
            BusinessStatus: null,
            PriceLevel: null,
            IconMaskBaseUri: null,
            IconBackgroundColor: null,
            Takeout: null,
            Delivery: null,
            DineIn: null,
            Reservable: null,
            ServesBreakfast: null,
            ServesLunch: null,
            ServesDinner: null,
            ServesBeer: null,
            ServesWine: null,
            ServesBrunch: null,
            ServesVegetarianFood: null,
            OutdoorSeating: null,
            LiveMusic: null,
            MenuForChildren: null,
            ServesCocktails: null,
            ServesDessert: null,
            ServesCoffee: null,
            AllowsDogs: null,
            Restroom: null,
            GoodForGroups: null,
            GoodForWatchingSports: null,
            PaymentOptions: new PlacePaymentOptionsSummary(null, null, null, null),
            AccessibilityOptions: new PlaceAccessibilitySummary(null, null, null, null),
            EditorialSummary: new PlaceEditorialSummary(null, null),
            Location: latitude.HasValue && longitude.HasValue
                ? new PlaceLocationSummary(latitude.Value, longitude.Value)
                : null);
    }
}
