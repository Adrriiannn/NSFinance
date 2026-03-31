using NSFinance.Api.Modules.Banking.Services.Models;

namespace NSFinance.Api.Modules.Banking.Services;

public enum ProviderTransactionVisibilityMode
{
    DateHistory,
    CappedVisibleSlice
}

public sealed record ProviderTransactionSyncPolicy(
    string ProviderKey,
    ProviderTransactionVisibilityMode VisibilityMode,
    int? SettledResponseCap,
    int InitialBackfillHistoryDays,
    string InitialBackfillPolicyName,
    int IncrementalLookbackDays,
    int IncrementalFallbackDays,
    int IncrementalChunkDays,
    int MaxAdaptiveSplitDepth,
    TimeSpan MinAdaptiveWindow,
    bool ReScanVisibleSliceEachSync);

public static class ProviderSyncPolicyCatalog
{
    private static readonly ProviderTransactionSyncPolicy DefaultPolicy = new(
        ProviderKey: "default",
        VisibilityMode: ProviderTransactionVisibilityMode.DateHistory,
        SettledResponseCap: null,
        InitialBackfillHistoryDays: 365 * 6,
        InitialBackfillPolicyName: "default_initial_6y",
        IncrementalLookbackDays: 35,
        IncrementalFallbackDays: 120,
        IncrementalChunkDays: 7,
        MaxAdaptiveSplitDepth: 0,
        MinAdaptiveWindow: TimeSpan.Zero,
        ReScanVisibleSliceEachSync: false);

    public static ProviderTransactionSyncPolicy ResolveForAccount(TrueLayerAccountRecord account)
    {
        var providerId = (account.ProviderId ?? string.Empty).Trim().ToLowerInvariant();
        var providerDisplayName = (account.ProviderDisplayName ?? string.Empty).Trim().ToLowerInvariant();
        var providerComposite = $"{providerId} {providerDisplayName}";

        if (providerComposite.Contains("allied irish bank", StringComparison.Ordinal)
            || providerComposite.Contains(" aib", StringComparison.Ordinal)
            || providerId.StartsWith("aib", StringComparison.Ordinal))
        {
            return new ProviderTransactionSyncPolicy(
                ProviderKey: "aib",
                VisibilityMode: ProviderTransactionVisibilityMode.CappedVisibleSlice,
                SettledResponseCap: 100,
                InitialBackfillHistoryDays: 366,
                InitialBackfillPolicyName: "aib_initial_1y_capped_slice",
                IncrementalLookbackDays: 35,
                IncrementalFallbackDays: 35,
                IncrementalChunkDays: 7,
                MaxAdaptiveSplitDepth: 6,
                MinAdaptiveWindow: TimeSpan.FromHours(6),
                ReScanVisibleSliceEachSync: true);
        }

        if (providerComposite.Contains("revolut", StringComparison.Ordinal))
        {
            return DefaultPolicy with
            {
                ProviderKey = "revolut",
                InitialBackfillHistoryDays = 365 * 6,
                InitialBackfillPolicyName = "revolut_initial_6y"
            };
        }

        if (providerComposite.Contains("ulster", StringComparison.Ordinal))
        {
            return DefaultPolicy with
            {
                ProviderKey = "ulster",
                InitialBackfillHistoryDays = 365 * 6,
                InitialBackfillPolicyName = "ulster_initial_6y"
            };
        }

        if (providerComposite.Contains("bank of ireland", StringComparison.Ordinal))
        {
            return DefaultPolicy with
            {
                ProviderKey = "boi",
                InitialBackfillHistoryDays = 366,
                InitialBackfillPolicyName = "boi_initial_1y"
            };
        }

        if (providerComposite.Contains("permanent tsb", StringComparison.Ordinal)
            || providerComposite.Contains("ptsb", StringComparison.Ordinal))
        {
            return DefaultPolicy with
            {
                ProviderKey = "ptsb",
                InitialBackfillHistoryDays = 95,
                InitialBackfillPolicyName = "ptsb_initial_90d"
            };
        }

        return DefaultPolicy;
    }
}
