using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class UserFinancialContextProfileLifecycleComponentTests
{
    [Fact]
    public void Mapper_ToState_HandlesLegacyAndInvalidJsonSafely()
    {
        var mapper = new UserFinancialProfileSerializationMapper();
        var profile = new UserFinancialContextProfile
        {
            UserId = Guid.NewGuid(),
            Country = "IE",
            Currency = "EUR",
            MonthlyIncomeRange = "2000-4000",
            KnownObligationsJson = "[]",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = "[]",
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "balanced",
            ExplicitSignalsJson = "{invalid",
            InferredSignalsJson = "{invalid",
            SignalMetadataJson = "{invalid",
            FreshnessState = "unknown_legacy_value",
            ProfileSchemaVersion = 0,
            CreatedUtc = DateTime.UtcNow.AddDays(-10),
            UpdatedUtc = DateTime.UtcNow.AddDays(-1),
            LastRefreshedUtc = default
        };

        var state = mapper.ToState(profile, DateTime.UtcNow);

        Assert.False(state.IsNewProfile);
        Assert.Equal(UserFinancialProfileFreshnessState.RefreshNeeded, state.Lifecycle.FreshnessState);
        Assert.True(state.InferredSignals.ContainsKey(UserFinancialProfileSignalKey.Country));
        Assert.True(state.InferredSignals.ContainsKey(UserFinancialProfileSignalKey.Currency));
        Assert.True(state.InferredSignals.ContainsKey(UserFinancialProfileSignalKey.AdviceStylePreference));
    }

    [Fact]
    public void Mapper_RoundTrip_PreservesUnknownSignalPayloads()
    {
        var mapper = new UserFinancialProfileSerializationMapper();
        var nowUtc = DateTime.UtcNow;
        var profile = new UserFinancialContextProfile
        {
            UserId = Guid.NewGuid(),
            Country = "IE",
            Currency = "EUR",
            MonthlyIncomeRange = null,
            KnownObligationsJson = "[]",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = "[]",
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "balanced",
            ExplicitSignalsJson = """{"country":"IE","legacy_key":"legacy"}""",
            InferredSignalsJson = """{"currency":"EUR","legacy_inferred":"value"}""",
            SignalMetadataJson = """{"country":{"source":"explicit_user","strength":"explicit","isExplicit":true,"updatedAtUtc":"2026-04-18T00:00:00Z"},"legacy_inferred":{"source":"system_default","strength":"weak","isExplicit":false,"updatedAtUtc":"2026-04-18T00:00:00Z"}}""",
            FreshnessState = "fresh",
            ProfileSchemaVersion = 1,
            CreatedUtc = nowUtc.AddDays(-2),
            UpdatedUtc = nowUtc.AddDays(-1),
            LastRefreshedUtc = nowUtc.AddDays(-1)
        };

        var state = mapper.ToState(profile, nowUtc);
        var persisted = new UserFinancialContextProfile
        {
            UserId = profile.UserId
        };
        mapper.ApplyToEntity(persisted, state);

        Assert.Contains("\"legacy_key\":\"legacy\"", persisted.ExplicitSignalsJson, StringComparison.Ordinal);
        Assert.Contains("\"legacy_inferred\":\"value\"", persisted.InferredSignalsJson, StringComparison.Ordinal);
        Assert.Contains("\"legacy_inferred\":", persisted.SignalMetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataPolicy_RepairsExplicitInferredMismatch_AndDropsOrphanMetadata()
    {
        var policy = new UserFinancialProfileSignalMetadataPolicy();
        var nowUtc = DateTime.UtcNow;
        var state = new UserFinancialContextProfileData(isNewProfile: false);
        state.ExplicitSignals[UserFinancialProfileSignalKey.Currency] = new UserFinancialProfileSignal(
            "EUR",
            new UserFinancialProfileSignalMetadata(
                Source: UserFinancialProfileSignalSource.InferredFromSummary,
                Strength: UserFinancialProfileSignalStrength.Weak,
                IsExplicit: false,
                UpdatedAtUtc: default));
        state.InferredSignals[UserFinancialProfileSignalKey.SpendingTendencies] = new UserFinancialProfileSignal(
            """{"averageDailySpend":20}""",
            new UserFinancialProfileSignalMetadata(
                Source: UserFinancialProfileSignalSource.ExplicitUser,
                Strength: UserFinancialProfileSignalStrength.Explicit,
                IsExplicit: true,
                UpdatedAtUtc: default));
        state.UnmappedMetadata["orphan_meta"] = new UserFinancialProfileSignalMetadata(
            Source: UserFinancialProfileSignalSource.SystemDefault,
            Strength: UserFinancialProfileSignalStrength.Weak,
            IsExplicit: false,
            UpdatedAtUtc: nowUtc);

        policy.EnsureSignalMetadataCoherence(state, nowUtc);

        Assert.True(state.ExplicitSignals[UserFinancialProfileSignalKey.Currency].Metadata.IsExplicit);
        Assert.Equal(
            UserFinancialProfileSignalStrength.Explicit,
            state.ExplicitSignals[UserFinancialProfileSignalKey.Currency].Metadata.Strength);
        Assert.False(state.InferredSignals[UserFinancialProfileSignalKey.SpendingTendencies].Metadata.IsExplicit);
        Assert.Equal(
            UserFinancialProfileSignalStrength.Acceptable,
            state.InferredSignals[UserFinancialProfileSignalKey.SpendingTendencies].Metadata.Strength);
        Assert.DoesNotContain("orphan_meta", state.UnmappedMetadata.Keys);
    }

    [Fact]
    public void MergePolicy_RespectsExplicitPrecedence_AndBlocksWeakDegradation()
    {
        var mergePolicy = new UserFinancialProfileMergePolicy();
        var persistencePolicy = new UserFinancialProfileInferencePersistencePolicy();
        var nowUtc = DateTime.UtcNow;
        var user = new User
        {
            PreferredCurrency = "GBP",
            CountryRegion = "GB",
            Preferences = new UserPreference
            {
                AdviceTonePreference = "conservative",
                EssentialCategoryPreferencesJson = "{}"
            }
        };
        var state = new UserFinancialContextProfileData(isNewProfile: false);
        state.InferredSignals[UserFinancialProfileSignalKey.Currency] = new UserFinancialProfileSignal(
            "USD",
            new UserFinancialProfileSignalMetadata(
                UserFinancialProfileSignalSource.InferredFromSummary,
                UserFinancialProfileSignalStrength.Strong,
                IsExplicit: false,
                UpdatedAtUtc: nowUtc.AddDays(-2)));
        state.InferredSignals[UserFinancialProfileSignalKey.SpendingTendencies] = new UserFinancialProfileSignal(
            """{"averageDailySpend":22}""",
            new UserFinancialProfileSignalMetadata(
                UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                UserFinancialProfileSignalStrength.Strong,
                IsExplicit: false,
                UpdatedAtUtc: nowUtc.AddDays(-2)));

        mergePolicy.ApplyExplicitSignals(user, state, nowUtc);
        mergePolicy.ApplyInferredSignals(
            state,
            [
                new UserFinancialProfileInferredSignalCandidate(
                    UserFinancialProfileSignalKey.Currency,
                    "EUR",
                    UserFinancialProfileSignalSource.InferredFromSummary,
                    UserFinancialProfileSignalStrength.Strong),
                new UserFinancialProfileInferredSignalCandidate(
                    UserFinancialProfileSignalKey.SpendingTendencies,
                    """{"averageDailySpend":2}""",
                    UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                    UserFinancialProfileSignalStrength.Weak)
            ],
            persistencePolicy,
            nowUtc);

        Assert.Equal("GBP", state.ExplicitSignals[UserFinancialProfileSignalKey.Currency].Value);
        Assert.Equal(
            """{"averageDailySpend":22}""",
            state.InferredSignals[UserFinancialProfileSignalKey.SpendingTendencies].Value);
    }

    [Fact]
    public void InferencePersistencePolicy_UsesModerateOrStrongThreshold()
    {
        var policy = new UserFinancialProfileInferencePersistencePolicy();

        var weakCandidate = new UserFinancialProfileInferredSignalCandidate(
            UserFinancialProfileSignalKey.BudgetStructure,
            "{}",
            UserFinancialProfileSignalSource.InferredFromBudget,
            UserFinancialProfileSignalStrength.Weak);
        var acceptableCandidate = weakCandidate with
        {
            Strength = UserFinancialProfileSignalStrength.Acceptable
        };

        Assert.False(policy.CanPersist(weakCandidate));
        Assert.True(policy.CanPersist(acceptableCandidate));
    }

    [Fact]
    public void InvariantValidator_RepairsLifecycleTimestampOrdering()
    {
        var validator = new UserFinancialProfileLifecycleInvariantValidator();
        var nowUtc = DateTime.UtcNow;
        var state = new UserFinancialContextProfileData(isNewProfile: false)
        {
            Lifecycle = new UserFinancialProfileLifecycleMetadata(
                FreshnessState: UserFinancialProfileFreshnessState.Stale,
                SchemaVersion: 0,
                CreatedUtc: nowUtc,
                UpdatedUtc: nowUtc.AddDays(-2),
                LastRefreshedUtc: nowUtc.AddDays(-5))
        };

        validator.EnsureValid(state, nowUtc);

        Assert.Equal(1, state.Lifecycle.SchemaVersion);
        Assert.True(state.Lifecycle.UpdatedUtc >= state.Lifecycle.CreatedUtc);
        Assert.True(state.Lifecycle.LastRefreshedUtc >= state.Lifecycle.CreatedUtc);
    }
}
