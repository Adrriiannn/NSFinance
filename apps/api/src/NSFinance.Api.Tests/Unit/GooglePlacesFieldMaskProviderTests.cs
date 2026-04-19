using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesFieldMaskProviderTests
{
    private readonly GooglePlacesFieldMaskProvider sut = new();

    [Fact]
    public void CompanionDiscoveryMask_IsExplicitAndExcludesHeavyFields()
    {
        var mask = sut.CompanionDiscoverySearchMask;

        Assert.DoesNotContain("*", mask, StringComparison.Ordinal);
        Assert.Contains("places.displayName", mask, StringComparison.Ordinal);
        Assert.Contains("places.regularOpeningHours.openNow", mask, StringComparison.Ordinal);
        Assert.Contains("places.paymentOptions", mask, StringComparison.Ordinal);
        Assert.Contains("places.accessibilityOptions", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.reviews", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.regularOpeningHours.periods", mask, StringComparison.Ordinal);
    }

    [Fact]
    public void MerchantLookupMask_IsExplicitAndLeanerThanCompanionDiscovery()
    {
        var merchantMask = sut.MerchantLookupSearchMask;
        var companionMask = sut.CompanionDiscoverySearchMask;

        Assert.DoesNotContain("*", merchantMask, StringComparison.Ordinal);
        Assert.Contains("places.displayName", merchantMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.editorialSummary", merchantMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.paymentOptions", merchantMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.accessibilityOptions", merchantMask, StringComparison.Ordinal);
        Assert.True(merchantMask.Split(',').Length < companionMask.Split(',').Length);
    }

    [Fact]
    public void CompanionNearbyMask_IsExplicitAndNoWildcard()
    {
        var nearbyMask = sut.CompanionNearbySearchMask;

        Assert.DoesNotContain("*", nearbyMask, StringComparison.Ordinal);
        Assert.Contains("places.displayName", nearbyMask, StringComparison.Ordinal);
        Assert.Contains("places.location", nearbyMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.reviews", nearbyMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos", nearbyMask, StringComparison.Ordinal);
    }
}
