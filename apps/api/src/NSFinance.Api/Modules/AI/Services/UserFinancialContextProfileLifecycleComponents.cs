using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

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

public interface IUserFinancialProfileSerializationMapper
{
    UserFinancialContextProfileData ToState(UserFinancialContextProfile? profile, DateTime nowUtc);
    void ApplyToEntity(UserFinancialContextProfile profile, UserFinancialContextProfileData state);
    UserFinancialContextSnapshot ToSnapshot(UserFinancialContextProfileData state);
    string BuildFingerprint(UserFinancialContextProfileData state);
}

public sealed class UserFinancialProfileSerializationMapper : IUserFinancialProfileSerializationMapper
{
    private static readonly JsonSerializerOptions SignalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public UserFinancialContextProfileData ToState(UserFinancialContextProfile? profile, DateTime nowUtc)
    {
        var state = new UserFinancialContextProfileData(isNewProfile: profile is null);
        if (profile is null)
        {
            state.Lifecycle = new UserFinancialProfileLifecycleMetadata(
                FreshnessState: UserFinancialProfileFreshnessState.RefreshNeeded,
                SchemaVersion: 1,
                CreatedUtc: nowUtc,
                UpdatedUtc: nowUtc,
                LastRefreshedUtc: nowUtc);
            return state;
        }

        var explicitMap = DeserializeStringMap(profile.ExplicitSignalsJson);
        var inferredMap = DeserializeStringMap(profile.InferredSignalsJson);
        var metadataMap = DeserializeMetadataMap(profile.SignalMetadataJson);

        LoadSignalMap(explicitMap, isExplicit: true, state, metadataMap, nowUtc);
        LoadSignalMap(inferredMap, isExplicit: false, state, metadataMap, nowUtc);
        SeedLegacyValues(profile, state, metadataMap, nowUtc);
        PreserveUnmappedMetadata(state, metadataMap);

        var createdUtc = profile.CreatedUtc == default ? nowUtc : profile.CreatedUtc;
        var updatedUtc = profile.UpdatedUtc == default ? createdUtc : profile.UpdatedUtc;
        var lastRefreshedUtc = profile.LastRefreshedUtc == default ? updatedUtc : profile.LastRefreshedUtc;
        state.Lifecycle = new UserFinancialProfileLifecycleMetadata(
            FreshnessState: ParseFreshnessState(profile.FreshnessState),
            SchemaVersion: Math.Max(1, profile.ProfileSchemaVersion),
            CreatedUtc: createdUtc,
            UpdatedUtc: updatedUtc,
            LastRefreshedUtc: lastRefreshedUtc);
        return state;
    }

    public void ApplyToEntity(UserFinancialContextProfile profile, UserFinancialContextProfileData state)
    {
        var explicitMap = BuildSignalMap(state.ExplicitSignals, state.UnmappedExplicitSignals);
        var inferredMap = BuildSignalMap(state.InferredSignals, state.UnmappedInferredSignals);
        var metadataMap = BuildMetadataMap(state);

        profile.ExplicitSignalsJson = SerializeOrdered(explicitMap, SignalJsonOptions);
        profile.InferredSignalsJson = SerializeOrdered(inferredMap, SignalJsonOptions);
        profile.SignalMetadataJson = SerializeOrdered(metadataMap, MetadataJsonOptions);

        profile.Country = GetEffectiveValue(state, UserFinancialProfileSignalKey.Country) ?? "ZZ";
        profile.Currency = GetEffectiveValue(state, UserFinancialProfileSignalKey.Currency) ?? "EUR";
        profile.MonthlyIncomeRange = GetEffectiveValue(state, UserFinancialProfileSignalKey.MonthlyIncomeRange);
        profile.KnownObligationsJson = GetEffectiveValue(state, UserFinancialProfileSignalKey.KnownObligations) ?? "[]";
        profile.BudgetStructureJson = GetEffectiveValue(state, UserFinancialProfileSignalKey.BudgetStructure) ?? "{}";
        profile.ActivePlansJson = GetEffectiveValue(state, UserFinancialProfileSignalKey.ActivePlans) ?? "[]";
        profile.SpendingTendenciesJson = GetEffectiveValue(state, UserFinancialProfileSignalKey.SpendingTendencies) ?? "[]";
        profile.CategoryFlexibilityMarkersJson = GetEffectiveValue(state, UserFinancialProfileSignalKey.CategoryFlexibilityMarkers) ?? "[]";
        profile.AdviceStylePreference = GetEffectiveValue(state, UserFinancialProfileSignalKey.AdviceStylePreference) ?? "balanced";
        profile.FreshnessState = ToFreshnessStorageValue(state.Lifecycle.FreshnessState);
        profile.ProfileSchemaVersion = Math.Max(1, state.Lifecycle.SchemaVersion);
        profile.CreatedUtc = state.Lifecycle.CreatedUtc;
        profile.UpdatedUtc = state.Lifecycle.UpdatedUtc;
        profile.LastRefreshedUtc = state.Lifecycle.LastRefreshedUtc;
    }

    public UserFinancialContextSnapshot ToSnapshot(UserFinancialContextProfileData state)
    {
        return new UserFinancialContextSnapshot(
            Country: GetEffectiveValue(state, UserFinancialProfileSignalKey.Country) ?? "ZZ",
            Currency: GetEffectiveValue(state, UserFinancialProfileSignalKey.Currency) ?? "EUR",
            MonthlyIncomeRange: GetEffectiveValue(state, UserFinancialProfileSignalKey.MonthlyIncomeRange),
            KnownObligationsJson: GetEffectiveValue(state, UserFinancialProfileSignalKey.KnownObligations) ?? "[]",
            BudgetStructureJson: GetEffectiveValue(state, UserFinancialProfileSignalKey.BudgetStructure) ?? "{}",
            ActivePlansJson: GetEffectiveValue(state, UserFinancialProfileSignalKey.ActivePlans) ?? "[]",
            SpendingTendenciesJson: GetEffectiveValue(state, UserFinancialProfileSignalKey.SpendingTendencies) ?? "[]",
            CategoryFlexibilityMarkersJson: GetEffectiveValue(state, UserFinancialProfileSignalKey.CategoryFlexibilityMarkers) ?? "[]",
            AdviceStylePreference: GetEffectiveValue(state, UserFinancialProfileSignalKey.AdviceStylePreference) ?? "balanced");
    }

    public string BuildFingerprint(UserFinancialContextProfileData state)
    {
        var fingerprint = new
        {
            explicitSignals = BuildSignalMap(state.ExplicitSignals, state.UnmappedExplicitSignals),
            inferredSignals = BuildSignalMap(state.InferredSignals, state.UnmappedInferredSignals),
            metadata = BuildMetadataMap(state),
            lifecycle = new
            {
                freshness = state.Lifecycle.FreshnessState,
                schemaVersion = state.Lifecycle.SchemaVersion,
                created = state.Lifecycle.CreatedUtc,
                updated = state.Lifecycle.UpdatedUtc,
                refreshed = state.Lifecycle.LastRefreshedUtc
            }
        };
        return JsonSerializer.Serialize(fingerprint, MetadataJsonOptions);
    }

    private static void LoadSignalMap(
        IReadOnlyDictionary<string, string> sourceMap,
        bool isExplicit,
        UserFinancialContextProfileData state,
        IReadOnlyDictionary<string, UserFinancialProfileSignalMetadata> metadataMap,
        DateTime nowUtc)
    {
        foreach (var (storageKey, value) in sourceMap)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!UserFinancialProfileSignalCatalog.TryFromStorageKey(storageKey, out var signalKey))
            {
                var targetMap = isExplicit ? state.UnmappedExplicitSignals : state.UnmappedInferredSignals;
                targetMap[storageKey] = value;
                continue;
            }

            var metadata = CoerceMetadata(
                storageKey,
                signalKey,
                isExplicit,
                metadataMap,
                nowUtc);
            var signal = new UserFinancialProfileSignal(value, metadata);
            if (isExplicit)
            {
                state.ExplicitSignals[signalKey] = signal;
            }
            else
            {
                state.InferredSignals[signalKey] = signal;
            }
        }
    }

    private static void PreserveUnmappedMetadata(
        UserFinancialContextProfileData state,
        IReadOnlyDictionary<string, UserFinancialProfileSignalMetadata> metadataMap)
    {
        foreach (var (storageKey, metadata) in metadataMap)
        {
            var known = UserFinancialProfileSignalCatalog.TryFromStorageKey(storageKey, out _);
            var hasUnknownSignal = state.UnmappedExplicitSignals.ContainsKey(storageKey)
                                   || state.UnmappedInferredSignals.ContainsKey(storageKey);
            if (!known && hasUnknownSignal)
            {
                state.UnmappedMetadata[storageKey] = metadata;
            }
        }
    }

    private static void SeedLegacyValues(
        UserFinancialContextProfile profile,
        UserFinancialContextProfileData state,
        IReadOnlyDictionary<string, UserFinancialProfileSignalMetadata> metadataMap,
        DateTime nowUtc)
    {
        foreach (var key in UserFinancialProfileSignalCatalog.OrderedKeys)
        {
            if (state.ExplicitSignals.ContainsKey(key) || state.InferredSignals.ContainsKey(key))
            {
                continue;
            }

            var legacyValue = GetLegacyValue(profile, key);
            if (string.IsNullOrWhiteSpace(legacyValue))
            {
                continue;
            }

            var storageKey = UserFinancialProfileSignalCatalog.ToStorageKey(key);
            var metadata = CoerceMetadata(
                storageKey,
                key,
                isExplicit: false,
                metadataMap,
                nowUtc);
            state.InferredSignals[key] = new UserFinancialProfileSignal(legacyValue, metadata);
        }
    }

    private static UserFinancialProfileSignalMetadata CoerceMetadata(
        string storageKey,
        UserFinancialProfileSignalKey key,
        bool isExplicit,
        IReadOnlyDictionary<string, UserFinancialProfileSignalMetadata> metadataMap,
        DateTime nowUtc)
    {
        if (metadataMap.TryGetValue(storageKey, out var metadata))
        {
            var updatedAt = metadata.UpdatedAtUtc == default ? nowUtc : metadata.UpdatedAtUtc;
            if (isExplicit)
            {
                return new UserFinancialProfileSignalMetadata(
                    Source: UserFinancialProfileSignalSource.ExplicitUser,
                    Strength: UserFinancialProfileSignalStrength.Explicit,
                    IsExplicit: true,
                    UpdatedAtUtc: updatedAt);
            }

            return new UserFinancialProfileSignalMetadata(
                Source: metadata.IsExplicit
                    ? UserFinancialProfileSignalCatalog.GetDefaultInferredSource(key)
                    : metadata.Source,
                Strength: metadata.Strength == UserFinancialProfileSignalStrength.Explicit
                    ? UserFinancialProfileSignalStrength.Acceptable
                    : metadata.Strength,
                IsExplicit: false,
                UpdatedAtUtc: updatedAt);
        }

        if (isExplicit)
        {
            return new UserFinancialProfileSignalMetadata(
                Source: UserFinancialProfileSignalSource.ExplicitUser,
                Strength: UserFinancialProfileSignalStrength.Explicit,
                IsExplicit: true,
                UpdatedAtUtc: nowUtc);
        }

        return new UserFinancialProfileSignalMetadata(
            Source: UserFinancialProfileSignalCatalog.GetDefaultInferredSource(key),
            Strength: UserFinancialProfileSignalStrength.Acceptable,
            IsExplicit: false,
            UpdatedAtUtc: nowUtc);
    }

    private static Dictionary<string, string> BuildSignalMap(
        IReadOnlyDictionary<UserFinancialProfileSignalKey, UserFinancialProfileSignal> knownSignals,
        IReadOnlyDictionary<string, string> unmappedSignals)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in UserFinancialProfileSignalCatalog.OrderedKeys)
        {
            if (knownSignals.TryGetValue(key, out var signal)
                && !string.IsNullOrWhiteSpace(signal.Value))
            {
                result[UserFinancialProfileSignalCatalog.ToStorageKey(key)] = signal.Value;
            }
        }

        foreach (var (storageKey, value) in unmappedSignals)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[storageKey] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, UserFinancialProfileSignalMetadata> BuildMetadataMap(UserFinancialContextProfileData state)
    {
        var result = new Dictionary<string, UserFinancialProfileSignalMetadata>(StringComparer.Ordinal);
        foreach (var (key, signal) in state.ExplicitSignals)
        {
            result[UserFinancialProfileSignalCatalog.ToStorageKey(key)] = signal.Metadata;
        }

        foreach (var (key, signal) in state.InferredSignals)
        {
            if (!state.ExplicitSignals.ContainsKey(key))
            {
                result[UserFinancialProfileSignalCatalog.ToStorageKey(key)] = signal.Metadata;
            }
        }

        foreach (var (storageKey, metadata) in state.UnmappedMetadata)
        {
            result[storageKey] = metadata;
        }

        return result;
    }

    private static string? GetLegacyValue(UserFinancialContextProfile profile, UserFinancialProfileSignalKey key)
    {
        return key switch
        {
            UserFinancialProfileSignalKey.Country => profile.Country,
            UserFinancialProfileSignalKey.Currency => profile.Currency,
            UserFinancialProfileSignalKey.MonthlyIncomeRange => profile.MonthlyIncomeRange,
            UserFinancialProfileSignalKey.KnownObligations => profile.KnownObligationsJson,
            UserFinancialProfileSignalKey.BudgetStructure => profile.BudgetStructureJson,
            UserFinancialProfileSignalKey.ActivePlans => profile.ActivePlansJson,
            UserFinancialProfileSignalKey.SpendingTendencies => profile.SpendingTendenciesJson,
            UserFinancialProfileSignalKey.CategoryFlexibilityMarkers => profile.CategoryFlexibilityMarkersJson,
            UserFinancialProfileSignalKey.AdviceStylePreference => profile.AdviceStylePreference,
            _ => null
        };
    }

    private static string? GetEffectiveValue(UserFinancialContextProfileData state, UserFinancialProfileSignalKey key)
    {
        if (state.ExplicitSignals.TryGetValue(key, out var explicitSignal)
            && !string.IsNullOrWhiteSpace(explicitSignal.Value))
        {
            return explicitSignal.Value;
        }

        if (state.InferredSignals.TryGetValue(key, out var inferredSignal)
            && !string.IsNullOrWhiteSpace(inferredSignal.Value))
        {
            return inferredSignal.Value;
        }

        return UserFinancialProfileSignalCatalog.GetFallbackValue(key);
    }

    private static Dictionary<string, string> DeserializeStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SignalJsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, UserFinancialProfileSignalMetadata> DeserializeMetadataMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, UserFinancialProfileSignalMetadata>>(json, MetadataJsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string SerializeOrdered<TValue>(
        IReadOnlyDictionary<string, TValue> map,
        JsonSerializerOptions options)
    {
        var ordered = map
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered, options);
    }

    private static UserFinancialProfileFreshnessState ParseFreshnessState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UserFinancialProfileFreshnessState.RefreshNeeded;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "fresh" => UserFinancialProfileFreshnessState.Fresh,
            "stale" => UserFinancialProfileFreshnessState.Stale,
            "refresh_needed" => UserFinancialProfileFreshnessState.RefreshNeeded,
            "refreshneeded" => UserFinancialProfileFreshnessState.RefreshNeeded,
            _ => UserFinancialProfileFreshnessState.RefreshNeeded
        };
    }

    private static string ToFreshnessStorageValue(UserFinancialProfileFreshnessState freshnessState)
    {
        return freshnessState switch
        {
            UserFinancialProfileFreshnessState.Fresh => "fresh",
            UserFinancialProfileFreshnessState.Stale => "stale",
            _ => "refresh_needed"
        };
    }
}

public interface IUserFinancialProfileSignalMetadataPolicy
{
    void EnsureSignalMetadataCoherence(UserFinancialContextProfileData state, DateTime nowUtc);
}

public sealed class UserFinancialProfileSignalMetadataPolicy : IUserFinancialProfileSignalMetadataPolicy
{
    public void EnsureSignalMetadataCoherence(UserFinancialContextProfileData state, DateTime nowUtc)
    {
        CoerceMap(state.ExplicitSignals, isExplicit: true, nowUtc);
        CoerceMap(state.InferredSignals, isExplicit: false, nowUtc);

        var keysToDrop = state.UnmappedMetadata
            .Where(pair => !state.UnmappedExplicitSignals.ContainsKey(pair.Key)
                           && !state.UnmappedInferredSignals.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in keysToDrop)
        {
            state.UnmappedMetadata.Remove(key);
        }
    }

    private static void CoerceMap(
        IDictionary<UserFinancialProfileSignalKey, UserFinancialProfileSignal> map,
        bool isExplicit,
        DateTime nowUtc)
    {
        var keysToRemove = map
            .Where(x => string.IsNullOrWhiteSpace(x.Value.Value))
            .Select(x => x.Key)
            .ToArray();
        foreach (var key in keysToRemove)
        {
            map.Remove(key);
        }

        var keys = map.Keys.ToArray();
        foreach (var key in keys)
        {
            var existing = map[key];
            var updatedAt = existing.Metadata.UpdatedAtUtc == default ? nowUtc : existing.Metadata.UpdatedAtUtc;
            if (isExplicit)
            {
                map[key] = existing with
                {
                    Metadata = new UserFinancialProfileSignalMetadata(
                        Source: UserFinancialProfileSignalSource.ExplicitUser,
                        Strength: UserFinancialProfileSignalStrength.Explicit,
                        IsExplicit: true,
                        UpdatedAtUtc: updatedAt)
                };
            }
            else
            {
                map[key] = existing with
                {
                    Metadata = new UserFinancialProfileSignalMetadata(
                        Source: existing.Metadata.IsExplicit
                            ? UserFinancialProfileSignalCatalog.GetDefaultInferredSource(key)
                            : existing.Metadata.Source,
                        Strength: existing.Metadata.Strength == UserFinancialProfileSignalStrength.Explicit
                            ? UserFinancialProfileSignalStrength.Acceptable
                            : existing.Metadata.Strength,
                        IsExplicit: false,
                        UpdatedAtUtc: updatedAt)
                };
            }
        }
    }
}

public interface IUserFinancialProfileInferencePersistencePolicy
{
    bool CanPersist(UserFinancialProfileInferredSignalCandidate candidate);
    bool CanReplace(UserFinancialProfileSignal existingSignal, UserFinancialProfileInferredSignalCandidate candidate);
}

public sealed class UserFinancialProfileInferencePersistencePolicy : IUserFinancialProfileInferencePersistencePolicy
{
    public bool CanPersist(UserFinancialProfileInferredSignalCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Value))
        {
            return false;
        }

        return ToTier(candidate.Strength) >= UserFinancialProfileInferenceStrengthTier.Moderate;
    }

    public bool CanReplace(UserFinancialProfileSignal existingSignal, UserFinancialProfileInferredSignalCandidate candidate)
    {
        if (existingSignal.Metadata.IsExplicit)
        {
            return false;
        }

        var incomingTier = ToTier(candidate.Strength);
        if (incomingTier < UserFinancialProfileInferenceStrengthTier.Moderate)
        {
            return false;
        }

        var existingTier = ToTier(existingSignal.Metadata.Strength);
        if (incomingTier < existingTier)
        {
            return false;
        }

        return incomingTier > existingTier
               || !string.Equals(existingSignal.Value, candidate.Value, StringComparison.Ordinal);
    }

    private static UserFinancialProfileInferenceStrengthTier ToTier(UserFinancialProfileSignalStrength strength)
    {
        return strength switch
        {
            UserFinancialProfileSignalStrength.Strong => UserFinancialProfileInferenceStrengthTier.Strong,
            UserFinancialProfileSignalStrength.Acceptable => UserFinancialProfileInferenceStrengthTier.Moderate,
            UserFinancialProfileSignalStrength.Explicit => UserFinancialProfileInferenceStrengthTier.Strong,
            _ => UserFinancialProfileInferenceStrengthTier.Weak
        };
    }
}

public interface IUserFinancialProfileMergePolicy
{
    void ApplyExplicitSignals(User user, UserFinancialContextProfileData state, DateTime nowUtc);
    void ApplyInferredSignals(
        UserFinancialContextProfileData state,
        IReadOnlyList<UserFinancialProfileInferredSignalCandidate> candidates,
        IUserFinancialProfileInferencePersistencePolicy persistencePolicy,
        DateTime nowUtc);
    void ResolveCanonicalValues(UserFinancialContextProfileData state, DateTime nowUtc);
}

public sealed class UserFinancialProfileMergePolicy : IUserFinancialProfileMergePolicy
{
    public void ApplyExplicitSignals(User user, UserFinancialContextProfileData state, DateTime nowUtc)
    {
        UpsertExplicit(
            state,
            UserFinancialProfileSignalKey.Country,
            UserFinancialProfileValueNormalizer.NormalizeCountry(user.CountryRegion),
            nowUtc);
        UpsertExplicit(
            state,
            UserFinancialProfileSignalKey.Currency,
            UserFinancialProfileValueNormalizer.NormalizeCurrency(user.PreferredCurrency),
            nowUtc);
        UpsertExplicit(
            state,
            UserFinancialProfileSignalKey.AdviceStylePreference,
            UserFinancialProfileValueNormalizer.NormalizeAdviceStyle(user.Preferences?.AdviceTonePreference),
            nowUtc);

        var categoryMarkers = UserFinancialProfileValueNormalizer.NormalizeJsonOrDefault(
            user.Preferences?.EssentialCategoryPreferencesJson,
            "{}");
        if (!string.Equals(categoryMarkers, "{}", StringComparison.Ordinal))
        {
            UpsertExplicit(
                state,
                UserFinancialProfileSignalKey.CategoryFlexibilityMarkers,
                categoryMarkers,
                nowUtc);
        }
        else
        {
            state.ExplicitSignals.Remove(UserFinancialProfileSignalKey.CategoryFlexibilityMarkers);
        }
    }

    public void ApplyInferredSignals(
        UserFinancialContextProfileData state,
        IReadOnlyList<UserFinancialProfileInferredSignalCandidate> candidates,
        IUserFinancialProfileInferencePersistencePolicy persistencePolicy,
        DateTime nowUtc)
    {
        foreach (var candidate in candidates)
        {
            if (!persistencePolicy.CanPersist(candidate))
            {
                continue;
            }

            if (state.ExplicitSignals.ContainsKey(candidate.Key))
            {
                continue;
            }

            if (state.InferredSignals.TryGetValue(candidate.Key, out var existingSignal)
                && !persistencePolicy.CanReplace(existingSignal, candidate))
            {
                continue;
            }

            state.InferredSignals[candidate.Key] = new UserFinancialProfileSignal(
                Value: candidate.Value,
                Metadata: new UserFinancialProfileSignalMetadata(
                    Source: candidate.Source,
                    Strength: candidate.Strength,
                    IsExplicit: false,
                    UpdatedAtUtc: nowUtc));
        }
    }

    public void ResolveCanonicalValues(UserFinancialContextProfileData state, DateTime nowUtc)
    {
        foreach (var key in UserFinancialProfileSignalCatalog.OrderedKeys)
        {
            if (state.ExplicitSignals.TryGetValue(key, out var explicitSignal)
                && !string.IsNullOrWhiteSpace(explicitSignal.Value))
            {
                continue;
            }

            if (state.InferredSignals.TryGetValue(key, out var inferredSignal)
                && !string.IsNullOrWhiteSpace(inferredSignal.Value))
            {
                continue;
            }

            var fallback = UserFinancialProfileSignalCatalog.GetFallbackValue(key);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                continue;
            }

            state.InferredSignals[key] = new UserFinancialProfileSignal(
                Value: fallback,
                Metadata: new UserFinancialProfileSignalMetadata(
                    Source: UserFinancialProfileSignalSource.SystemDefault,
                    Strength: UserFinancialProfileSignalStrength.Weak,
                    IsExplicit: false,
                    UpdatedAtUtc: nowUtc));
        }
    }

    private static void UpsertExplicit(
        UserFinancialContextProfileData state,
        UserFinancialProfileSignalKey key,
        string value,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        state.ExplicitSignals[key] = new UserFinancialProfileSignal(
            Value: value,
            Metadata: new UserFinancialProfileSignalMetadata(
                Source: UserFinancialProfileSignalSource.ExplicitUser,
                Strength: UserFinancialProfileSignalStrength.Explicit,
                IsExplicit: true,
                UpdatedAtUtc: nowUtc));
    }
}

public interface IUserFinancialProfileInferenceBuilder
{
    Task<IReadOnlyList<UserFinancialProfileInferredSignalCandidate>> BuildCandidatesAsync(
        Guid userId,
        UserFinancialContextProfileData currentState,
        CancellationToken cancellationToken);
}

public sealed class UserFinancialProfileInferenceBuilder(
    AppDbContext dbContext,
    IUserFinancialSummaryService summaryService,
    IRecurringObligationsService recurringObligationsService,
    IBudgetStatusService budgetStatusService,
    ISpendingAnalysisService spendingAnalysisService,
    IOptions<CompanionProfileLifecycleOptions> options,
    ILogger<UserFinancialProfileInferenceBuilder> logger) : IUserFinancialProfileInferenceBuilder
{
    private static readonly JsonSerializerOptions InferencePayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly CompanionProfileLifecycleOptions _options = options.Value;

    public async Task<IReadOnlyList<UserFinancialProfileInferredSignalCandidate>> BuildCandidatesAsync(
        Guid userId,
        UserFinancialContextProfileData currentState,
        CancellationToken cancellationToken)
    {
        var candidates = new List<UserFinancialProfileInferredSignalCandidate>(8);
        UserFinancialSummary? summary = null;
        RecurringObligationsResult? recurring = null;
        BudgetStatusResult? budget = null;
        SpendingAnalysisResult? spending = null;

        try
        {
            summary = await summaryService.GetSummaryAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Financial summary unavailable for profile inference. userId={UserId}", userId);
        }

        try
        {
            recurring = await recurringObligationsService.GetRecurringAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recurring obligations unavailable for profile inference. userId={UserId}", userId);
        }

        try
        {
            budget = await budgetStatusService.GetBudgetStatusAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Budget status unavailable for profile inference. userId={UserId}", userId);
        }

        try
        {
            spending = await spendingAnalysisService.AnalyzeAsync(
                userId,
                Math.Clamp(_options.SpendingAnalysisLookbackDays, 14, 180),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Spending analysis unavailable for profile inference. userId={UserId}", userId);
        }

        if (summary is not null)
        {
            var incomeRange = UserFinancialProfileValueNormalizer.DeriveIncomeRange(summary.IncomeLast30Days);
            if (!string.IsNullOrWhiteSpace(incomeRange))
            {
                candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                    Key: UserFinancialProfileSignalKey.MonthlyIncomeRange,
                    Value: incomeRange,
                    Source: UserFinancialProfileSignalSource.InferredFromSummary,
                    Strength: summary.IncomeLast30Days >= 4_000m
                        ? UserFinancialProfileSignalStrength.Strong
                        : UserFinancialProfileSignalStrength.Acceptable));
            }

            if (!currentState.ExplicitSignals.ContainsKey(UserFinancialProfileSignalKey.Currency))
            {
                candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                    Key: UserFinancialProfileSignalKey.Currency,
                    Value: UserFinancialProfileValueNormalizer.NormalizeCurrency(summary.Currency),
                    Source: UserFinancialProfileSignalSource.InferredFromSummary,
                    Strength: UserFinancialProfileSignalStrength.Acceptable));
            }
        }

        if (recurring is not null && recurring.Items.Count > 0)
        {
            var recurringItems = recurring.Items
                .Take(Math.Clamp(_options.MaxRecurringObligations, 1, 32))
                .ToArray();
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.KnownObligations,
                Value: JsonSerializer.Serialize(recurringItems, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromRecurringObligations,
                Strength: recurring.Items.Count >= 2
                          || recurring.EstimatedMonthlyTotal >= _options.StrongRecurringMonthlyTotalThreshold
                    ? UserFinancialProfileSignalStrength.Strong
                    : UserFinancialProfileSignalStrength.Acceptable));
        }

        if (budget is not null && (budget.HasBudgetPlan || budget.MonthToDateSpend > 0m))
        {
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.BudgetStructure,
                Value: JsonSerializer.Serialize(new
                {
                    budget.HasBudgetPlan,
                    budget.MonthlyBudget,
                    budget.MonthToDateSpend,
                    budget.RemainingBudget
                }, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromBudget,
                Strength: budget.HasBudgetPlan || budget.MonthToDateSpend >= _options.StrongMonthToDateSpendThreshold
                    ? UserFinancialProfileSignalStrength.Strong
                    : UserFinancialProfileSignalStrength.Acceptable));
        }

        var activePlans = await dbContext.ExpensePlans
            .AsNoTracking()
            .Where(x => x.UserId == userId && (x.Status == "active" || x.ActivatedAtUtc.HasValue))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(_options.MaxActivePlans, 1, 20))
            .Select(x => new { x.Id, x.Title, x.Status, x.ExpectedSpendTotal, x.CurrencyCode })
            .ToListAsync(cancellationToken);
        if (activePlans.Count > 0)
        {
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.ActivePlans,
                Value: JsonSerializer.Serialize(activePlans, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromPlanData,
                Strength: UserFinancialProfileSignalStrength.Strong));
        }

        if (spending is not null && spending.SpendByDomain.Count > 0 && spending.AverageDailySpend > 0m)
        {
            var nonZeroDomainCount = spending.SpendByDomain.Count(x => x.Value > 0m);
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.SpendingTendencies,
                Value: JsonSerializer.Serialize(new
                {
                    spending.AverageDailySpend,
                    spending.LargestExpense,
                    spendByDomain = spending.SpendByDomain
                }, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                Strength: nonZeroDomainCount >= Math.Max(1, _options.StrongSpendingDomainCountThreshold)
                          && spending.AverageDailySpend >= _options.StrongAverageDailySpendThreshold
                          && spending.LargestExpense >= _options.StrongLargestExpenseThreshold
                    ? UserFinancialProfileSignalStrength.Strong
                    : UserFinancialProfileSignalStrength.Acceptable));
        }

        return candidates;
    }
}

public interface IUserFinancialProfileLifecycleInvariantValidator
{
    void EnsureValid(UserFinancialContextProfileData state, DateTime nowUtc);
}

public sealed class UserFinancialProfileLifecycleInvariantValidator : IUserFinancialProfileLifecycleInvariantValidator
{
    public void EnsureValid(UserFinancialContextProfileData state, DateTime nowUtc)
    {
        var createdUtc = state.Lifecycle.CreatedUtc == default ? nowUtc : state.Lifecycle.CreatedUtc;
        var updatedUtc = state.Lifecycle.UpdatedUtc == default ? createdUtc : state.Lifecycle.UpdatedUtc;
        if (updatedUtc < createdUtc)
        {
            updatedUtc = createdUtc;
        }

        var refreshedUtc = state.Lifecycle.LastRefreshedUtc == default ? updatedUtc : state.Lifecycle.LastRefreshedUtc;
        if (refreshedUtc < createdUtc)
        {
            refreshedUtc = updatedUtc;
        }

        state.Lifecycle = state.Lifecycle with
        {
            SchemaVersion = Math.Max(1, state.Lifecycle.SchemaVersion),
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
            LastRefreshedUtc = refreshedUtc
        };
    }
}

internal static class UserFinancialProfileValueNormalizer
{
    public static string NormalizeCountry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "ZZ";
        }

        return raw.Trim().ToUpperInvariant();
    }

    public static string NormalizeCurrency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "EUR";
        }

        var cleaned = raw.Trim().ToUpperInvariant();
        return cleaned.Length == 3 ? cleaned : "EUR";
    }

    public static string NormalizeAdviceStyle(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "conservative" => "conservative",
            "flexible" => "flexible",
            _ => "balanced"
        };
    }

    public static string NormalizeJsonOrDefault(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        try
        {
            _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch
        {
            return fallback;
        }
    }

    public static string? DeriveIncomeRange(decimal incomeLast30Days)
    {
        if (incomeLast30Days <= 0m)
        {
            return null;
        }

        return incomeLast30Days switch
        {
            < 2000m => "0-2000",
            < 4000m => "2000-4000",
            < 7000m => "4000-7000",
            _ => "7000+"
        };
    }
}
