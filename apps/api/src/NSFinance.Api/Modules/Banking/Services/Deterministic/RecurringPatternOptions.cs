namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed record RecurringPatternTextDescriptor(
    string NormalizedDescription,
    IReadOnlySet<string> Tokens,
    IReadOnlySet<string> MerchantTokens,
    IReadOnlySet<string> SignatureTokens,
    string MerchantFamilyKey,
    string BillingSignatureKey,
    bool IsMixedUseMerchantFamily);

public sealed class RecurringPatternOptions
{
    public int MinimumPriorMatches { get; init; } = 2;
    public TimeSpan LookbackWindow { get; init; } = TimeSpan.FromDays(800);
    public double MerchantFuzzyMatchThreshold { get; init; } = 0.62d;
    public double DescriptionSimilarityThreshold { get; init; } = 0.68d;
    public double AmountTolerancePercent { get; init; } = 0.08d;
    public double NearStableAmountTolerancePercent { get; init; } = 0.12d;
    public double ShiftedAmountRatioThreshold { get; init; } = 1.10d;
    public double MajorAmountShiftRatioThreshold { get; init; } = 1.75d;
    public bool RequireSameCurrency { get; init; } = true;
    public int MaxCandidatePoolSize { get; init; } = 64;
    public int MaxFallbackScanRows { get; init; } = 1500;
    public double ReversalAmountTolerancePercent { get; init; } = 0.08d;
    public int ReversalWindowDays { get; init; } = 14;
    public IReadOnlyDictionary<Guid, RecurringPatternTextDescriptor>? PrecomputedTextByTransactionId { get; init; }
    public IReadOnlySet<string> DiscretionaryMerchantTokens { get; init; } = DefaultDiscretionaryMerchantTokens;
    public IReadOnlySet<string> MerchantStopWords { get; init; } = DefaultMerchantStopWords;
    public IReadOnlySet<string> RecurringSignatureStopWords { get; init; } = DefaultRecurringSignatureStopWords;
    public IReadOnlySet<string> MixedUseMerchantFamilies { get; init; } = DefaultMixedUseMerchantFamilies;
    public IReadOnlySet<string> ReversalLikeTokens { get; init; } = DefaultReversalLikeTokens;

    public static IReadOnlySet<string> DefaultDiscretionaryMerchantTokens { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "aldi", "lidl", "tesco", "supermarket", "grocery", "groceries", "restaurant", "cafe", "coffee", "bar",
        "uber", "lyft", "steam", "amazon", "shopping", "market", "petrol", "fuel", "cinema", "ticket", "store"
    };

    public static IReadOnlySet<string> DefaultMerchantStopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "to", "from", "transfer", "payment", "paid", "bank", "account", "debit", "credit", "card", "sepa",
        "fp", "faster", "standing", "order", "direct", "dd", "internal", "incoming", "outgoing", "revolut", "aib"
    };

    public static IReadOnlySet<string> DefaultRecurringSignatureStopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "to", "from", "payment", "bank", "card", "transfer", "purchase", "debit", "credit", "internal",
        "outgoing", "incoming", "sepa", "fp", "faster", "standing", "order", "direct", "dd",
        "eu", "uk", "ie", "ltd", "plc"
    };

    public static IReadOnlySet<string> DefaultMixedUseMerchantFamilies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "amazon", "apple", "google", "microsoft", "meta", "paypal"
    };

    public static IReadOnlySet<string> DefaultReversalLikeTokens { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "refund", "reversal", "chargeback", "returned", "return", "reimburse", "reimbursed", "reversal_ref"
    };

    public RecurringPatternTextDescriptor BuildDescriptor(
        TransactionNormalizationService normalizationService,
        string? description)
    {
        var normalizedDescription = normalizationService.NormalizeDescription(description);
        var tokens = normalizationService.Tokenize(normalizedDescription);
        return BuildDescriptorFromNormalized(normalizedDescription, tokens);
    }

    public RecurringPatternTextDescriptor BuildDescriptorFromNormalized(
        string normalizedDescription,
        IReadOnlySet<string> tokens)
    {
        var orderedTokens = normalizedDescription
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        var merchantTokens = orderedTokens
            .Where(token =>
                token.Length > 2
                && !MerchantStopWords.Contains(token)
                && !token.All(char.IsDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (merchantTokens.Count == 0)
        {
            merchantTokens = orderedTokens
                .Where(token => token.Length > 2 && !token.All(char.IsDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var signatureTokens = orderedTokens
            .Where(token =>
                token.Length > 2
                && !RecurringSignatureStopWords.Contains(token)
                && !token.All(char.IsDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (signatureTokens.Count == 0)
        {
            signatureTokens = merchantTokens;
        }

        var merchantFamilyKey = ResolveMerchantFamilyKey(merchantTokens, signatureTokens, orderedTokens, normalizedDescription, MixedUseMerchantFamilies);
        var billingSignatureKey = ResolveBillingSignatureKey(merchantFamilyKey, signatureTokens);
        var mixedUseFamily = !string.IsNullOrWhiteSpace(merchantFamilyKey) && MixedUseMerchantFamilies.Contains(merchantFamilyKey);

        return new RecurringPatternTextDescriptor(
            NormalizedDescription: normalizedDescription,
            Tokens: tokens,
            MerchantTokens: merchantTokens,
            SignatureTokens: signatureTokens,
            MerchantFamilyKey: merchantFamilyKey,
            BillingSignatureKey: billingSignatureKey,
            IsMixedUseMerchantFamily: mixedUseFamily);
    }

    private static string ResolveMerchantFamilyKey(
        IReadOnlySet<string> merchantTokens,
        IReadOnlySet<string> signatureTokens,
        IReadOnlyList<string> orderedTokens,
        string normalizedDescription,
        IReadOnlySet<string> mixedUseMerchantFamilies)
    {
        foreach (var token in orderedTokens)
        {
            if (mixedUseMerchantFamilies.Contains(token))
            {
                return token;
            }
        }

        foreach (var token in orderedTokens)
        {
            if (merchantTokens.Contains(token) || signatureTokens.Contains(token))
            {
                return token;
            }
        }

        var explicitMixedUseFamily = signatureTokens
            .Concat(merchantTokens)
            .FirstOrDefault(token => mixedUseMerchantFamilies.Contains(token));
        if (!string.IsNullOrWhiteSpace(explicitMixedUseFamily))
        {
            return explicitMixedUseFamily;
        }

        if (merchantTokens.Count > 0)
        {
            return merchantTokens
                .OrderByDescending(token => signatureTokens.Contains(token))
                .ThenByDescending(token => token.Length)
                .ThenBy(token => token, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
        }

        if (signatureTokens.Count > 0)
        {
            return signatureTokens
                .OrderByDescending(token => token.Length)
                .ThenBy(token => token, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
        }

        return normalizedDescription
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string ResolveBillingSignatureKey(string merchantFamilyKey, IReadOnlySet<string> signatureTokens)
    {
        if (signatureTokens.Count == 0)
        {
            return merchantFamilyKey;
        }

        var ordered = signatureTokens
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ordered.Length <= 6
            ? string.Join('|', ordered)
            : string.Join('|', ordered.Take(6));
    }
}
