using NSFinance.Api.Modules.Banking.Services.Models;

namespace NSFinance.Api.Modules.Banking.Services;

public enum ProviderTransactionVisibilityMode
{
    DateHistory,
    CappedVisibleSlice
}

public enum ProviderPendingSupportMode
{
    Unknown,
    Supported,
    Unsupported,
    Partial
}

public enum ProviderTimestampPrecisionMode
{
    FullTimestamp,
    DateOnlyOrMixed
}

public sealed record ProviderTransactionSyncPolicy(
    string ProviderKey,
    string ProviderFamily,
    ProviderTransactionVisibilityMode VisibilityMode,
    int? SettledResponseCap,
    int InitialBackfillHistoryDays,
    int CardInitialBackfillHistoryDays,
    string InitialBackfillPolicyName,
    int IncrementalLookbackDays,
    int IncrementalFallbackDays,
    int IncrementalChunkDays,
    int MaxAdaptiveSplitDepth,
    TimeSpan MinAdaptiveWindow,
    bool ReScanVisibleSliceEachSync,
    ProviderPendingSupportMode PendingSupport,
    ProviderTimestampPrecisionMode TimestampPrecision,
    int? InitialLongHistoryGraceMinutes,
    string HistoryNotes);

public static class ProviderSyncPolicyCatalog
{
    private sealed record ProviderPolicyRule(
        ProviderTransactionSyncPolicy Policy,
        IReadOnlyList<string> ProviderIdHints,
        IReadOnlyList<string> ProviderDisplayHints);

    private static readonly ProviderTransactionSyncPolicy DefaultPolicy = new(
        ProviderKey: "default",
        ProviderFamily: "generic_date_history",
        VisibilityMode: ProviderTransactionVisibilityMode.DateHistory,
        SettledResponseCap: null,
        InitialBackfillHistoryDays: 365 * 6,
        CardInitialBackfillHistoryDays: 365 * 6,
        InitialBackfillPolicyName: "default_initial_6y",
        IncrementalLookbackDays: 35,
        IncrementalFallbackDays: 120,
        IncrementalChunkDays: 7,
        MaxAdaptiveSplitDepth: 0,
        MinAdaptiveWindow: TimeSpan.Zero,
        ReScanVisibleSliceEachSync: false,
        PendingSupport: ProviderPendingSupportMode.Unknown,
        TimestampPrecision: ProviderTimestampPrecisionMode.DateOnlyOrMixed,
        InitialLongHistoryGraceMinutes: null,
        HistoryNotes: "Generic TrueLayer date-history provider profile.");

    private static readonly ProviderTransactionSyncPolicy AibPolicy = new(
        ProviderKey: "aib",
        ProviderFamily: "irish_capped_slice",
        VisibilityMode: ProviderTransactionVisibilityMode.CappedVisibleSlice,
        SettledResponseCap: 100,
        InitialBackfillHistoryDays: 365 * 2,
        CardInitialBackfillHistoryDays: 365 * 2,
        InitialBackfillPolicyName: "aib_initial_2y_capped_slice",
        IncrementalLookbackDays: 35,
        IncrementalFallbackDays: 35,
        IncrementalChunkDays: 7,
        MaxAdaptiveSplitDepth: 6,
        MinAdaptiveWindow: TimeSpan.FromHours(6),
        ReScanVisibleSliceEachSync: true,
        PendingSupport: ProviderPendingSupportMode.Unknown,
        TimestampPrecision: ProviderTimestampPrecisionMode.DateOnlyOrMixed,
        InitialLongHistoryGraceMinutes: null,
        HistoryNotes: "Count-limited visible-slice provider (up to ~100 returned rows).");

    private static readonly ProviderTransactionSyncPolicy BoiPolicy = DefaultPolicy with
    {
        ProviderKey = "boi",
        ProviderFamily = "irish_retail_standard",
        InitialBackfillHistoryDays = 366,
        CardInitialBackfillHistoryDays = 366,
        InitialBackfillPolicyName = "boi_initial_1y",
        HistoryNotes = "Bank of Ireland profile; practical initial history target is around one year."
    };

    private static readonly ProviderTransactionSyncPolicy PtsbPolicy = DefaultPolicy with
    {
        ProviderKey = "ptsb",
        ProviderFamily = "irish_mixed_history",
        InitialBackfillHistoryDays = 366,
        CardInitialBackfillHistoryDays = 180,
        InitialBackfillPolicyName = "ptsb_initial_mixed_visa_nonvisa",
        HistoryNotes = "PTSB may expose mixed windows by rail/card type; keep broader account backfill with conservative card window."
    };

    private static readonly ProviderTransactionSyncPolicy RevolutPolicy = DefaultPolicy with
    {
        ProviderKey = "revolut",
        ProviderFamily = "fintech_revolut",
        InitialBackfillPolicyName = "revolut_initial_6y",
        PendingSupport = ProviderPendingSupportMode.Supported,
        TimestampPrecision = ProviderTimestampPrecisionMode.FullTimestamp,
        InitialLongHistoryGraceMinutes = 5,
        HistoryNotes = "Revolut can expose deep history on early consent window; later calls may narrow by SCA window."
    };

    private static readonly ProviderTransactionSyncPolicy MonzoPolicy = DefaultPolicy with
    {
        ProviderKey = "monzo",
        ProviderFamily = "fintech_monzo",
        InitialBackfillPolicyName = "monzo_initial_6y",
        PendingSupport = ProviderPendingSupportMode.Supported,
        TimestampPrecision = ProviderTimestampPrecisionMode.FullTimestamp,
        InitialLongHistoryGraceMinutes = 5,
        HistoryNotes = "Monzo supports deep initial history with later consent-window constraints."
    };

    private static readonly ProviderTransactionSyncPolicy StarlingPolicy = DefaultPolicy with
    {
        ProviderKey = "starling",
        ProviderFamily = "fintech_starling",
        InitialBackfillPolicyName = "starling_initial_6y",
        PendingSupport = ProviderPendingSupportMode.Partial,
        TimestampPrecision = ProviderTimestampPrecisionMode.FullTimestamp,
        HistoryNotes = "Starling pending behavior is provider-specific; treat as partial capability."
    };

    private static readonly ProviderTransactionSyncPolicy SantanderPolicy = DefaultPolicy with
    {
        ProviderKey = "santander",
        ProviderFamily = "uk_retail_santander",
        InitialBackfillHistoryDays = 365 * 2,
        CardInitialBackfillHistoryDays = 365 * 2,
        InitialBackfillPolicyName = "santander_initial_2y",
        PendingSupport = ProviderPendingSupportMode.Unsupported,
        HistoryNotes = "Santander profile: treat pending as unsupported unless provider behavior changes."
    };

    private static readonly ProviderTransactionSyncPolicy NatWestFamilyPolicy = DefaultPolicy with
    {
        ProviderKey = "natwest_family",
        ProviderFamily = "uk_natwest_rbs_ulster_family",
        InitialBackfillPolicyName = "natwest_family_initial_6y",
        CardInitialBackfillHistoryDays = 180,
        HistoryNotes = "NatWest/RBS/Ulster family; initial long history often available with post-window constraints."
    };

    private static readonly ProviderTransactionSyncPolicy LloydsFamilyPolicy = DefaultPolicy with
    {
        ProviderKey = "lloyds_family",
        ProviderFamily = "uk_lloyds_halifax_bos_mbna_family",
        InitialBackfillPolicyName = "lloyds_family_initial_6y",
        CardInitialBackfillHistoryDays = 180,
        InitialLongHistoryGraceMinutes = 45,
        HistoryNotes = "Lloyds family often offers long account history with tighter card-history windows."
    };

    private static readonly ProviderTransactionSyncPolicy HsbcFamilyPolicy = DefaultPolicy with
    {
        ProviderKey = "hsbc_family",
        ProviderFamily = "uk_hsbc_firstdirect_ms_family",
        InitialBackfillPolicyName = "hsbc_family_initial_6y",
        CardInitialBackfillHistoryDays = 180,
        InitialLongHistoryGraceMinutes = 60,
        HistoryNotes = "HSBC/First Direct/M&S profile; long history is time-window sensitive after consent."
    };

    private static readonly ProviderTransactionSyncPolicy BarclaysFamilyPolicy = DefaultPolicy with
    {
        ProviderKey = "barclays_family",
        ProviderFamily = "uk_barclays_barclaycard_family",
        InitialBackfillHistoryDays = 365 * 2,
        CardInitialBackfillHistoryDays = 365 * 2,
        InitialBackfillPolicyName = "barclays_family_initial_2y",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        HistoryNotes = "Barclays/Barclaycard profile with card-heavy datasets."
    };

    private static readonly ProviderTransactionSyncPolicy CardFirstPolicy = DefaultPolicy with
    {
        ProviderKey = "card_first",
        ProviderFamily = "uk_card_first_mix",
        InitialBackfillHistoryDays = 365 * 2,
        CardInitialBackfillHistoryDays = 365 * 2,
        InitialBackfillPolicyName = "card_first_initial_2y",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        HistoryNotes = "Card-first providers (Amex/Capital One/Tesco/Virgin) may have narrower account-like surfaces."
    };

    private static readonly ProviderTransactionSyncPolicy WisePolicy = DefaultPolicy with
    {
        ProviderKey = "wise",
        ProviderFamily = "fintech_wise",
        InitialBackfillPolicyName = "wise_initial_full_history",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        TimestampPrecision = ProviderTimestampPrecisionMode.FullTimestamp,
        HistoryNotes = "Wise generally exposes broad history from online banking records."
    };

    private static readonly ProviderTransactionSyncPolicy TidePolicy = DefaultPolicy with
    {
        ProviderKey = "tide",
        ProviderFamily = "fintech_tide_business",
        InitialBackfillPolicyName = "tide_initial_all_then_90d",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        TimestampPrecision = ProviderTimestampPrecisionMode.FullTimestamp,
        HistoryNotes = "Tide profile: deep initial visibility, then shorter rolling windows may apply."
    };

    private static readonly ProviderTransactionSyncPolicy MettleZemplerPolicy = DefaultPolicy with
    {
        ProviderKey = "mettle_zempler",
        ProviderFamily = "fintech_business_banking",
        InitialBackfillPolicyName = "mettle_zempler_initial_6y",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        TimestampPrecision = ProviderTimestampPrecisionMode.FullTimestamp,
        HistoryNotes = "Business fintech profile for Mettle and Zempler/Cashplus."
    };

    private static readonly ProviderTransactionSyncPolicy BuildingSocietyPolicy = DefaultPolicy with
    {
        ProviderKey = "building_society",
        ProviderFamily = "uk_building_society",
        InitialBackfillPolicyName = "building_society_initial_6y",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        HistoryNotes = "Building society profile (Chelsea/Yorkshire/TSB)."
    };

    private static readonly ProviderTransactionSyncPolicy DanskePolicy = DefaultPolicy with
    {
        ProviderKey = "danske",
        ProviderFamily = "uk_danske",
        InitialBackfillHistoryDays = 760,
        CardInitialBackfillHistoryDays = 760,
        InitialBackfillPolicyName = "danske_initial_25m",
        PendingSupport = ProviderPendingSupportMode.Unknown,
        HistoryNotes = "Danske profile with roughly 25-month initial history windows."
    };

    private static readonly ProviderTransactionSyncPolicy NationwidePolicy = DefaultPolicy with
    {
        ProviderKey = "nationwide",
        ProviderFamily = "uk_nationwide",
        InitialBackfillHistoryDays = 450,
        CardInitialBackfillHistoryDays = 90,
        InitialBackfillPolicyName = "nationwide_initial_15m",
        InitialLongHistoryGraceMinutes = 15,
        PendingSupport = ProviderPendingSupportMode.Unknown,
        HistoryNotes = "Nationwide profile: longer current-account history with shorter card window."
    };

    private static readonly IReadOnlyList<ProviderPolicyRule> Rules =
    [
        new ProviderPolicyRule(
            AibPolicy,
            ProviderIdHints: ["ob-aib"],
            ProviderDisplayHints: ["allied irish bank", "aib"]),
        new ProviderPolicyRule(
            BoiPolicy,
            ProviderIdHints: ["ob-boi"],
            ProviderDisplayHints: ["bank of ireland"]),
        new ProviderPolicyRule(
            PtsbPolicy,
            ProviderIdHints: ["ob-ptsb"],
            ProviderDisplayHints: ["permanent tsb", "ptsb"]),
        new ProviderPolicyRule(
            RevolutPolicy,
            ProviderIdHints: ["ob-revolut"],
            ProviderDisplayHints: ["revolut"]),
        new ProviderPolicyRule(
            MonzoPolicy,
            ProviderIdHints: ["ob-monzo"],
            ProviderDisplayHints: ["monzo"]),
        new ProviderPolicyRule(
            StarlingPolicy,
            ProviderIdHints: ["ob-starling"],
            ProviderDisplayHints: ["starling"]),
        new ProviderPolicyRule(
            SantanderPolicy,
            ProviderIdHints: ["ob-santander"],
            ProviderDisplayHints: ["santander"]),
        new ProviderPolicyRule(
            NatWestFamilyPolicy,
            ProviderIdHints: ["ob-natwest", "ob-rbs", "ob-ulster"],
            ProviderDisplayHints: ["natwest", "royal bank of scotland", "rbs", "ulster bank"]),
        new ProviderPolicyRule(
            LloydsFamilyPolicy,
            ProviderIdHints: ["ob-lloyds", "ob-halifax", "ob-bos", "ob-mbna"],
            ProviderDisplayHints: ["lloyds", "halifax", "bank of scotland", "mbna"]),
        new ProviderPolicyRule(
            HsbcFamilyPolicy,
            ProviderIdHints: ["ob-hsbc", "ob-first-direct", "ob-ms"],
            ProviderDisplayHints: ["hsbc", "first direct", "m&s bank", "marks & spencer bank"]),
        new ProviderPolicyRule(
            BarclaysFamilyPolicy,
            ProviderIdHints: ["ob-barclays", "ob-barclaycard"],
            ProviderDisplayHints: ["barclays", "barclaycard"]),
        new ProviderPolicyRule(
            CardFirstPolicy,
            ProviderIdHints: ["ob-amex", "ob-capital-one", "ob-tesco", "ob-virgin-money"],
            ProviderDisplayHints: ["american express", "capital one", "tesco bank", "virgin money"]),
        new ProviderPolicyRule(
            WisePolicy,
            ProviderIdHints: ["ob-transferwise"],
            ProviderDisplayHints: ["wise", "transferwise"]),
        new ProviderPolicyRule(
            TidePolicy,
            ProviderIdHints: ["ob-tide"],
            ProviderDisplayHints: ["tide"]),
        new ProviderPolicyRule(
            MettleZemplerPolicy,
            ProviderIdHints: ["ob-mettle", "ob-cashplus"],
            ProviderDisplayHints: ["mettle", "zempler", "cashplus"]),
        new ProviderPolicyRule(
            BuildingSocietyPolicy,
            ProviderIdHints: ["ob-chelsea-building-society", "ob-yorkshire-building-society", "ob-tsb"],
            ProviderDisplayHints: ["chelsea building society", "yorkshire building society", "tsb"]),
        new ProviderPolicyRule(
            DanskePolicy,
            ProviderIdHints: ["ob-danske", "xs2a-danske"],
            ProviderDisplayHints: ["danske"]),
        new ProviderPolicyRule(
            NationwidePolicy,
            ProviderIdHints: ["ob-nationwide"],
            ProviderDisplayHints: ["nationwide"])
    ];

    public static ProviderTransactionSyncPolicy ResolveForAccount(TrueLayerAccountRecord account)
    {
        return ResolveForConnection(account.ProviderId, account.ProviderDisplayName);
    }

    public static ProviderTransactionSyncPolicy ResolveForConnection(string? providerId, string? providerDisplayName)
    {
        var normalizedProviderId = Normalize(providerId);
        var normalizedProviderDisplayName = Normalize(providerDisplayName);

        foreach (var rule in Rules)
        {
            if (Matches(rule, normalizedProviderId, normalizedProviderDisplayName))
            {
                return rule.Policy;
            }
        }

        return DefaultPolicy;
    }

    private static bool Matches(
        ProviderPolicyRule rule,
        string normalizedProviderId,
        string normalizedProviderDisplayName)
    {
        return ContainsAnyHint(normalizedProviderId, rule.ProviderIdHints)
            || ContainsAnyHint(normalizedProviderDisplayName, rule.ProviderDisplayHints);
    }

    private static bool ContainsAnyHint(string candidate, IEnumerable<string> hints)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var hint in hints)
        {
            var normalizedHint = Normalize(hint);
            if (string.IsNullOrWhiteSpace(normalizedHint))
            {
                continue;
            }

            if (candidate.Contains(normalizedHint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.Replace("_", " ", StringComparison.Ordinal);
        normalized = normalized.Replace("-", " ", StringComparison.Ordinal);
        return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
