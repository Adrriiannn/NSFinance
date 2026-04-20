using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldSearchScopeResolverTests
{
    private readonly LocalDiscoveryConstraintExtractor extractor = new();
    private readonly RealWorldSearchScopeResolver sut = new();

    [Fact]
    public void Resolve_ExplicitAreaInPrompt_OverridesDeviceScopeAndRetainsSecondaryDeviceContext()
    {
        var requestGrounding = new CompanionLocationGrounding(
            Source: "gps",
            Latitude: 53.3570,
            Longitude: -6.4486,
            RadiusMeters: 1500,
            TypedArea: null,
            LocalityLabel: "Lucan",
            AccuracyBucket: "high",
            CapturedAtUtc: DateTimeOffset.Parse("2026-04-20T18:00:00Z"));
        var requestLocalDiscovery = extractor.Extract("where can i go drinking in dublin 2?");

        var resolution = sut.Resolve(
            "where can i go drinking in dublin 2?",
            requestGrounding,
            requestLocalDiscovery,
            new RealWorldConversationSearchContextReadResult(
                Context: null,
                ContextReused: false,
                ContextExpired: false,
                DeviceLocationExpired: false,
                ReasonCodes: []));

        Assert.True(resolution.HasUsableScope);
        Assert.Equal(RealWorldSearchScopeKind.ExplicitArea, resolution.SearchScope);
        Assert.True(resolution.ExplicitAreaOverrodeDeviceLocation);
        Assert.Equal("dublin 2", resolution.ExplicitArea);
        Assert.False(resolution.EffectiveGrounding.HasCoordinates);
        Assert.Equal("dublin 2", resolution.EffectiveGrounding.TypedArea);
        Assert.True(resolution.SecondaryDeviceGrounding.HasCoordinates);
        Assert.Equal(53.3570, resolution.SecondaryDeviceGrounding.Latitude);
        Assert.Contains("real_world_search_scope:explicit_area", resolution.ReasonCodes);
        Assert.Contains(
            "real_world_search_scope:explicit_area_overrode_device_location",
            resolution.ReasonCodes);
    }

    [Fact]
    public void Resolve_ContextualFollowUp_ReusesPriorScopeAndPromotesLocalDiscoveryCandidate()
    {
        var now = DateTimeOffset.Parse("2026-04-20T18:00:00Z");
        var context = new RealWorldConversationSearchContext(
            UpdatedAtUtc: now,
            DeviceLatitude: 53.3570,
            DeviceLongitude: -6.4486,
            DeviceRadiusMeters: 1800,
            DeviceAccuracyBucket: "medium",
            DeviceCapturedAtUtc: now,
            DeviceLocalityLabel: "Lucan",
            DeviceSource: "gps",
            ExplicitArea: null,
            LastExecutionMode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            LastIntentFamily: RealWorldIntentFamily.ExploratoryAssistance,
            LastDomains: [RealWorldDiscoveryDomain.MovieTheater, RealWorldDiscoveryDomain.PubBar],
            AudienceHints: [],
            TimeHints: ["tonight"],
            PreferenceHints: [],
            NearbyOutingActive: true);
        var requestLocalDiscovery = extractor.Extract("with kids");

        var resolution = sut.Resolve(
            "with kids",
            new CompanionLocationGrounding(
                Source: null,
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: null,
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            requestLocalDiscovery,
            new RealWorldConversationSearchContextReadResult(
                Context: context,
                ContextReused: true,
                ContextExpired: false,
                DeviceLocationExpired: false,
                ReasonCodes: ["real_world_search_context_reused"]));

        Assert.True(resolution.HasUsableScope);
        Assert.Equal(RealWorldSearchScopeKind.DeviceLocation, resolution.SearchScope);
        Assert.True(resolution.EffectiveGrounding.HasCoordinates);
        Assert.True(resolution.EffectiveLocalDiscovery.IsLocalDiscoveryCandidate);
        Assert.Contains("tonight", resolution.EffectiveLocalDiscovery.TimeHints);
        Assert.Contains("real_world_search_context_reused", resolution.ReasonCodes);
        Assert.Contains("real_world_search_context_refinement_applied", resolution.ReasonCodes);
        Assert.Contains("real_world_search_context_hints_reused", resolution.ReasonCodes);
    }

    [Fact]
    public void Resolve_NoRequestAndNoContextScope_ReportsMissingScope()
    {
        var resolution = sut.Resolve(
            "what can i do later tonight?",
            new CompanionLocationGrounding(
                Source: null,
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: null,
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            extractor.Extract("what can i do later tonight?"),
            new RealWorldConversationSearchContextReadResult(
                Context: null,
                ContextReused: false,
                ContextExpired: false,
                DeviceLocationExpired: false,
                ReasonCodes: []));

        Assert.False(resolution.HasUsableScope);
        Assert.Equal(RealWorldSearchScopeKind.None, resolution.SearchScope);
        Assert.Contains("real_world_search_scope:missing", resolution.ReasonCodes);
    }
}
