using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesCacheTests
{
    [Fact]
    public void InMemoryCache_ReturnsValueWithinTtl_AndExpiresAfterTtl()
    {
        var cache = new InMemoryGooglePlacesCache();
        var now = new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);
        var key = "k1";
        var value = new CompanionPlaceDiscoveryResult(
            Succeeded: true,
            Candidates: [],
            Metadata: new PlaceSearchMetadata(
                UseCase: "companion_discovery",
                FromCache: false,
                RequestedCandidateCount: 8,
                ReturnedCandidateCount: 0,
                FieldMaskVariant: "companion_discovery_v1",
                Elapsed: TimeSpan.FromMilliseconds(12),
                TimedOut: false),
            Warnings: []);

        cache.Set(key, value, now, TimeSpan.FromSeconds(30));

        var foundBeforeExpiry = cache.TryGet(key, now.AddSeconds(29), out CompanionPlaceDiscoveryResult cachedBeforeExpiry);
        var foundAfterExpiry = cache.TryGet(key, now.AddSeconds(31), out CompanionPlaceDiscoveryResult _);

        Assert.True(foundBeforeExpiry);
        Assert.Same(value, cachedBeforeExpiry);
        Assert.False(foundAfterExpiry);
    }

    [Fact]
    public void CacheKeyBuilder_NormalizesInputForDeterministicKeys()
    {
        var keyBuilder = new GooglePlacesCacheKeyBuilder();
        var keyA = keyBuilder.BuildCompanionDiscoveryKey(
            new CompanionPlaceDiscoveryRequest(
                Query: "  Coffee Near Me ",
                CountryCode: "IE",
                LanguageCode: "en",
                Latitude: 53.349805,
                Longitude: -6.26031,
                RadiusMeters: 2500),
            maxCandidates: 8);
        var keyB = keyBuilder.BuildCompanionDiscoveryKey(
            new CompanionPlaceDiscoveryRequest(
                Query: "coffee near me",
                CountryCode: "ie",
                LanguageCode: "EN",
                Latitude: 53.349805,
                Longitude: -6.26031,
                RadiusMeters: 2500),
            maxCandidates: 8);

        Assert.Equal(keyA, keyB);
        Assert.StartsWith("companion_discovery:", keyA, StringComparison.Ordinal);
    }
}
