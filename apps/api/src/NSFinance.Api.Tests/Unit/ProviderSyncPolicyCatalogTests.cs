using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Models;

namespace NSFinance.Api.Tests.Unit;

public class ProviderSyncPolicyCatalogTests
{
    [Fact]
    public void ResolveForAccount_Aib_UsesCappedVisibleSlicePolicy()
    {
        var account = new TrueLayerAccountRecord(
            AccountId: "acc-aib-001",
            DisplayName: "AIB Current",
            Currency: "EUR",
            AccountType: "TRANSACTION",
            AccountSubType: null,
            ProviderId: "aib-ie-ob",
            ProviderDisplayName: "Allied Irish Bank",
            ProviderIconUri: null,
            ProviderLogoUri: null,
            ProviderBrandBgColor: null,
            AccountNumberMetadataJson: null,
            RawPayloadJson: "{}");

        var policy = ProviderSyncPolicyCatalog.ResolveForAccount(account);

        Assert.Equal("aib", policy.ProviderKey);
        Assert.Equal(ProviderTransactionVisibilityMode.CappedVisibleSlice, policy.VisibilityMode);
        Assert.Equal(100, policy.SettledResponseCap);
        Assert.True(policy.ReScanVisibleSliceEachSync);
    }

    [Fact]
    public void ResolveForAccount_Revolut_UsesDateHistoryPolicy()
    {
        var account = new TrueLayerAccountRecord(
            AccountId: "acc-revolut-001",
            DisplayName: "Revolut Main",
            Currency: "EUR",
            AccountType: "TRANSACTION",
            AccountSubType: null,
            ProviderId: "revolut-ie-ob",
            ProviderDisplayName: "Revolut",
            ProviderIconUri: null,
            ProviderLogoUri: null,
            ProviderBrandBgColor: null,
            AccountNumberMetadataJson: null,
            RawPayloadJson: "{}");

        var policy = ProviderSyncPolicyCatalog.ResolveForAccount(account);

        Assert.Equal("revolut", policy.ProviderKey);
        Assert.Equal(ProviderTransactionVisibilityMode.DateHistory, policy.VisibilityMode);
        Assert.Null(policy.SettledResponseCap);
        Assert.False(policy.ReScanVisibleSliceEachSync);
    }
}
