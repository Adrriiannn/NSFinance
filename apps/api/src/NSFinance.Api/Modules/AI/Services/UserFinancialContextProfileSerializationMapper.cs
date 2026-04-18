using System.Text.Json;
using System.Text.Json.Serialization;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

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

