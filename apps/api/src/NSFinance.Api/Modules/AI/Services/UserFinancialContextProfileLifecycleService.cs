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
    IUserFinancialProfileSerializationMapper mapper,
    IUserFinancialProfileMergePolicy mergePolicy,
    IUserFinancialProfileInferenceBuilder inferenceBuilder,
    IUserFinancialProfileInferencePersistencePolicy persistencePolicy,
    IUserFinancialProfileSignalMetadataPolicy metadataPolicy,
    IUserFinancialProfileLifecycleInvariantValidator invariantValidator,
    IUserFinancialProfileFreshnessEvaluator freshnessEvaluator,
    IOptions<CompanionProfileLifecycleOptions> lifecycleOptions,
    ILogger<UserFinancialContextProfileService> logger) : IUserFinancialContextProfileService
{
    private readonly CompanionProfileLifecycleOptions _lifecycleOptions = lifecycleOptions.Value;

    public async Task<UserFinancialContextSnapshot> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.Preferences)
            .SingleAsync(x => x.Id == userId, cancellationToken);

        var existingProfile = await dbContext.UserFinancialContextProfiles
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var isNewProfile = existingProfile is null;

        var state = mapper.ToState(existingProfile, nowUtc);
        var beforeFingerprint = mapper.BuildFingerprint(state);

        mergePolicy.ApplyExplicitSignals(user, state, nowUtc);
        metadataPolicy.EnsureSignalMetadataCoherence(state, nowUtc);

        var computedFreshness = freshnessEvaluator.Evaluate(nowUtc, state.Lifecycle.LastRefreshedUtc);
        state.Lifecycle = state.Lifecycle with
        {
            FreshnessState = computedFreshness
        };

        var schemaVersion = Math.Max(1, _lifecycleOptions.ProfileSchemaVersion);
        var shouldRefreshInference = isNewProfile
                                    || state.Lifecycle.SchemaVersion < schemaVersion
                                    || state.Lifecycle.FreshnessState == UserFinancialProfileFreshnessState.RefreshNeeded
                                    || (_lifecycleOptions.RefreshWhenStale
                                        && state.Lifecycle.FreshnessState == UserFinancialProfileFreshnessState.Stale);
        if (shouldRefreshInference)
        {
            var candidates = await inferenceBuilder.BuildCandidatesAsync(userId, state, cancellationToken);
            mergePolicy.ApplyInferredSignals(state, candidates, persistencePolicy, nowUtc);
            state.Lifecycle = state.Lifecycle with
            {
                FreshnessState = UserFinancialProfileFreshnessState.Fresh,
                LastRefreshedUtc = nowUtc
            };
        }

        if (state.Lifecycle.LastRefreshedUtc == default)
        {
            state.Lifecycle = state.Lifecycle with
            {
                LastRefreshedUtc = nowUtc
            };
        }

        if (state.Lifecycle.SchemaVersion != schemaVersion)
        {
            state.Lifecycle = state.Lifecycle with
            {
                SchemaVersion = schemaVersion
            };
        }

        metadataPolicy.EnsureSignalMetadataCoherence(state, nowUtc);
        mergePolicy.ResolveCanonicalValues(state, nowUtc);
        invariantValidator.EnsureValid(state, nowUtc);

        var afterFingerprint = mapper.BuildFingerprint(state);
        if (isNewProfile || !string.Equals(beforeFingerprint, afterFingerprint, StringComparison.Ordinal))
        {
            var profileEntity = existingProfile ?? new UserFinancialContextProfile
            {
                UserId = userId
            };

            state.Lifecycle = state.Lifecycle with
            {
                UpdatedUtc = nowUtc
            };

            mapper.ApplyToEntity(profileEntity, state);
            if (isNewProfile)
            {
                dbContext.UserFinancialContextProfiles.Add(profileEntity);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "[AI_PROFILE_LIFECYCLE] userId={UserId} isNew={IsNew} refreshed={Refreshed} freshness={Freshness} schemaVersion={SchemaVersion}",
                userId,
                isNewProfile,
                shouldRefreshInference,
                state.Lifecycle.FreshnessState,
                state.Lifecycle.SchemaVersion);
        }

        return mapper.ToSnapshot(state);
    }
}
