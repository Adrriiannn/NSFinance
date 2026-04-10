namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed record RecurringPatternTextDescriptor(
    string NormalizedDescription,
    IReadOnlySet<string> Tokens,
    IReadOnlySet<string> MerchantTokens);

public sealed class RecurringPatternOptions
{
    public int MinimumPriorMatches { get; init; } = 2;
    public TimeSpan LookbackWindow { get; init; } = TimeSpan.FromDays(730);
    public double MerchantFuzzyMatchThreshold { get; init; } = 0.62d;
    public double DescriptionSimilarityThreshold { get; init; } = 0.68d;
    public double AmountTolerancePercent { get; init; } = 0.08d;
    public bool RequireSameCurrency { get; init; } = true;
    public int MaxCandidatePoolSize { get; init; } = 64;
    public IReadOnlyDictionary<Guid, RecurringPatternTextDescriptor>? PrecomputedTextByTransactionId { get; init; }
    public IReadOnlySet<string> DiscretionaryMerchantTokens { get; init; } = DefaultDiscretionaryMerchantTokens;
    public IReadOnlySet<string> MerchantStopWords { get; init; } = DefaultMerchantStopWords;

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
}
