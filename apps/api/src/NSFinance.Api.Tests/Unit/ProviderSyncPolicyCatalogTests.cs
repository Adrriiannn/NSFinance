using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Models;

namespace NSFinance.Api.Tests.Unit;

public class ProviderSyncPolicyCatalogTests
{
    [Theory]
    [InlineData("ob-aib", "AIB", "aib", "irish_capped_slice", ProviderTransactionVisibilityMode.CappedVisibleSlice)]
    [InlineData("ob-boi-ie", "Bank of Ireland", "boi", "irish_retail_standard", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-ptsb", "PTSB", "ptsb", "irish_mixed_history", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-revolut-ie", "REVOLUT-IE", "revolut", "fintech_revolut", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-monzo", "Monzo", "monzo", "fintech_monzo", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-starling", "Starling", "starling", "fintech_starling", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-santander", "Santander", "santander", "uk_retail_santander", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-rbs", "The Royal Bank of Scotland", "natwest_family", "uk_natwest_rbs_ulster_family", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-ulster", "Ulster Bank", "natwest_family", "uk_natwest_rbs_ulster_family", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-halifax", "Halifax", "lloyds_family", "uk_lloyds_halifax_bos_mbna_family", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-hsbc", "HSBC", "hsbc_family", "uk_hsbc_firstdirect_ms_family", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-barclays", "Barclays", "barclays_family", "uk_barclays_barclaycard_family", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-transferwise", "Wise", "wise", "fintech_wise", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-tide", "Tide", "tide", "fintech_tide_business", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-mettle", "Mettle Bank", "mettle_zempler", "fintech_business_banking", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-cashplus", "Zempler (formerly Cashplus)", "mettle_zempler", "fintech_business_banking", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-chelsea-building-society", "Chelsea Building Society", "building_society", "uk_building_society", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-yorkshire-building-society", "Yorkshire Building Society", "building_society", "uk_building_society", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-tsb", "TSB", "building_society", "uk_building_society", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-danske", "Danske Bank", "danske", "uk_danske", ProviderTransactionVisibilityMode.DateHistory)]
    [InlineData("ob-nationwide", "Nationwide", "nationwide", "uk_nationwide", ProviderTransactionVisibilityMode.DateHistory)]
    public void ResolveForAccount_MapsKnownProvidersToExpectedPolicyFamily(
        string providerId,
        string providerDisplayName,
        string expectedPolicyKey,
        string expectedFamily,
        ProviderTransactionVisibilityMode expectedVisibilityMode)
    {
        var account = BuildAccount(providerId, providerDisplayName);

        var policy = ProviderSyncPolicyCatalog.ResolveForAccount(account);

        Assert.Equal(expectedPolicyKey, policy.ProviderKey);
        Assert.Equal(expectedFamily, policy.ProviderFamily);
        Assert.Equal(expectedVisibilityMode, policy.VisibilityMode);
    }

    [Fact]
    public void ResolveForAccount_Aib_UsesCappedVisibleSliceAndRescanStrategy()
    {
        var account = BuildAccount("ob-aib", "Allied Irish Bank");

        var policy = ProviderSyncPolicyCatalog.ResolveForAccount(account);

        Assert.Equal("aib", policy.ProviderKey);
        Assert.Equal(ProviderTransactionVisibilityMode.CappedVisibleSlice, policy.VisibilityMode);
        Assert.Equal(100, policy.SettledResponseCap);
        Assert.True(policy.ReScanVisibleSliceEachSync);
        Assert.True(policy.MaxAdaptiveSplitDepth > 0);
    }

    [Fact]
    public void ResolveForAccount_Santander_DeclaresPendingUnsupported()
    {
        var account = BuildAccount("ob-santander", "Santander");

        var policy = ProviderSyncPolicyCatalog.ResolveForAccount(account);

        Assert.Equal("santander", policy.ProviderKey);
        Assert.Equal(ProviderPendingSupportMode.Unsupported, policy.PendingSupport);
    }

    [Fact]
    public void ResolveForConnection_UsesDisplayNameFallbackWhenProviderIdMissing()
    {
        var policy = ProviderSyncPolicyCatalog.ResolveForConnection(null, "Revolut");

        Assert.Equal("revolut", policy.ProviderKey);
        Assert.Equal("fintech_revolut", policy.ProviderFamily);
    }

    [Fact]
    public void ResolveForConnection_UnknownProvider_UsesDefaultPolicy()
    {
        var policy = ProviderSyncPolicyCatalog.ResolveForConnection("ob-unknown-bank", "Unknown Bank");

        Assert.Equal("default", policy.ProviderKey);
        Assert.Equal("generic_date_history", policy.ProviderFamily);
        Assert.Equal(ProviderPendingSupportMode.Unknown, policy.PendingSupport);
    }

    private static TrueLayerAccountRecord BuildAccount(string providerId, string providerDisplayName)
    {
        return new TrueLayerAccountRecord(
            AccountId: "acc-test-001",
            DisplayName: "Test Account",
            Currency: "EUR",
            AccountType: "TRANSACTION",
            AccountSubType: null,
            ProviderId: providerId,
            ProviderDisplayName: providerDisplayName,
            ProviderIconUri: null,
            ProviderLogoUri: null,
            ProviderBrandBgColor: null,
            AccountNumberMetadataJson: null,
            RawPayloadJson: "{}");
    }
}
