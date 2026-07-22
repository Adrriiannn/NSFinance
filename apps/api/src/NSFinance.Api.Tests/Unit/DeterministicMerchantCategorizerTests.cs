using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Tests.Unit;

public sealed class DeterministicMerchantCategorizerTests
{
    [Fact]
    public void ProviderPrefixedIrishMerchants_MatchTheirCategories()
    {
        var tesco = DeterministicMerchantCategorizer.Match("VDC-TESCO STORES 3", -33.75m);
        Assert.NotNull(tesco);
        Assert.Equal(13010, tesco.TaxonomyCategoryId);
        Assert.Equal("TESCO", tesco.MatchedSignal);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, tesco.CharacteristicsVersion);

        var fuel = DeterministicMerchantCategorizer.Match("VDC-APPLEGREEN SANDYFORD", -62.4m);
        Assert.NotNull(fuel);
        Assert.Equal(12020, fuel.TaxonomyCategoryId);

        // Full-coverage rebalance: LEAP is owned by the Transit Passes
        // subcategory now, which is the more precise assignment.
        var leap = DeterministicMerchantCategorizer.Match("POS TFI LEAP TOPUP", -20m);
        Assert.NotNull(leap);
        Assert.Equal(120106, leap.TaxonomySubcategoryId);
    }

    [Fact]
    public void DirectionExpectations_AreEnforced()
    {
        // A refund from Tesco is inflow; the outflow-only groceries rule must not fire.
        Assert.Null(DeterministicMerchantCategorizer.Match("VDC-TESCO STORES 3", 12.5m));

        // Salary signals only match inflows.
        var salary = DeterministicMerchantCategorizer.Match("ACME LTD PAYROLL JUL", 3200m);
        Assert.NotNull(salary);
        Assert.Equal(910101, salary.TaxonomySubcategoryId);
        Assert.Null(DeterministicMerchantCategorizer.Match("ACME LTD PAYROLL REVERSAL", -3200m));
    }

    [Fact]
    public void LongestSignalWins_AndUnknownTextStaysUnmatched()
    {
        var health = DeterministicMerchantCategorizer.Match("IRISH LIFE HEALTH DD", -180m);
        Assert.NotNull(health);
        Assert.Equal(15010, health.TaxonomyCategoryId);
        Assert.Equal("IRISH LIFE HEALTH", health.MatchedSignal);

        Assert.Null(DeterministicMerchantCategorizer.Match("SOME UNKNOWN MERCHANT 42", -10m));
        Assert.Null(DeterministicMerchantCategorizer.Match("", -10m));
    }

    [Fact]
    public void RelationshipOnlyCategories_NeverMatchByText()
    {
        // "Internal transfer"-looking text must not trip the deterministic-only
        // Internal Transfers category, which has no merchant signals by design.
        var match = DeterministicMerchantCategorizer.Match("INTERNAL TRANSFER OWN ACCOUNT", -100m);
        Assert.True(match is null || match.TaxonomyCategoryId != 92010);
    }
}
