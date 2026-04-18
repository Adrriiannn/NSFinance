namespace NSFinance.Api.Modules.AI.Services;

public enum UserFinancialProfileFreshnessState
{
    Fresh = 0,
    Stale = 1,
    RefreshNeeded = 2
}

public enum UserFinancialProfileSignalSource
{
    ExplicitUser = 0,
    InferredFromSummary = 1,
    InferredFromBudget = 2,
    InferredFromSpendingPattern = 3,
    InferredFromPlanData = 4,
    InferredFromRecurringObligations = 5,
    SystemDefault = 6
}

public enum UserFinancialProfileSignalStrength
{
    Weak = 0,
    Acceptable = 1,
    Strong = 2,
    Explicit = 3
}

public sealed record UserFinancialProfileSignalMetadata(
    UserFinancialProfileSignalSource Source,
    UserFinancialProfileSignalStrength Strength,
    bool IsExplicit,
    DateTime UpdatedAtUtc);

public enum UserFinancialProfileSignalKey
{
    Country = 0,
    Currency = 1,
    MonthlyIncomeRange = 2,
    KnownObligations = 3,
    BudgetStructure = 4,
    ActivePlans = 5,
    SpendingTendencies = 6,
    CategoryFlexibilityMarkers = 7,
    AdviceStylePreference = 8
}

public enum UserFinancialProfileInferenceStrengthTier
{
    Weak = 0,
    Moderate = 1,
    Strong = 2
}

public sealed record UserFinancialProfileSignal(
    string Value,
    UserFinancialProfileSignalMetadata Metadata);

public sealed record UserFinancialProfileLifecycleMetadata(
    UserFinancialProfileFreshnessState FreshnessState,
    int SchemaVersion,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime LastRefreshedUtc);

public sealed class UserFinancialContextProfileData
{
    public UserFinancialContextProfileData(bool isNewProfile)
    {
        IsNewProfile = isNewProfile;
    }

    public bool IsNewProfile { get; }
    public Dictionary<UserFinancialProfileSignalKey, UserFinancialProfileSignal> ExplicitSignals { get; } = [];
    public Dictionary<UserFinancialProfileSignalKey, UserFinancialProfileSignal> InferredSignals { get; } = [];
    public Dictionary<string, string> UnmappedExplicitSignals { get; } = [];
    public Dictionary<string, string> UnmappedInferredSignals { get; } = [];
    public Dictionary<string, UserFinancialProfileSignalMetadata> UnmappedMetadata { get; } = [];
    public UserFinancialProfileLifecycleMetadata Lifecycle { get; set; } = new(
        FreshnessState: UserFinancialProfileFreshnessState.RefreshNeeded,
        SchemaVersion: 1,
        CreatedUtc: DateTime.UtcNow,
        UpdatedUtc: DateTime.UtcNow,
        LastRefreshedUtc: DateTime.UtcNow);
}

public sealed record UserFinancialProfileInferredSignalCandidate(
    UserFinancialProfileSignalKey Key,
    string Value,
    UserFinancialProfileSignalSource Source,
    UserFinancialProfileSignalStrength Strength);

public static class UserFinancialProfileSignalCatalog
{
    private sealed record SignalDefinition(string StorageKey, bool IsOptional, string? FallbackValue, bool IsJson);

    private static readonly Dictionary<UserFinancialProfileSignalKey, SignalDefinition> Definitions = new()
    {
        [UserFinancialProfileSignalKey.Country] = new("country", false, "ZZ", false),
        [UserFinancialProfileSignalKey.Currency] = new("currency", false, "EUR", false),
        [UserFinancialProfileSignalKey.MonthlyIncomeRange] = new("monthly_income_range", true, null, false),
        [UserFinancialProfileSignalKey.KnownObligations] = new("known_obligations", false, "[]", true),
        [UserFinancialProfileSignalKey.BudgetStructure] = new("budget_structure", false, "{}", true),
        [UserFinancialProfileSignalKey.ActivePlans] = new("active_plans", false, "[]", true),
        [UserFinancialProfileSignalKey.SpendingTendencies] = new("spending_tendencies", false, "[]", true),
        [UserFinancialProfileSignalKey.CategoryFlexibilityMarkers] = new("category_flexibility_markers", false, "[]", true),
        [UserFinancialProfileSignalKey.AdviceStylePreference] = new("advice_style_preference", false, "balanced", false)
    };

    private static readonly Dictionary<string, UserFinancialProfileSignalKey> StorageLookup = Definitions
        .ToDictionary(x => x.Value.StorageKey, x => x.Key, StringComparer.Ordinal);

    public static IReadOnlyList<UserFinancialProfileSignalKey> OrderedKeys { get; } = Definitions.Keys
        .OrderBy(key => key)
        .ToArray();

    public static string ToStorageKey(UserFinancialProfileSignalKey key) => Definitions[key].StorageKey;

    public static bool TryFromStorageKey(string key, out UserFinancialProfileSignalKey signalKey)
    {
        return StorageLookup.TryGetValue(key, out signalKey);
    }

    public static bool IsOptional(UserFinancialProfileSignalKey key) => Definitions[key].IsOptional;

    public static string? GetFallbackValue(UserFinancialProfileSignalKey key) => Definitions[key].FallbackValue;

    public static bool IsJsonSignal(UserFinancialProfileSignalKey key) => Definitions[key].IsJson;

    public static UserFinancialProfileSignalSource GetDefaultInferredSource(UserFinancialProfileSignalKey key)
    {
        return key switch
        {
            UserFinancialProfileSignalKey.BudgetStructure => UserFinancialProfileSignalSource.InferredFromBudget,
            UserFinancialProfileSignalKey.KnownObligations => UserFinancialProfileSignalSource.InferredFromRecurringObligations,
            UserFinancialProfileSignalKey.ActivePlans => UserFinancialProfileSignalSource.InferredFromPlanData,
            UserFinancialProfileSignalKey.SpendingTendencies => UserFinancialProfileSignalSource.InferredFromSpendingPattern,
            _ => UserFinancialProfileSignalSource.InferredFromSummary
        };
    }
}

