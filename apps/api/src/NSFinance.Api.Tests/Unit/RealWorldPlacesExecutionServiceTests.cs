using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldPlacesExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_MultiDomainCommerceRequest_UsesIsolatedDomainQueries()
    {
        var search = new TrackingDomainPlacesSearchService(
            resultsByDomain: new Dictionary<RealWorldDiscoveryDomain, IReadOnlyList<PlaceSearchItem>>
            {
                [RealWorldDiscoveryDomain.ElectronicsRetail] =
                [
                    BuildItem("tech-1", "Tech Hub", "Electronics", "electronics_store")
                ],
                [RealWorldDiscoveryDomain.ConvenienceStore] =
                [
                    BuildItem("quick-1", "Quick Mart", "Convenience Store", "convenience_store")
                ]
            });
        var sut = new RealWorldPlacesExecutionService(
            search,
            NullLogger<RealWorldPlacesExecutionService>.Instance);

        var result = await sut.ExecuteAsync(
            new RealWorldPlacesExecutionRequest(
                UserQuery: "where can i buy batteries",
                CountryCode: "IE",
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.357,
                    Longitude: -6.448,
                    RadiusMeters: 1500,
                    HasNearMeSemantic: false,
                    ImplicitLocalBias: true),
                Domains:
                [
                    RealWorldDiscoveryDomain.ElectronicsRetail,
                    RealWorldDiscoveryDomain.ConvenienceStore
                ],
                MaxDomains: 2,
                MaxItemsPerDomain: 4,
                MaxTotalItems: 8,
                Mode: RealWorldExecutionMode.FocusedThemeSearch,
                RetrievalPlan: new RealWorldPlaceRetrievalPlan(
                    Authoritative: true,
                    HasNearMeSemantic: false,
                    ExecutionMode: RealWorldExecutionMode.FocusedThemeSearch,
                    SelectedDomains:
                    [
                        RealWorldDiscoveryDomain.ElectronicsRetail,
                        RealWorldDiscoveryDomain.ConvenienceStore
                    ],
                    CanonicalConcepts: ["commerce_general"],
                    RequestedShortlistSize: 8,
                    IntentFamily: RealWorldIntentFamily.CommerceDiscovery,
                    CommerceProductHints: [],
                    EnableImplicitLocalBias: true)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, search.Calls.Count);
        Assert.NotEqual(search.Calls[0].Query, search.Calls[1].Query);
        Assert.Contains(result.ReasonCodes, code => code == "real_world_domain_retrieval_isolated:true");
        Assert.Contains(result.ReasonCodes, code => code == "real_world_retrieval_query_family:electronics_retail");
        Assert.Contains(result.ReasonCodes, code => code == "real_world_retrieval_query_family:convenience_store");
    }

    [Fact]
    public async Task ExecuteAsync_ExploratorySiblingDomains_DedupesBySemanticWinner()
    {
        var search = new TrackingDomainPlacesSearchService(
            resultsByDomain: new Dictionary<RealWorldDiscoveryDomain, IReadOnlyList<PlaceSearchItem>>
            {
                [RealWorldDiscoveryDomain.PubBar] =
                [
                    BuildItem("temple-bar", "Temple Bar Pub", "Pub", "bar"),
                    BuildItem("long-hall", "The Long Hall", "Pub", "bar")
                ],
                [RealWorldDiscoveryDomain.NightlifeGeneral] =
                [
                    BuildItem("temple-bar", "Temple Bar Pub", "Nightlife", "bar"),
                    BuildItem("club-noir", "Club Noir", "Nightlife", "night_club")
                ]
            });
        var sut = new RealWorldPlacesExecutionService(
            search,
            NullLogger<RealWorldPlacesExecutionService>.Instance);

        var result = await sut.ExecuteAsync(
            new RealWorldPlacesExecutionRequest(
                UserQuery: "where can i go drinking in dublin 2",
                CountryCode: "IE",
                LocationContext: new PlaceSearchLocationContext(
                    Source: "typed_area",
                    TypedArea: "dublin 2"),
                Domains:
                [
                    RealWorldDiscoveryDomain.PubBar,
                    RealWorldDiscoveryDomain.NightlifeGeneral
                ],
                MaxDomains: 2,
                MaxItemsPerDomain: 4,
                MaxTotalItems: 8,
                Mode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
                RetrievalPlan: new RealWorldPlaceRetrievalPlan(
                    Authoritative: true,
                    HasNearMeSemantic: false,
                    ExecutionMode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
                    SelectedDomains:
                    [
                        RealWorldDiscoveryDomain.PubBar,
                        RealWorldDiscoveryDomain.NightlifeGeneral
                    ],
                    CanonicalConcepts: ["pub_bar"],
                    RequestedShortlistSize: 8,
                    IntentFamily: RealWorldIntentFamily.ExploratoryAssistance)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("real_world_exploratory_per_domain_item_boost_applied", result.ReasonCodes);
        Assert.Contains("real_world_cross_domain_dedupe_applied", result.ReasonCodes);
        Assert.Contains("real_world_cross_domain_dedupe_winner:pubbar", result.ReasonCodes);
        var flattened = result.Groups.SelectMany(group => group.Items).ToArray();
        Assert.Equal(
            flattened.Length,
            flattened.Select(item => item.PlaceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task ExecuteAsync_CommercialDomainFilterRejectsTourismOnlyCandidates()
    {
        var search = new TrackingDomainPlacesSearchService(
            resultsByDomain: new Dictionary<RealWorldDiscoveryDomain, IReadOnlyList<PlaceSearchItem>>
            {
                [RealWorldDiscoveryDomain.ElectronicsRetail] =
                [
                    BuildItem("museum-1", "National Museum", "Museum", "museum"),
                    BuildItem("park-1", "City Park", "Park", "park")
                ]
            });
        var sut = new RealWorldPlacesExecutionService(
            search,
            NullLogger<RealWorldPlacesExecutionService>.Instance);

        var result = await sut.ExecuteAsync(
            new RealWorldPlacesExecutionRequest(
                UserQuery: "where can i buy a ps5",
                CountryCode: "IE",
                LocationContext: new PlaceSearchLocationContext(
                    Source: "gps",
                    Latitude: 53.357,
                    Longitude: -6.448),
                Domains:
                [
                    RealWorldDiscoveryDomain.ElectronicsRetail
                ],
                MaxDomains: 1,
                MaxItemsPerDomain: 4,
                MaxTotalItems: 8,
                Mode: RealWorldExecutionMode.FocusedPlaceSearch,
                RetrievalPlan: new RealWorldPlaceRetrievalPlan(
                    Authoritative: true,
                    HasNearMeSemantic: false,
                    ExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
                    SelectedDomains:
                    [
                        RealWorldDiscoveryDomain.ElectronicsRetail
                    ],
                    CanonicalConcepts: ["electronics_retail"],
                    RequestedShortlistSize: 8,
                    IntentFamily: RealWorldIntentFamily.CommerceDiscovery,
                    CommerceProductHints: ["ps5"])),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RealWorldFailureScenario.NoMatchesFound, result.FailureScenario);
        Assert.Contains(
            result.ReasonCodes,
            code => code == "real_world_places_domain_filter_zero_matches:electronicsretail");
    }

    private static PlaceSearchItem BuildItem(
        string placeId,
        string name,
        string category,
        string primaryType)
    {
        return new PlaceSearchItem(
            PlaceId: placeId,
            Name: name,
            Category: category,
            PriceLevel: null,
            PrimaryType: primaryType,
            PrimaryTypeDisplayName: category,
            Types: [primaryType]);
    }

    private sealed class TrackingDomainPlacesSearchService(
        IReadOnlyDictionary<RealWorldDiscoveryDomain, IReadOnlyList<PlaceSearchItem>> resultsByDomain) : IPlacesSearchService
    {
        public List<(string Query, RealWorldDiscoveryDomain? Domain)> Calls { get; } = [];

        public Task<PlaceSearchResult> SearchAsync(
            string query,
            string country,
            PlaceSearchLocationContext? locationContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = locationContext?.PlannerSelectedDomain;
            Calls.Add((query, domain));
            var items = domain is not null && resultsByDomain.TryGetValue(domain.Value, out var value)
                ? value
                : [];
            return Task.FromResult(
                new PlaceSearchResult(
                    Items: items,
                    Metadata: new PlaceSearchMetadata(
                        UseCase: "companion_discovery",
                        FromCache: false,
                        RequestedCandidateCount: 8,
                        ReturnedCandidateCount: items.Count,
                        FieldMaskVariant: "companion_discovery",
                        Elapsed: TimeSpan.FromMilliseconds(5),
                        TimedOut: false,
                        ProviderErrorCode: null),
                    Warnings: []));
        }
    }
}
