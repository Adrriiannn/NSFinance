using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IAITriggerGateService
{
    Task<AITriggerGateDecision> EvaluateAsync(
        AITriggerGateInput input,
        CancellationToken cancellationToken);
}

public sealed record AITriggerGateInput(
    MerchantResolutionRequest Request,
    string MerchantKey,
    string NormalizedDescriptor,
    DomainTriggerPolicyEvaluation PolicyEvaluation,
    bool DeterministicResolved,
    bool RegistryResolved,
    bool DescriptorMerchantLike,
    DateTime? MerchantInvestigatedAtUtc,
    DateTime? MerchantCooldownUntilUtc,
    DateTime? UnresolvedCooldownUntilUtc,
    int MerchantOccurrenceCount);

public sealed record AITriggerGateDecision(
    bool ShouldTriggerAI,
    AITriggerSkipReason? SkipReason,
    string BudgetState,
    string CooldownState,
    bool SuggestionOnly,
    int UserDailyAICallCount);

public sealed class AITriggerGateService(
    AppDbContext dbContext,
    IOptions<MerchantAIGovernanceOptions> governanceOptions,
    IOptions<AIIntegrationOptions> aiIntegrationOptions) : IAITriggerGateService
{
    private static readonly string[] FutureReuseIndicators =
    [
        "subscription",
        "membership",
        "insurance",
        "utility",
        "utilities",
        "rent",
        "mortgage",
        "bill",
        "telecom",
        "broadband",
        "pharmacy"
    ];

    private readonly MerchantAIGovernanceOptions _governance = governanceOptions.Value;
    private readonly AIIntegrationOptions _aiIntegration = aiIntegrationOptions.Value;

    public async Task<AITriggerGateDecision> EvaluateAsync(
        AITriggerGateInput input,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var runState = input.Request.RunState;
        var userDailyAICallCount = 0;

        if (input.Request.ManualOverridePresent)
        {
            return Reject(AITriggerSkipReason.ManualOverridePresent, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (input.DeterministicResolved || input.Request.DeterministicTerminal)
        {
            return Reject(AITriggerSkipReason.DeterministicTerminal, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (input.RegistryResolved)
        {
            return Reject(AITriggerSkipReason.RegistryResolved, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (!input.DescriptorMerchantLike)
        {
            return Reject(AITriggerSkipReason.DescriptorNotMerchantLike, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (!_governance.Enabled || !_aiIntegration.Enabled)
        {
            return Reject(AITriggerSkipReason.DomainPolicyDisallowsAI, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (input.PolicyEvaluation.TriggerMode == DomainTriggerMode.D0)
        {
            return Reject(AITriggerSkipReason.DomainPolicyDisallowsAI, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (input.PolicyEvaluation.TriggerMode == DomainTriggerMode.D1 && !_governance.AllowD1AIByDefault)
        {
            return Reject(AITriggerSkipReason.DomainPolicyDisallowsAI, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (runState is not null && !runState.TryMarkMerchantProcessed(input.MerchantKey))
        {
            return Reject(AITriggerSkipReason.DuplicateMerchantInRun, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        var recentlyInvestigated = input.MerchantInvestigatedAtUtc.HasValue
                                   && input.MerchantInvestigatedAtUtc.Value > nowUtc.AddDays(-Math.Max(1, _governance.MerchantInvestigationCooldownDays));
        if (recentlyInvestigated)
        {
            return Reject(AITriggerSkipReason.MerchantRecentlyInvestigated, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        var cooldownUntilUtc = MaxUtc(input.MerchantCooldownUntilUtc, input.UnresolvedCooldownUntilUtc);
        if (cooldownUntilUtc.HasValue && cooldownUntilUtc.Value > nowUtc)
        {
            return Reject(AITriggerSkipReason.MerchantOnCooldown, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        if (runState is not null)
        {
            if (runState.AICallsThisRun >= Math.Max(1, _governance.MaxAICallsPerSyncRun))
            {
                return Reject(AITriggerSkipReason.RunBudgetExceeded, runState, input.Request.ConnectionId, userDailyAICallCount);
            }

            if (input.Request.ConnectionId.HasValue
                && runState.GetAICallsForConnection(input.Request.ConnectionId.Value) >= Math.Max(1, _governance.MaxAICallsPerConnectionPerRun))
            {
                return Reject(AITriggerSkipReason.RunBudgetExceeded, runState, input.Request.ConnectionId, userDailyAICallCount);
            }
        }

        if (input.Request.UserId.HasValue)
        {
            var windowStartUtc = nowUtc.AddHours(-24);
            userDailyAICallCount = await dbContext.MerchantAIDecisionLogs
                .AsNoTracking()
                .Where(x => x.UserId == input.Request.UserId.Value
                            && x.AICallExecuted
                            && x.CreatedUtc >= windowStartUtc)
                .CountAsync(cancellationToken);
            if (userDailyAICallCount >= Math.Max(1, _governance.MaxAICallsPerUserPer24h))
            {
                return Reject(AITriggerSkipReason.DailyBudgetExceeded, runState, input.Request.ConnectionId, userDailyAICallCount);
            }
        }

        var repeatedMerchant = input.MerchantOccurrenceCount >= Math.Max(2, _governance.MinimumOccurrencesForExpectedValue);
        var meaningfulSpend = Math.Abs(input.Request.Amount) >= Math.Abs(_governance.MeaningfulSpendThreshold);
        var likelyFutureReuse = LooksLikelyFutureReuse(input.NormalizedDescriptor);
        if (!repeatedMerchant && !meaningfulSpend && !likelyFutureReuse)
        {
            return Reject(AITriggerSkipReason.ExpectedValueTooLow, runState, input.Request.ConnectionId, userDailyAICallCount);
        }

        var budgetState = BuildBudgetState(runState, input.Request.ConnectionId, userDailyAICallCount);
        var cooldownState = BuildCooldownState(input.MerchantCooldownUntilUtc, input.UnresolvedCooldownUntilUtc);
        return new AITriggerGateDecision(
            ShouldTriggerAI: true,
            SkipReason: null,
            BudgetState: budgetState,
            CooldownState: cooldownState,
            SuggestionOnly: _governance.SuggestionOnlyForD3 && input.PolicyEvaluation.TriggerMode == DomainTriggerMode.D3,
            UserDailyAICallCount: userDailyAICallCount);
    }

    private static AITriggerGateDecision Reject(
        AITriggerSkipReason reason,
        MerchantResolutionRunState? runState,
        Guid? connectionId,
        int userDailyAICallCount)
    {
        return new AITriggerGateDecision(
            ShouldTriggerAI: false,
            SkipReason: reason,
            BudgetState: BuildBudgetState(runState, connectionId, userDailyAICallCount),
            CooldownState: BuildCooldownState(null, null),
            SuggestionOnly: reason == AITriggerSkipReason.UserConfirmationPreferred,
            UserDailyAICallCount: userDailyAICallCount);
    }

    private static string BuildBudgetState(
        MerchantResolutionRunState? runState,
        Guid? connectionId,
        int dailyCount)
    {
        var runCount = runState?.AICallsThisRun ?? 0;
        var connectionCount = connectionId.HasValue && runState is not null
            ? runState.GetAICallsForConnection(connectionId.Value)
            : 0;

        return $"run={runCount};connection={connectionCount};daily24h={dailyCount}";
    }

    private static string BuildCooldownState(DateTime? merchantCooldownUntilUtc, DateTime? unresolvedCooldownUntilUtc)
    {
        return $"merchant={merchantCooldownUntilUtc:O};unresolved={unresolvedCooldownUntilUtc:O}";
    }

    private static DateTime? MaxUtc(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static bool LooksLikelyFutureReuse(string normalizedDescriptor)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescriptor))
        {
            return false;
        }

        return FutureReuseIndicators.Any(token => normalizedDescriptor.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
