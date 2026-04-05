using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public enum NarrativeSignalConfidenceTier
{
    LowConfidence = 0,
    MediumConfidence = 1,
    HighConfidence = 2
}

public sealed record NarrativeSignalSet(
    IReadOnlySet<string> MachineReferenceTokens,
    IReadOnlySet<string> AccountLikeTokens,
    IReadOnlySet<string> IbanLikeFragments,
    IReadOnlySet<string> PaymentSystemMarkers,
    IReadOnlySet<string> BeneficiaryNameTokens,
    IReadOnlySet<string> OriginatorNameTokens,
    IReadOnlySet<string> FreeTextReferenceTokens,
    IReadOnlySet<string> ProviderSpecificReferenceTokens,
    IReadOnlySet<string> MerchantLikeTokens,
    IReadOnlyDictionary<string, NarrativeSignalConfidenceTier> SignalConfidenceMap)
{
    public static readonly NarrativeSignalSet Empty = new(
        MachineReferenceTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        AccountLikeTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        IbanLikeFragments: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        PaymentSystemMarkers: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        BeneficiaryNameTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        OriginatorNameTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        FreeTextReferenceTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        ProviderSpecificReferenceTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        MerchantLikeTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        SignalConfidenceMap: new Dictionary<string, NarrativeSignalConfidenceTier>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlySet<string> HighConfidenceTokens =>
        MachineReferenceTokens
            .Concat(AccountLikeTokens)
            .Concat(IbanLikeFragments)
            .Concat(PaymentSystemMarkers)
            .Concat(ProviderSpecificReferenceTokens)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> MediumConfidenceTokens =>
        BeneficiaryNameTokens
            .Concat(OriginatorNameTokens)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> LowConfidenceTokens =>
        FreeTextReferenceTokens
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed class NarrativeSignalExtractor
{
    private static readonly Regex MachineReferenceRegex = new(
        @"\b(?=[a-z0-9]*[a-z])(?=[a-z0-9]*\d)[a-z0-9]{8,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AccountLikeRegex = new(
        @"\b(?:\d{4,10}|x{2,}\d{2,4}|\*{2,}\d{2,4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IbanRegex = new(
        @"\b[a-z]{2}\d{2}[a-z0-9]{6,30}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReferenceRegex = new(
        @"\b(?:ref|reference)\s*[:\-]?\s*([a-z0-9][a-z0-9_\-/]{2,})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BeneficiaryRegex = new(
        @"\b(?:to|beneficiary)\s+([a-z][a-z0-9 '&.\-]{2,40})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OriginatorRegex = new(
        @"\b(?:from|originator|payer)\s+([a-z][a-z0-9 '&.\-]{2,40})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NatWestFpidRegex = new(
        @"\bfpid\s*[:\-]?\s*([a-z0-9\-]{5,})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AibIeReferenceRegex = new(
        @"\bie[a-z0-9]{5,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GenericLongOpaqueRegex = new(
        @"\b[a-z0-9]{10,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> PaymentSystemMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "fp",
        "fpid",
        "faster",
        "fasterpayment",
        "fasterpayments",
        "sepa",
        "ip",
        "bacs",
        "chaps"
    };

    private static readonly HashSet<string> MerchantDescriptorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "subscription",
        "monthly",
        "billing",
        "software",
        "services",
        "pharmacy",
        "retail",
        "store",
        "card",
        "contactless",
        "pos",
        "purchase",
        "terminal",
        "online",
        "renewal"
    };

    private static readonly HashSet<string> CompanyShapeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ltd",
        "limited",
        "llc",
        "inc",
        "plc",
        "corp",
        "company",
        "co"
    };

    public NarrativeSignalSet Extract(
        string rawDescription,
        string normalizedDescription,
        DeterministicProviderCapabilities capabilities)
    {
        if (string.IsNullOrWhiteSpace(rawDescription) && string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return NarrativeSignalSet.Empty;
        }

        var machineReferenceTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accountLikeTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ibanLikeFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paymentSystemMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var beneficiaryNameTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var originatorNameTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var freeTextReferenceTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providerSpecificReferenceTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merchantLikeTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var confidenceMap = new Dictionary<string, NarrativeSignalConfidenceTier>(StringComparer.OrdinalIgnoreCase);

        AddMatches(normalizedDescription, MachineReferenceRegex, machineReferenceTokens, confidenceMap, NarrativeSignalConfidenceTier.HighConfidence);
        AddMatches(normalizedDescription, AccountLikeRegex, accountLikeTokens, confidenceMap, NarrativeSignalConfidenceTier.HighConfidence);
        AddMatches(normalizedDescription, IbanRegex, ibanLikeFragments, confidenceMap, NarrativeSignalConfidenceTier.HighConfidence);

        foreach (Match match in ReferenceRegex.Matches(normalizedDescription))
        {
            AddToken(
                freeTextReferenceTokens,
                confidenceMap,
                match.Groups[1].Value,
                NarrativeSignalConfidenceTier.LowConfidence);
        }

        AddNameTokens(normalizedDescription, BeneficiaryRegex, beneficiaryNameTokens, confidenceMap, NarrativeSignalConfidenceTier.MediumConfidence);
        AddNameTokens(normalizedDescription, OriginatorRegex, originatorNameTokens, confidenceMap, NarrativeSignalConfidenceTier.MediumConfidence);

        if (capabilities.SupportsPaymentSystemMarkers)
        {
            foreach (var marker in PaymentSystemMarkers)
            {
                if (normalizedDescription.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(paymentSystemMarkers, confidenceMap, marker, NarrativeSignalConfidenceTier.HighConfidence);
                }
            }
        }

        AddGenericMerchantSignals(rawDescription, normalizedDescription, merchantLikeTokens, confidenceMap);

        switch (capabilities.NarrativeParserProfile)
        {
            case DeterministicNarrativeParserProfile.Aib:
                AddMatches(
                    normalizedDescription,
                    AibIeReferenceRegex,
                    providerSpecificReferenceTokens,
                    confidenceMap,
                    NarrativeSignalConfidenceTier.HighConfidence);
                break;
            case DeterministicNarrativeParserProfile.NatWestFamily:
                AddMatches(
                    normalizedDescription,
                    NatWestFpidRegex,
                    providerSpecificReferenceTokens,
                    confidenceMap,
                    NarrativeSignalConfidenceTier.HighConfidence,
                    captureGroup: 1);
                if (normalizedDescription.Contains("faster payment", StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(paymentSystemMarkers, confidenceMap, "faster_payment", NarrativeSignalConfidenceTier.HighConfidence);
                }

                break;
            case DeterministicNarrativeParserProfile.BankOfIreland:
                if (normalizedDescription.Contains(" ip ", StringComparison.OrdinalIgnoreCase)
                    || normalizedDescription.StartsWith("ip ", StringComparison.OrdinalIgnoreCase)
                    || normalizedDescription.EndsWith(" ip", StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(paymentSystemMarkers, confidenceMap, "ip", NarrativeSignalConfidenceTier.HighConfidence);
                    AddToken(providerSpecificReferenceTokens, confidenceMap, "ip_marker", NarrativeSignalConfidenceTier.HighConfidence);
                }

                if (normalizedDescription.Contains("sepa", StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(paymentSystemMarkers, confidenceMap, "sepa", NarrativeSignalConfidenceTier.HighConfidence);
                }

                break;
            case DeterministicNarrativeParserProfile.Revolut:
                if (normalizedDescription.Contains("vault", StringComparison.OrdinalIgnoreCase)
                    || normalizedDescription.Contains("pocket", StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(providerSpecificReferenceTokens, confidenceMap, "savings_label", NarrativeSignalConfidenceTier.MediumConfidence);
                }

                break;
            case DeterministicNarrativeParserProfile.Wise:
                if (normalizedDescription.Contains("wise", StringComparison.OrdinalIgnoreCase)
                    || normalizedDescription.Contains("partner", StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(providerSpecificReferenceTokens, confidenceMap, "wise_partner_hint", NarrativeSignalConfidenceTier.MediumConfidence);
                }

                break;
            case DeterministicNarrativeParserProfile.Monzo:
            case DeterministicNarrativeParserProfile.Starling:
                if (normalizedDescription.Contains("pot", StringComparison.OrdinalIgnoreCase)
                    || normalizedDescription.Contains("saving space", StringComparison.OrdinalIgnoreCase))
                {
                    AddToken(providerSpecificReferenceTokens, confidenceMap, "user_savings_label", NarrativeSignalConfidenceTier.MediumConfidence);
                }

                break;
        }

        if (capabilities.SupportsMachineReferenceTokens)
        {
            AddMatches(
                normalizedDescription,
                GenericLongOpaqueRegex,
                machineReferenceTokens,
                confidenceMap,
                NarrativeSignalConfidenceTier.MediumConfidence);
        }

        return new NarrativeSignalSet(
            machineReferenceTokens,
            accountLikeTokens,
            ibanLikeFragments,
            paymentSystemMarkers,
            beneficiaryNameTokens,
            originatorNameTokens,
            freeTextReferenceTokens,
            providerSpecificReferenceTokens,
            merchantLikeTokens,
            confidenceMap);
    }

    private static void AddGenericMerchantSignals(
        string rawDescription,
        string normalizedDescription,
        ISet<string> merchantLikeTokens,
        IDictionary<string, NarrativeSignalConfidenceTier> confidenceMap)
    {
        if (rawDescription.Contains('*', StringComparison.Ordinal)
            || rawDescription.Contains('/', StringComparison.Ordinal)
            || rawDescription.Contains("  ", StringComparison.Ordinal))
        {
            AddToken(merchantLikeTokens, confidenceMap, "processor_separator", NarrativeSignalConfidenceTier.MediumConfidence);
        }

        foreach (var keyword in MerchantDescriptorKeywords)
        {
            if (normalizedDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                AddToken(merchantLikeTokens, confidenceMap, keyword, NarrativeSignalConfidenceTier.MediumConfidence);
            }
        }

        foreach (var companyToken in CompanyShapeTokens)
        {
            if (normalizedDescription.Contains($" {companyToken} ", StringComparison.OrdinalIgnoreCase)
                || normalizedDescription.EndsWith($" {companyToken}", StringComparison.OrdinalIgnoreCase))
            {
                AddToken(merchantLikeTokens, confidenceMap, companyToken, NarrativeSignalConfidenceTier.MediumConfidence);
            }
        }

        var words = rawDescription
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var uppercaseDenseWordCount = words.Count(word =>
            word.Length >= 5
            && word.All(char.IsLetter)
            && word.ToUpperInvariant() == word);
        if (uppercaseDenseWordCount >= 2)
        {
            AddToken(merchantLikeTokens, confidenceMap, "uppercase_company_shape", NarrativeSignalConfidenceTier.MediumConfidence);
        }
    }

    private static void AddNameTokens(
        string input,
        Regex regex,
        ISet<string> sink,
        IDictionary<string, NarrativeSignalConfidenceTier> confidenceMap,
        NarrativeSignalConfidenceTier confidence)
    {
        foreach (Match match in regex.Matches(input))
        {
            if (!match.Success || match.Groups.Count < 2)
            {
                continue;
            }

            var tokens = match.Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length >= 3)
                .Take(5);
            foreach (var token in tokens)
            {
                AddToken(sink, confidenceMap, token, confidence);
            }
        }
    }

    private static void AddMatches(
        string input,
        Regex regex,
        ISet<string> sink,
        IDictionary<string, NarrativeSignalConfidenceTier> confidenceMap,
        NarrativeSignalConfidenceTier confidence,
        int captureGroup = 0)
    {
        foreach (Match match in regex.Matches(input))
        {
            if (!match.Success)
            {
                continue;
            }

            var value = captureGroup > 0 && match.Groups.Count > captureGroup
                ? match.Groups[captureGroup].Value
                : match.Value;
            AddToken(sink, confidenceMap, value, confidence);
        }
    }

    private static void AddToken(
        ISet<string> sink,
        IDictionary<string, NarrativeSignalConfidenceTier> confidenceMap,
        string token,
        NarrativeSignalConfidenceTier confidence)
    {
        var normalized = token.Trim().ToLowerInvariant();
        if (normalized.Length < 2)
        {
            return;
        }

        sink.Add(normalized);
        if (!confidenceMap.TryGetValue(normalized, out var existing) || existing < confidence)
        {
            confidenceMap[normalized] = confidence;
        }
    }
}
