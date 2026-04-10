namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public enum RecurringConfidenceTier
{
    None = 0,
    Weak = 1,
    Probable = 2,
    Strong = 3
}

public enum RecurringCadence
{
    Weekly = 0,
    BiWeekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
    Irregular = 5,
    Unknown = 6
}

public sealed record RecurringSignalBreakdown(
    double MerchantConsistencyScore,
    double IntervalConsistencyScore,
    double AmountConsistencyScore,
    double DirectionConsistencyScore,
    double ContinuityScore,
    double PenaltyScore)
{
    public static RecurringSignalBreakdown Empty { get; } = new(0d, 0d, 0d, 0d, 0d, 0d);
}

public sealed record RecurringPatternResult(
    bool IsRecurring,
    RecurringConfidenceTier ConfidenceTier,
    double ConfidenceScore,
    RecurringCadence? Cadence,
    int OccurrenceCount,
    IReadOnlyList<Guid> MatchedTransactionIds,
    RecurringSignalBreakdown Signals,
    IReadOnlyList<string> ReasonCodes)
{
    public static RecurringPatternResult None(
        IReadOnlyList<string>? reasonCodes = null,
        RecurringSignalBreakdown? signals = null,
        int occurrenceCount = 0,
        IReadOnlyList<Guid>? matchedTransactionIds = null,
        double confidenceScore = 0d,
        RecurringCadence cadence = RecurringCadence.Unknown) =>
        new(
            IsRecurring: false,
            ConfidenceTier: RecurringConfidenceTier.None,
            ConfidenceScore: Math.Clamp(confidenceScore, 0d, 100d),
            Cadence: cadence,
            OccurrenceCount: occurrenceCount,
            MatchedTransactionIds: matchedTransactionIds ?? [],
            Signals: signals ?? RecurringSignalBreakdown.Empty,
            ReasonCodes: reasonCodes ?? []);
}

public static class RecurringPatternReasonCodes
{
    public const string MinimumPriorMatchesNotMet = "MINIMUM_PRIOR_MATCHES_NOT_MET";
    public const string ZeroAmountNotSupported = "ZERO_AMOUNT_NOT_SUPPORTED";
    public const string MerchantExactMatch = "MERCHANT_EXACT_MATCH";
    public const string MerchantFuzzyMatch = "MERCHANT_FUZZY_MATCH";
    public const string MerchantDescriptionSimilarity = "DESCRIPTION_SIMILARITY_MATCH";
    public const string WeeklyIntervalCluster = "WEEKLY_INTERVAL_CLUSTER";
    public const string BiWeeklyIntervalCluster = "BIWEEKLY_INTERVAL_CLUSTER";
    public const string MonthlyIntervalCluster = "MONTHLY_INTERVAL_CLUSTER";
    public const string QuarterlyIntervalCluster = "QUARTERLY_INTERVAL_CLUSTER";
    public const string YearlyIntervalCluster = "YEARLY_INTERVAL_CLUSTER";
    public const string IrregularIntervalPattern = "IRREGULAR_INTERVAL_PATTERN";
    public const string AmountWithinTolerance = "AMOUNT_WITHIN_TOLERANCE";
    public const string AmountVarianceHigh = "AMOUNT_VARIANCE_HIGH";
    public const string IntervalVarianceHigh = "INTERVAL_VARIANCE_HIGH";
    public const string ThreeOrMoreOccurrences = "THREE_OR_MORE_OCCURRENCES";
    public const string FourOrMoreOccurrences = "FOUR_OR_MORE_OCCURRENCES";
    public const string MissingCycleGap = "MISSING_CYCLE_GAP";
    public const string MerchantDiscretionaryPattern = "MERCHANT_DISCRETIONARY_PATTERN";
    public const string TooCloseClustering = "TOO_CLOSE_CLUSTERING";
    public const string MixedDescriptions = "MIXED_DESCRIPTIONS";
    public const string MixedDirectionObserved = "MIXED_DIRECTION_OBSERVED";
    public const string BlockedByHighIntervalVariance = "BLOCKED_BY_HIGH_INTERVAL_VARIANCE";
    public const string BlockedByHighAmountVariance = "BLOCKED_BY_HIGH_AMOUNT_VARIANCE";
    public const string BlockedByDiscretionaryMerchant = "BLOCKED_BY_DISCRETIONARY_MERCHANT";
    public const string BlockedByMixedDirection = "BLOCKED_BY_MIXED_DIRECTION";
}

