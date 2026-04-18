using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

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
