using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlacesResponseBuilderTests
{
    [Fact]
    public void Build_WithGroundedCandidates_ReturnsDeterministicBoundedShortlist()
    {
        var sut = new CompanionPlacesResponseBuilder(Options.Create(new CompanionOrchestrationOptions
        {
            MaxPlaceItems = 3
        }));
        var context = new FinancialCompanionContext(
            Intent: FinancialCompanionIntent.LocalPlacesOutings,
            Profile: BuildProfile(),
            ToolOutputs: new Dictionary<string, object?>
            {
                [CompanionTool.PlacesSearch.ToOutputKey()] = new CompanionPlacesSearchContext(
                    TotalItemCount: 5,
                    Items:
                    [
                        BuildPlaceItem("p1", "Cafe One", "Cafe", "PRICE_LEVEL_MODERATE", 4.5, 190, true, "1 Main St"),
                        BuildPlaceItem("p2", "Bistro Two", "Restaurant", "PRICE_LEVEL_INEXPENSIVE", 4.3, 150, false, "2 Main St"),
                        BuildPlaceItem("p3", "Bakery Three", "Bakery", "PRICE_LEVEL_INEXPENSIVE", null, null, null, "3 Main St"),
                        BuildPlaceItem("p4", "Diner Four", "Diner", "PRICE_LEVEL_MODERATE", null, null, null, "4 Main St"),
                        BuildPlaceItem("p5", "Pub Five", "Pub", "PRICE_LEVEL_EXPENSIVE", null, null, null, "5 Main St")
                    ]),
                [CompanionTool.BudgetStatus.ToOutputKey()] = new CompanionBudgetStatusContext(
                    HasBudgetPlan: true,
                    MonthlyBudget: 2000m,
                    MonthToDateSpend: 1300m,
                    RemainingBudget: 700m)
            },
            ToolsUsed: ["IPlacesSearchService", "IBudgetStatusService"]);

        var result = sut.Build(context);

        Assert.True(result.Succeeded);
        Assert.Contains("Cafe One", result.ReplyText, StringComparison.Ordinal);
        Assert.Contains("Bistro Two", result.ReplyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Diner Four", result.ReplyText, StringComparison.Ordinal);
        Assert.Contains("places_response_built_from_grounded_candidates", result.Warnings);
        Assert.Contains("places_response_candidate_count:3", result.Warnings);
    }

    [Fact]
    public void Build_WithoutCandidates_ReturnsExplicitInsufficiency()
    {
        var sut = new CompanionPlacesResponseBuilder(Options.Create(new CompanionOrchestrationOptions()));
        var context = new FinancialCompanionContext(
            Intent: FinancialCompanionIntent.LocalPlacesOutings,
            Profile: BuildProfile(),
            ToolOutputs: new Dictionary<string, object?>
            {
                [CompanionTool.PlacesSearch.ToOutputKey()] = new CompanionPlacesSearchContext(
                    TotalItemCount: 0,
                    Items: [])
            },
            ToolsUsed: []);

        var result = sut.Build(context);

        Assert.False(result.Succeeded);
        Assert.True(result.HasInsufficientData);
        Assert.Contains("local_places_intent_missing_places_grounding", result.InsufficientDataReasons);
        Assert.Contains("places_search_no_data", result.Warnings);
    }

    private static CompanionPlaceSearchContextItem BuildPlaceItem(
        string placeId,
        string name,
        string category,
        string priceLevel,
        double? rating,
        int? userRatingCount,
        bool? openNow,
        string shortAddress)
    {
        return new CompanionPlaceSearchContextItem(
            PlaceId: placeId,
            Name: name,
            Category: category,
            PriceLevel: priceLevel,
            PrimaryType: category.ToLowerInvariant(),
            PrimaryTypeDisplayName: category,
            Types: [category.ToLowerInvariant()],
            NationalPhoneNumber: null,
            FormattedAddress: null,
            ShortFormattedAddress: shortAddress,
            Rating: rating,
            UserRatingCount: userRatingCount,
            GoogleMapsUri: null,
            WebsiteUri: null,
            OpeningHours: openNow.HasValue
                ? new PlaceOpeningHoursSummary(
                    OpenNow: openNow,
                    WeekdayDescriptions: ["Monday: 9:00 AM - 5:00 PM"],
                    NextOpenTimeUtc: null)
                : null,
            BusinessStatus: "OPERATIONAL",
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
            PaymentOptions: null,
            AccessibilityOptions: null,
            EditorialSummary: null,
            Location: null);
    }

    private static UserFinancialContextSnapshot BuildProfile()
    {
        return new UserFinancialContextSnapshot(
            Country: "IE",
            Currency: "EUR",
            MonthlyIncomeRange: "2000-4000",
            KnownObligationsJson: "[]",
            BudgetStructureJson: "{}",
            ActivePlansJson: "[]",
            SpendingTendenciesJson: "[]",
            CategoryFlexibilityMarkersJson: "[]",
            AdviceStylePreference: "balanced");
    }
}
