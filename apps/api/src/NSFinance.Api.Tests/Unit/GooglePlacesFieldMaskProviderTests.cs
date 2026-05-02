using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesFieldMaskProviderTests
{
    private readonly GooglePlacesFieldMaskProvider sut = new();

    [Fact]
    public void CompanionDiscoveryMask_IsExplicitAndLightweight()
    {
        var mask = sut.CompanionDiscoverySearchMask;

        Assert.DoesNotContain("*", mask, StringComparison.Ordinal);
        Assert.Contains("places.displayName", mask, StringComparison.Ordinal);
        Assert.Contains("places.regularOpeningHours.openNow", mask, StringComparison.Ordinal);
        Assert.Contains("places.location", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.paymentOptions", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.accessibilityOptions", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.websiteUri", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.nationalPhoneNumber", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.reviews", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos.name", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos.widthPx", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos.heightPx", mask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.regularOpeningHours.periods", mask, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceDetailsMask_IncludesCardOnlyFieldsForFinalists()
    {
        var detailsMask = sut.PlaceDetailsMask;

        Assert.DoesNotContain("*", detailsMask, StringComparison.Ordinal);
        Assert.Contains("websiteUri", detailsMask, StringComparison.Ordinal);
        Assert.Contains("nationalPhoneNumber", detailsMask, StringComparison.Ordinal);
        Assert.Contains("photos.name", detailsMask, StringComparison.Ordinal);
        Assert.Contains("photos.widthPx", detailsMask, StringComparison.Ordinal);
        Assert.Contains("photos.heightPx", detailsMask, StringComparison.Ordinal);
    }

    [Fact]
    public void CompanionNearbyMask_IsExplicitAndNoWildcard()
    {
        var nearbyMask = sut.CompanionNearbySearchMask;

        Assert.DoesNotContain("*", nearbyMask, StringComparison.Ordinal);
        Assert.Contains("places.displayName", nearbyMask, StringComparison.Ordinal);
        Assert.Contains("places.location", nearbyMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.reviews", nearbyMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos.name", nearbyMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos.widthPx", nearbyMask, StringComparison.Ordinal);
        Assert.DoesNotContain("places.photos.heightPx", nearbyMask, StringComparison.Ordinal);
    }
}
