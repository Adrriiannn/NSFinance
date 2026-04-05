namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public enum DeterministicProviderTimestampPrecision
{
    Unknown = 0,
    PreciseDateTime = 1,
    CoarseDateTime = 2,
    DateOnly = 3
}

public enum DeterministicNarrativeStructureRichness
{
    Generic = 0,
    SemiStructured = 1,
    RichStructured = 2
}

public enum DeterministicMerchantDescriptorReliability
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum DeterministicNarrativeParserProfile
{
    Generic = 0,
    Aib = 1,
    NatWestFamily = 2,
    BankOfIreland = 3,
    Revolut = 4,
    Wise = 5,
    Monzo = 6,
    Starling = 7
}

public sealed record DeterministicProviderCapabilities(
    string ProviderKey,
    DeterministicProviderTimestampPrecision TimestampPrecision,
    DeterministicNarrativeStructureRichness NarrativeStructureRichness,
    bool SupportsMachineReferenceTokens,
    bool SupportsPaymentSystemMarkers,
    bool SupportsReliableCounterpartyReferenceFragments,
    DeterministicMerchantDescriptorReliability MerchantDescriptorReliability,
    bool SupportsProviderSpecificTransferMarkers,
    DeterministicNarrativeParserProfile NarrativeParserProfile,
    bool IsProviderSpecificRule);

public sealed class ProviderCapabilityRegistry
{
    private sealed record ProviderCapabilityRule(
        DeterministicProviderCapabilities Capabilities,
        IReadOnlyList<string> ProviderIdHints,
        IReadOnlyList<string> ProviderDisplayHints);

    private static readonly DeterministicProviderCapabilities DefaultCapabilities = new(
        ProviderKey: "generic",
        TimestampPrecision: DeterministicProviderTimestampPrecision.Unknown,
        NarrativeStructureRichness: DeterministicNarrativeStructureRichness.Generic,
        SupportsMachineReferenceTokens: false,
        SupportsPaymentSystemMarkers: false,
        SupportsReliableCounterpartyReferenceFragments: false,
        MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
        SupportsProviderSpecificTransferMarkers: false,
        NarrativeParserProfile: DeterministicNarrativeParserProfile.Generic,
        IsProviderSpecificRule: false);

    private static readonly IReadOnlyList<ProviderCapabilityRule> Rules =
    [
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "aib",
                TimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.RichStructured,
                SupportsMachineReferenceTokens: true,
                SupportsPaymentSystemMarkers: false,
                SupportsReliableCounterpartyReferenceFragments: true,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: true,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.Aib,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob aib", "ob aib business", "aib ie ob"],
            ProviderDisplayHints: ["aib", "allied irish bank"]),
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "natwest_family",
                TimestampPrecision: DeterministicProviderTimestampPrecision.CoarseDateTime,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.RichStructured,
                SupportsMachineReferenceTokens: true,
                SupportsPaymentSystemMarkers: true,
                SupportsReliableCounterpartyReferenceFragments: true,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: true,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.NatWestFamily,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob natwest", "ob rbs", "ob ulster"],
            ProviderDisplayHints: ["natwest", "royal bank of scotland", "rbs", "ulster bank"]),
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "boi",
                TimestampPrecision: DeterministicProviderTimestampPrecision.CoarseDateTime,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.SemiStructured,
                SupportsMachineReferenceTokens: false,
                SupportsPaymentSystemMarkers: true,
                SupportsReliableCounterpartyReferenceFragments: false,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: true,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.BankOfIreland,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob boi"],
            ProviderDisplayHints: ["bank of ireland", "boi"]),
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "revolut",
                TimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.SemiStructured,
                SupportsMachineReferenceTokens: false,
                SupportsPaymentSystemMarkers: false,
                SupportsReliableCounterpartyReferenceFragments: false,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: false,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.Revolut,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob revolut", "revolut ie ob"],
            ProviderDisplayHints: ["revolut"]),
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "wise",
                TimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.SemiStructured,
                SupportsMachineReferenceTokens: false,
                SupportsPaymentSystemMarkers: false,
                SupportsReliableCounterpartyReferenceFragments: false,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: false,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.Wise,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob transferwise", "ob wise"],
            ProviderDisplayHints: ["wise", "transferwise"]),
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "monzo",
                TimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.SemiStructured,
                SupportsMachineReferenceTokens: false,
                SupportsPaymentSystemMarkers: false,
                SupportsReliableCounterpartyReferenceFragments: false,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: false,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.Monzo,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob monzo"],
            ProviderDisplayHints: ["monzo"]),
        new(
            new DeterministicProviderCapabilities(
                ProviderKey: "starling",
                TimestampPrecision: DeterministicProviderTimestampPrecision.Unknown,
                NarrativeStructureRichness: DeterministicNarrativeStructureRichness.SemiStructured,
                SupportsMachineReferenceTokens: false,
                SupportsPaymentSystemMarkers: false,
                SupportsReliableCounterpartyReferenceFragments: false,
                MerchantDescriptorReliability: DeterministicMerchantDescriptorReliability.Medium,
                SupportsProviderSpecificTransferMarkers: false,
                NarrativeParserProfile: DeterministicNarrativeParserProfile.Starling,
                IsProviderSpecificRule: true),
            ProviderIdHints: ["ob starling"],
            ProviderDisplayHints: ["starling"]),
        new(
            DefaultCapabilities with
            {
                ProviderKey = "generic_known_provider"
            },
            ProviderIdHints:
            [
                "ob american express",
                "ob barclaycard",
                "ob barclays",
                "ob capital one",
                "ob chelsea building society",
                "ob danske",
                "ob first direct",
                "ob halifax",
                "ob hsbc",
                "ob lloyds",
                "ob mbna",
                "ob mettle",
                "ob ms",
                "ob nationwide",
                "ob ptsb",
                "ob santander",
                "ob tesco",
                "ob tide",
                "ob tsb",
                "ob virgin money",
                "ob yorkshire building society",
                "ob cashplus"
            ],
            ProviderDisplayHints:
            [
                "american express",
                "barclaycard",
                "barclays",
                "capital one",
                "chelsea building society",
                "danske",
                "first direct",
                "halifax",
                "hsbc",
                "lloyds",
                "mbna",
                "mettle",
                "m&s bank",
                "marks & spencer bank",
                "nationwide",
                "ptsb",
                "permanent tsb",
                "santander",
                "tesco bank",
                "tide",
                "tsb",
                "virgin money",
                "yorkshire building society",
                "zempler bank",
                "cashplus"
            ])
    ];

    public DeterministicProviderCapabilities Resolve(string? providerId, string? providerDisplayName)
    {
        var normalizedProviderId = Normalize(providerId);
        var normalizedProviderDisplayName = Normalize(providerDisplayName);

        foreach (var rule in Rules)
        {
            if (Matches(rule, normalizedProviderId, normalizedProviderDisplayName))
            {
                return rule.Capabilities;
            }
        }

        return DefaultCapabilities;
    }

    private static bool Matches(
        ProviderCapabilityRule rule,
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
