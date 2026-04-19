using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesCompanionSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_WithGpsGrounding_ForwardsCoordinatesToDiscoveryRequest()
    {
        var discovery = new TrackingCompanionDiscoveryService();
        var sut = new GooglePlacesCompanionSearchService(
            discovery,
            new LocalDiscoveryQueryShaper(new LocalDiscoveryConstraintExtractor()),
            NullLogger<GooglePlacesCompanionSearchService>.Instance);

        await sut.SearchAsync(
            "kids playgrounds near me",
            "IE",
            new PlaceSearchLocationContext(
                Source: "gps",
                Latitude: 53.3498,
                Longitude: -6.2603,
                RadiusMeters: 2500),
            CancellationToken.None);

        var request = Assert.Single(discovery.Requests);
        Assert.Equal(53.3498, request.Latitude);
        Assert.Equal(-6.2603, request.Longitude);
        Assert.Equal(2500, request.RadiusMeters);
    }

    [Fact]
    public async Task SearchAsync_WithTypedArea_ShapesQueryWithArea()
    {
        var discovery = new TrackingCompanionDiscoveryService();
        var sut = new GooglePlacesCompanionSearchService(
            discovery,
            new LocalDiscoveryQueryShaper(new LocalDiscoveryConstraintExtractor()),
            NullLogger<GooglePlacesCompanionSearchService>.Instance);

        var result = await sut.SearchAsync(
            "places to visit near me",
            "IE",
            new PlaceSearchLocationContext(
                Source: "typed_area",
                TypedArea: "Dublin city centre"),
            CancellationToken.None);

        var request = Assert.Single(discovery.Requests);
        Assert.Contains("dublin city centre", request.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            result.Warnings ?? [],
            warning => warning.StartsWith("places_query_shape:", StringComparison.Ordinal));
    }

    private sealed class TrackingCompanionDiscoveryService : ICompanionPlaceDiscoveryService
    {
        public List<CompanionPlaceDiscoveryRequest> Requests { get; } = [];

        public Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
            CompanionPlaceDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(
                new CompanionPlaceDiscoveryResult(
                    Succeeded: true,
                    Candidates: [],
                    Metadata: new PlaceSearchMetadata(
                        UseCase: "companion_discovery",
                        FromCache: false,
                        RequestedCandidateCount: 8,
                        ReturnedCandidateCount: 0,
                        FieldMaskVariant: "companion_discovery_v1",
                        Elapsed: TimeSpan.FromMilliseconds(5),
                        TimedOut: false,
                        ProviderErrorCode: null),
                    Warnings: []));
        }
    }
}
