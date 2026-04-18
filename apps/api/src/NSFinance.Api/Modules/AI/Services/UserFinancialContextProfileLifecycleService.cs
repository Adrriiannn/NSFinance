using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

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

public interface IUserFinancialProfileFreshnessEvaluator
{
    UserFinancialProfileFreshnessState Evaluate(DateTime nowUtc, DateTime lastRefreshedUtc);
}

public sealed class UserFinancialProfileFreshnessEvaluator(
    IOptions<CompanionProfileLifecycleOptions> options) : IUserFinancialProfileFreshnessEvaluator
{
    private readonly CompanionProfileLifecycleOptions _options = options.Value;

    public UserFinancialProfileFreshnessState Evaluate(DateTime nowUtc, DateTime lastRefreshedUtc)
    {
        if (lastRefreshedUtc == default)
        {
            return UserFinancialProfileFreshnessState.RefreshNeeded;
        }

        var age = nowUtc - lastRefreshedUtc;
        if (age.TotalHours >= Math.Max(_options.StaleAfterHours + 1, _options.RefreshNeededAfterHours))
        {
            return UserFinancialProfileFreshnessState.RefreshNeeded;
        }

        return age.TotalHours >= Math.Max(1, _options.StaleAfterHours)
            ? UserFinancialProfileFreshnessState.Stale
            : UserFinancialProfileFreshnessState.Fresh;
    }
}

public sealed class UserFinancialContextProfileService(
    AppDbContext dbContext,
    IUserFinancialSummaryService summaryService,
    IRecurringObligationsService recurringObligationsService,
    IBudgetStatusService budgetStatusService,
    ISpendingAnalysisService spendingAnalysisService,
    IUserFinancialProfileFreshnessEvaluator freshnessEvaluator,
    IOptions<CompanionProfileLifecycleOptions> lifecycleOptions,
    ILogger<UserFinancialContextProfileService> logger) : IUserFinancialContextProfileService
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions MetadataSerializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly CompanionProfileLifecycleOptions _lifecycleOptions = lifecycleOptions.Value;

    public async Task<UserFinancialContextSnapshot> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.Preferences)
            .SingleAsync(x => x.Id == userId, cancellationToken);

        var profile = await dbContext.UserFinancialContextProfiles
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var isNewProfile = profile is null;
        profile ??= CreateProfileShell(userId, nowUtc);

        var explicitSignals = DeserializeSignalMap(profile.ExplicitSignalsJson);
        var inferredSignals = DeserializeSignalMap(profile.InferredSignalsJson);
        var metadata = DeserializeMetadataMap(profile.SignalMetadataJson);

        var changed = false;
        changed |= ApplyExplicitSignals(user, explicitSignals, metadata, nowUtc);

        var previousFreshness = ParseFreshnessState(profile.FreshnessState);
        var lastRefreshedUtc = profile.LastRefreshedUtc == default
            ? profile.UpdatedUtc
            : profile.LastRefreshedUtc;
        var freshness = freshnessEvaluator.Evaluate(nowUtc, lastRefreshedUtc);
        if (previousFreshness != freshness)
        {
            profile.FreshnessState = ToFreshnessStorageValue(freshness);
            changed = true;
        }

        var needsSchemaRefresh = profile.ProfileSchemaVersion < Math.Max(1, _lifecycleOptions.ProfileSchemaVersion);
        var shouldRefreshInference = isNewProfile
            || needsSchemaRefresh
            || freshness == UserFinancialProfileFreshnessState.RefreshNeeded
            || (_lifecycleOptions.RefreshWhenStale && freshness == UserFinancialProfileFreshnessState.Stale);

        if (shouldRefreshInference)
        {
            var inferenceCandidates = await BuildInferenceCandidatesAsync(userId, explicitSignals, cancellationToken);
            changed |= ApplyInferredSignals(inferredSignals, metadata, inferenceCandidates, nowUtc);
            profile.LastRefreshedUtc = nowUtc;
            profile.FreshnessState = ToFreshnessStorageValue(UserFinancialProfileFreshnessState.Fresh);
            changed = true;
        }
        else if (profile.LastRefreshedUtc == default)
        {
            profile.LastRefreshedUtc = nowUtc;
            changed = true;
        }

        changed |= ResolveFinalProfileValues(profile, explicitSignals, inferredSignals, metadata, nowUtc);

        var explicitSerialized = SerializeSignalMap(explicitSignals);
        if (!string.Equals(profile.ExplicitSignalsJson, explicitSerialized, StringComparison.Ordinal))
        {
            profile.ExplicitSignalsJson = explicitSerialized;
            changed = true;
        }

        var inferredSerialized = SerializeSignalMap(inferredSignals);
        if (!string.Equals(profile.InferredSignalsJson, inferredSerialized, StringComparison.Ordinal))
        {
            profile.InferredSignalsJson = inferredSerialized;
            changed = true;
        }

        var metadataSerialized = SerializeMetadataMap(metadata);
        if (!string.Equals(profile.SignalMetadataJson, metadataSerialized, StringComparison.Ordinal))
        {
            profile.SignalMetadataJson = metadataSerialized;
            changed = true;
        }

        var schemaVersion = Math.Max(1, _lifecycleOptions.ProfileSchemaVersion);
        if (profile.ProfileSchemaVersion != schemaVersion)
        {
            profile.ProfileSchemaVersion = schemaVersion;
            changed = true;
        }

        if (isNewProfile)
        {
            dbContext.UserFinancialContextProfiles.Add(profile);
            changed = true;
        }

        if (changed)
        {
            profile.UpdatedUtc = nowUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "[AI_PROFILE_LIFECYCLE] userId={UserId} isNew={IsNew} refreshed={Refreshed} freshness={Freshness} schemaVersion={SchemaVersion}",
                userId,
                isNewProfile,
                shouldRefreshInference,
                profile.FreshnessState,
                profile.ProfileSchemaVersion);
        }

        return new UserFinancialContextSnapshot(
            profile.Country,
            profile.Currency,
            profile.MonthlyIncomeRange,
            profile.KnownObligationsJson,
            profile.BudgetStructureJson,
            profile.ActivePlansJson,
            profile.SpendingTendenciesJson,
            profile.CategoryFlexibilityMarkersJson,
            profile.AdviceStylePreference);
    }

    private static UserFinancialContextProfile CreateProfileShell(Guid userId, DateTime nowUtc)
    {
        return new UserFinancialContextProfile
        {
            UserId = userId,
            Country = "ZZ",
            Currency = "EUR",
            MonthlyIncomeRange = null,
            KnownObligationsJson = "[]",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = "[]",
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "balanced",
            ExplicitSignalsJson = "{}",
            InferredSignalsJson = "{}",
            SignalMetadataJson = "{}",
            FreshnessState = ToFreshnessStorageValue(UserFinancialProfileFreshnessState.RefreshNeeded),
            ProfileSchemaVersion = 1,
            LastRefreshedUtc = nowUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };
    }

    private bool ApplyExplicitSignals(
        User user,
        IDictionary<string, string> explicitSignals,
        IDictionary<string, UserFinancialProfileSignalMetadata> metadata,
        DateTime nowUtc)
    {
        var changed = false;

        changed |= UpsertExplicitSignal(
            explicitSignals,
            metadata,
            ProfileSignalKeys.Country,
            NormalizeCountry(user.CountryRegion),
            nowUtc);
        changed |= UpsertExplicitSignal(
            explicitSignals,
            metadata,
            ProfileSignalKeys.Currency,
            NormalizeCurrency(user.PreferredCurrency),
            nowUtc);
        changed |= UpsertExplicitSignal(
            explicitSignals,
            metadata,
            ProfileSignalKeys.AdviceStylePreference,
            NormalizeAdviceStyle(user.Preferences?.AdviceTonePreference),
            nowUtc);

        var categoryMarkers = NormalizeJsonOrDefault(
            user.Preferences?.EssentialCategoryPreferencesJson,
            "{}");
        if (!string.Equals(categoryMarkers, "{}", StringComparison.Ordinal))
        {
            changed |= UpsertExplicitSignal(
                explicitSignals,
                metadata,
                ProfileSignalKeys.CategoryFlexibilityMarkers,
                categoryMarkers,
                nowUtc);
        }
        else
        {
            changed |= RemoveExplicitSignal(explicitSignals, metadata, ProfileSignalKeys.CategoryFlexibilityMarkers, nowUtc);
        }

        return changed;
    }

    private async Task<IReadOnlyList<InferredSignalCandidate>> BuildInferenceCandidatesAsync(
        Guid userId,
        IReadOnlyDictionary<string, string> explicitSignals,
        CancellationToken cancellationToken)
    {
        var candidates = new List<InferredSignalCandidate>(8);

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
                Math.Clamp(_lifecycleOptions.SpendingAnalysisLookbackDays, 14, 180),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Spending analysis unavailable for profile inference. userId={UserId}", userId);
        }

        if (summary is not null)
        {
            var incomeRange = DeriveIncomeRange(summary.IncomeLast30Days);
            if (!string.IsNullOrWhiteSpace(incomeRange))
            {
                candidates.Add(new InferredSignalCandidate(
                    ProfileSignalKeys.MonthlyIncomeRange,
                    incomeRange,
                    UserFinancialProfileSignalSource.InferredFromSummary,
                    summary.IncomeLast30Days >= 4_000m
                        ? UserFinancialProfileSignalStrength.Strong
                        : UserFinancialProfileSignalStrength.Acceptable));
            }

            if (!explicitSignals.ContainsKey(ProfileSignalKeys.Currency))
            {
                var inferredCurrency = NormalizeCurrency(summary.Currency);
                if (!string.IsNullOrWhiteSpace(inferredCurrency))
                {
                    candidates.Add(new InferredSignalCandidate(
                        ProfileSignalKeys.Currency,
                        inferredCurrency,
                        UserFinancialProfileSignalSource.InferredFromSummary,
                        UserFinancialProfileSignalStrength.Acceptable));
                }
            }
        }

        if (recurring is not null && recurring.Items.Count > 0)
        {
            var recurringItems = recurring.Items
                .Take(Math.Clamp(_lifecycleOptions.MaxRecurringObligations, 1, 32))
                .ToArray();
            var recurringStrength = recurring.Items.Count >= 2
                                    || recurring.EstimatedMonthlyTotal >= _lifecycleOptions.StrongRecurringMonthlyTotalThreshold
                ? UserFinancialProfileSignalStrength.Strong
                : UserFinancialProfileSignalStrength.Acceptable;
            candidates.Add(new InferredSignalCandidate(
                ProfileSignalKeys.KnownObligations,
                JsonSerializer.Serialize(recurringItems, SerializationOptions),
                UserFinancialProfileSignalSource.InferredFromRecurringObligations,
                recurringStrength));
        }

        if (budget is not null && (budget.HasBudgetPlan || budget.MonthToDateSpend > 0m))
        {
            var budgetStrength = budget.HasBudgetPlan || budget.MonthToDateSpend >= _lifecycleOptions.StrongMonthToDateSpendThreshold
                ? UserFinancialProfileSignalStrength.Strong
                : UserFinancialProfileSignalStrength.Acceptable;
            candidates.Add(new InferredSignalCandidate(
                ProfileSignalKeys.BudgetStructure,
                JsonSerializer.Serialize(new
                {
                    budget.HasBudgetPlan,
                    budget.MonthlyBudget,
                    budget.MonthToDateSpend,
                    budget.RemainingBudget
                }, SerializationOptions),
                UserFinancialProfileSignalSource.InferredFromBudget,
                budgetStrength));
        }

        var activePlans = await dbContext.ExpensePlans
            .AsNoTracking()
            .Where(x => x.UserId == userId && (x.Status == "active" || x.ActivatedAtUtc.HasValue))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(_lifecycleOptions.MaxActivePlans, 1, 20))
            .Select(x => new { x.Id, x.Title, x.Status, x.ExpectedSpendTotal, x.CurrencyCode })
            .ToListAsync(cancellationToken);
        if (activePlans.Count > 0)
        {
            candidates.Add(new InferredSignalCandidate(
                ProfileSignalKeys.ActivePlans,
                JsonSerializer.Serialize(activePlans, SerializationOptions),
                UserFinancialProfileSignalSource.InferredFromPlanData,
                UserFinancialProfileSignalStrength.Strong));
        }

        if (spending is not null && spending.SpendByDomain.Count > 0 && spending.AverageDailySpend > 0m)
        {
            var nonZeroDomainCount = spending.SpendByDomain.Count(x => x.Value > 0m);
            var spendingStrength = nonZeroDomainCount >= Math.Max(1, _lifecycleOptions.StrongSpendingDomainCountThreshold)
                                   && spending.AverageDailySpend >= _lifecycleOptions.StrongAverageDailySpendThreshold
                                   && spending.LargestExpense >= _lifecycleOptions.StrongLargestExpenseThreshold
                ? UserFinancialProfileSignalStrength.Strong
                : UserFinancialProfileSignalStrength.Acceptable;
            candidates.Add(new InferredSignalCandidate(
                ProfileSignalKeys.SpendingTendencies,
                JsonSerializer.Serialize(new
                {
                    spending.AverageDailySpend,
                    spending.LargestExpense,
                    spendByDomain = spending.SpendByDomain
                }, SerializationOptions),
                UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                spendingStrength));
        }

        return candidates;
    }

    private static bool ApplyInferredSignals(
        IDictionary<string, string> inferredSignals,
        IDictionary<string, UserFinancialProfileSignalMetadata> metadata,
        IReadOnlyList<InferredSignalCandidate> candidates,
        DateTime nowUtc)
    {
        var changed = false;

        foreach (var candidate in candidates)
        {
            if (candidate.Strength < UserFinancialProfileSignalStrength.Acceptable)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Value))
            {
                continue;
            }

            if (metadata.TryGetValue(candidate.Key, out var existingMetadata)
                && existingMetadata.IsExplicit)
            {
                continue;
            }

            var existingStrength = metadata.TryGetValue(candidate.Key, out existingMetadata)
                ? existingMetadata.Strength
                : UserFinancialProfileSignalStrength.Weak;
            if (candidate.Strength < existingStrength)
            {
                continue;
            }

            if (!inferredSignals.TryGetValue(candidate.Key, out var existingValue)
                || !string.Equals(existingValue, candidate.Value, StringComparison.Ordinal))
            {
                inferredSignals[candidate.Key] = candidate.Value;
                changed = true;
            }

            var newMetadata = new UserFinancialProfileSignalMetadata(
                candidate.Source,
                candidate.Strength,
                IsExplicit: false,
                UpdatedAtUtc: nowUtc);
            if (!metadata.TryGetValue(candidate.Key, out var currentMetadata)
                || currentMetadata != newMetadata)
            {
                metadata[candidate.Key] = newMetadata;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ResolveFinalProfileValues(
        UserFinancialContextProfile profile,
        IReadOnlyDictionary<string, string> explicitSignals,
        IReadOnlyDictionary<string, string> inferredSignals,
        IDictionary<string, UserFinancialProfileSignalMetadata> metadata,
        DateTime nowUtc)
    {
        var changed = false;

        var country = ResolveRequired(
            ProfileSignalKeys.Country,
            explicitSignals,
            inferredSignals,
            fallback: "ZZ",
            metadata,
            nowUtc);
        if (!string.Equals(profile.Country, country, StringComparison.Ordinal))
        {
            profile.Country = country;
            changed = true;
        }

        var currency = ResolveRequired(
            ProfileSignalKeys.Currency,
            explicitSignals,
            inferredSignals,
            fallback: "EUR",
            metadata,
            nowUtc);
        if (!string.Equals(profile.Currency, currency, StringComparison.Ordinal))
        {
            profile.Currency = currency;
            changed = true;
        }

        var monthlyIncomeRange = ResolveOptional(
            ProfileSignalKeys.MonthlyIncomeRange,
            explicitSignals,
            inferredSignals,
            existingValue: profile.MonthlyIncomeRange);
        if (!string.Equals(profile.MonthlyIncomeRange, monthlyIncomeRange, StringComparison.Ordinal))
        {
            profile.MonthlyIncomeRange = monthlyIncomeRange;
            changed = true;
        }

        var knownObligations = ResolveRequired(
            ProfileSignalKeys.KnownObligations,
            explicitSignals,
            inferredSignals,
            fallback: "[]",
            metadata,
            nowUtc);
        if (!string.Equals(profile.KnownObligationsJson, knownObligations, StringComparison.Ordinal))
        {
            profile.KnownObligationsJson = knownObligations;
            changed = true;
        }

        var budgetStructure = ResolveRequired(
            ProfileSignalKeys.BudgetStructure,
            explicitSignals,
            inferredSignals,
            fallback: "{}",
            metadata,
            nowUtc);
        if (!string.Equals(profile.BudgetStructureJson, budgetStructure, StringComparison.Ordinal))
        {
            profile.BudgetStructureJson = budgetStructure;
            changed = true;
        }

        var activePlans = ResolveRequired(
            ProfileSignalKeys.ActivePlans,
            explicitSignals,
            inferredSignals,
            fallback: "[]",
            metadata,
            nowUtc);
        if (!string.Equals(profile.ActivePlansJson, activePlans, StringComparison.Ordinal))
        {
            profile.ActivePlansJson = activePlans;
            changed = true;
        }

        var spendingTendencies = ResolveRequired(
            ProfileSignalKeys.SpendingTendencies,
            explicitSignals,
            inferredSignals,
            fallback: "[]",
            metadata,
            nowUtc);
        if (!string.Equals(profile.SpendingTendenciesJson, spendingTendencies, StringComparison.Ordinal))
        {
            profile.SpendingTendenciesJson = spendingTendencies;
            changed = true;
        }

        var categoryFlexibilityMarkers = ResolveRequired(
            ProfileSignalKeys.CategoryFlexibilityMarkers,
            explicitSignals,
            inferredSignals,
            fallback: "[]",
            metadata,
            nowUtc);
        if (!string.Equals(profile.CategoryFlexibilityMarkersJson, categoryFlexibilityMarkers, StringComparison.Ordinal))
        {
            profile.CategoryFlexibilityMarkersJson = categoryFlexibilityMarkers;
            changed = true;
        }

        var adviceStyle = ResolveRequired(
            ProfileSignalKeys.AdviceStylePreference,
            explicitSignals,
            inferredSignals,
            fallback: "balanced",
            metadata,
            nowUtc);
        if (!string.Equals(profile.AdviceStylePreference, adviceStyle, StringComparison.Ordinal))
        {
            profile.AdviceStylePreference = adviceStyle;
            changed = true;
        }

        return changed;
    }

    private static string ResolveRequired(
        string key,
        IReadOnlyDictionary<string, string> explicitSignals,
        IReadOnlyDictionary<string, string> inferredSignals,
        string fallback,
        IDictionary<string, UserFinancialProfileSignalMetadata> metadata,
        DateTime nowUtc)
    {
        if (explicitSignals.TryGetValue(key, out var explicitValue) && !string.IsNullOrWhiteSpace(explicitValue))
        {
            if (!metadata.TryGetValue(key, out var explicitMetadata)
                || !explicitMetadata.IsExplicit
                || explicitMetadata.Source != UserFinancialProfileSignalSource.ExplicitUser
                || explicitMetadata.Strength != UserFinancialProfileSignalStrength.Explicit)
            {
                metadata[key] = new UserFinancialProfileSignalMetadata(
                    UserFinancialProfileSignalSource.ExplicitUser,
                    UserFinancialProfileSignalStrength.Explicit,
                    IsExplicit: true,
                    UpdatedAtUtc: nowUtc);
            }

            return explicitValue;
        }

        if (inferredSignals.TryGetValue(key, out var inferredValue) && !string.IsNullOrWhiteSpace(inferredValue))
        {
            if (!metadata.ContainsKey(key))
            {
                metadata[key] = new UserFinancialProfileSignalMetadata(
                    UserFinancialProfileSignalSource.InferredFromSummary,
                    UserFinancialProfileSignalStrength.Acceptable,
                    IsExplicit: false,
                    UpdatedAtUtc: nowUtc);
            }

            return inferredValue;
        }

        if (!metadata.ContainsKey(key))
        {
            metadata[key] = new UserFinancialProfileSignalMetadata(
                UserFinancialProfileSignalSource.SystemDefault,
                UserFinancialProfileSignalStrength.Weak,
                IsExplicit: false,
                UpdatedAtUtc: nowUtc);
        }

        return fallback;
    }

    private static string? ResolveOptional(
        string key,
        IReadOnlyDictionary<string, string> explicitSignals,
        IReadOnlyDictionary<string, string> inferredSignals,
        string? existingValue)
    {
        if (explicitSignals.TryGetValue(key, out var explicitValue) && !string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        if (inferredSignals.TryGetValue(key, out var inferredValue) && !string.IsNullOrWhiteSpace(inferredValue))
        {
            return inferredValue;
        }

        return existingValue;
    }

    private static bool UpsertExplicitSignal(
        IDictionary<string, string> explicitSignals,
        IDictionary<string, UserFinancialProfileSignalMetadata> metadata,
        string key,
        string value,
        DateTime nowUtc)
    {
        var changed = false;

        var valueChanged = !explicitSignals.TryGetValue(key, out var existingValue)
                           || !string.Equals(existingValue, value, StringComparison.Ordinal);
        if (valueChanged)
        {
            explicitSignals[key] = value;
            changed = true;
        }

        if (!metadata.TryGetValue(key, out var existingMetadata)
            || !existingMetadata.IsExplicit
            || existingMetadata.Source != UserFinancialProfileSignalSource.ExplicitUser
            || existingMetadata.Strength != UserFinancialProfileSignalStrength.Explicit
            || valueChanged)
        {
            metadata[key] = new UserFinancialProfileSignalMetadata(
                UserFinancialProfileSignalSource.ExplicitUser,
                UserFinancialProfileSignalStrength.Explicit,
                IsExplicit: true,
                UpdatedAtUtc: nowUtc);
            changed = true;
        }

        return changed;
    }

    private static bool RemoveExplicitSignal(
        IDictionary<string, string> explicitSignals,
        IDictionary<string, UserFinancialProfileSignalMetadata> metadata,
        string key,
        DateTime nowUtc)
    {
        var changed = false;
        if (explicitSignals.Remove(key))
        {
            changed = true;
        }

        if (metadata.TryGetValue(key, out var signalMetadata)
            && signalMetadata.IsExplicit)
        {
            metadata[key] = new UserFinancialProfileSignalMetadata(
                UserFinancialProfileSignalSource.SystemDefault,
                UserFinancialProfileSignalStrength.Weak,
                IsExplicit: false,
                UpdatedAtUtc: nowUtc);
            changed = true;
        }

        return changed;
    }

    private static Dictionary<string, string> DeserializeSignalMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializationOptions) ?? [];
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
            return JsonSerializer.Deserialize<Dictionary<string, UserFinancialProfileSignalMetadata>>(json, MetadataSerializationOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string SerializeSignalMap(IReadOnlyDictionary<string, string> map)
    {
        var ordered = map
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered, SerializationOptions);
    }

    private static string SerializeMetadataMap(IReadOnlyDictionary<string, UserFinancialProfileSignalMetadata> map)
    {
        var ordered = map
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered, MetadataSerializationOptions);
    }

    private static string NormalizeCountry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "ZZ";
        }

        return raw.Trim().ToUpperInvariant();
    }

    private static string NormalizeCurrency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "EUR";
        }

        var cleaned = raw.Trim().ToUpperInvariant();
        return cleaned.Length == 3 ? cleaned : "EUR";
    }

    private static string NormalizeAdviceStyle(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "conservative" => "conservative",
            "flexible" => "flexible",
            _ => "balanced"
        };
    }

    private static string NormalizeJsonOrDefault(string? raw, string fallback)
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

    private static string? DeriveIncomeRange(decimal incomeLast30Days)
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
            "refreshneeded" => UserFinancialProfileFreshnessState.RefreshNeeded,
            "refresh_needed" => UserFinancialProfileFreshnessState.RefreshNeeded,
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

    private static class ProfileSignalKeys
    {
        public const string Country = "country";
        public const string Currency = "currency";
        public const string MonthlyIncomeRange = "monthly_income_range";
        public const string KnownObligations = "known_obligations";
        public const string BudgetStructure = "budget_structure";
        public const string ActivePlans = "active_plans";
        public const string SpendingTendencies = "spending_tendencies";
        public const string CategoryFlexibilityMarkers = "category_flexibility_markers";
        public const string AdviceStylePreference = "advice_style_preference";
    }

    private sealed record InferredSignalCandidate(
        string Key,
        string Value,
        UserFinancialProfileSignalSource Source,
        UserFinancialProfileSignalStrength Strength);
}
