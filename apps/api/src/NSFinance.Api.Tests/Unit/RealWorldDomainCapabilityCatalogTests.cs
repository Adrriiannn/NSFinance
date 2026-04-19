using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldDomainCapabilityCatalogTests
{
    private readonly RealWorldDomainCapabilityCatalog catalog = new();

    [Fact]
    public void GetDomains_ContainsAllKnownRealWorldDomains()
    {
        var capabilities = catalog.GetDomains();
        var coveredDomains = capabilities.Select(static capability => capability.Domain).ToHashSet();
        var allDomains = Enum.GetValues<RealWorldDiscoveryDomain>();

        foreach (var domain in allDomains)
        {
            Assert.Contains(domain, coveredDomains);
        }
    }

    [Fact]
    public void Pharmacy_IsClassifiedAsEssentialServiceDomain()
    {
        Assert.True(catalog.TryGetDomain(RealWorldDiscoveryDomain.Pharmacy, out var capability));
        Assert.Equal(RealWorldDomainFamily.EssentialService, capability.Family);
        Assert.True(capability.SupportsServiceDiscovery);
        Assert.True(capability.SupportsEssentialService);
        Assert.True(capability.SuitableQuickErrand);
    }

    [Fact]
    public void Cafe_IsExploratoryAndThemeEligibleWithoutBeingGeneric()
    {
        Assert.True(catalog.TryGetDomain(RealWorldDiscoveryDomain.Cafe, out var capability));
        Assert.True(capability.SupportsExploratorySearch);
        Assert.True(capability.SupportsFocusedThemeSearch);
        Assert.False(capability.IsGeneric);
        Assert.Contains("cafe", capability.CanonicalConcepts);
    }

    [Fact]
    public void MetaDomains_AreExcludedFromDirectSearchEligibility()
    {
        Assert.True(catalog.TryGetDomain(RealWorldDiscoveryDomain.ExploratoryEveningActivity, out var eveningMeta));
        Assert.False(eveningMeta.SupportsExploratorySearch);
        Assert.False(eveningMeta.SupportsFocusedPlaceSearch);
        Assert.False(eveningMeta.NearMeAppropriate);

        Assert.True(catalog.TryGetDomain(RealWorldDiscoveryDomain.ExploratoryFamilyActivity, out var familyMeta));
        Assert.False(familyMeta.SupportsExploratorySearch);
        Assert.False(familyMeta.SupportsFocusedPlaceSearch);
        Assert.False(familyMeta.ExplicitLocalityAppropriate);
    }
}
