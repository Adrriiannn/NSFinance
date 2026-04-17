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
    int MerchantOccurrenceCount,
    double ExpectedValueScore,
    int QueuePosition,
    int QueueDepth,
    string QueueState,
    string BacklogState);

public sealed record AITriggerGateDecision(
    bool ShouldTriggerAI,
    AITriggerSkipReason? SkipReason,
    string BudgetState,
    string CooldownState,
    string QueueState,
    bool SuggestionOnly,
    int UserDailyAICallCount);

public sealed class AITriggerGateService(
    AppDbContext dbContext,
    IOptions<MerchantAIGovernanceOptions> governanceOptions,
    IOptions<AIIntegrationOptions> aiIntegrationOptions) : IAITriggerGateService
{
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
            return Reject(
                AITriggerSkipReason.ManualOverridePresent,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (input.DeterministicResolved || input.Request.DeterministicTerminal)
        {
            return Reject(
                AITriggerSkipReason.DeterministicTerminal,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (input.RegistryResolved)
        {
            return Reject(
                AITriggerSkipReason.RegistryResolved,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (!input.DescriptorMerchantLike)
        {
            return Reject(
                AITriggerSkipReason.DescriptorNotMerchantLike,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (!_governance.Enabled || !_aiIntegration.Enabled)
        {
            return Reject(
                AITriggerSkipReason.DomainPolicyDisallowsAI,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (input.PolicyEvaluation.TriggerMode == DomainTriggerMode.D0)
        {
            return Reject(
                AITriggerSkipReason.DomainPolicyDisallowsAI,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (input.PolicyEvaluation.TriggerMode == DomainTriggerMode.D1 && !_governance.AllowD1AIByDefault)
        {
            return Reject(
                AITriggerSkipReason.DomainPolicyDisallowsAI,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (runState is not null && !runState.TryMarkMerchantProcessed(input.MerchantKey))
        {
            return Reject(
                AITriggerSkipReason.DuplicateMerchantInRun,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        var recentlyInvestigated = input.MerchantInvestigatedAtUtc.HasValue
                                   && input.MerchantInvestigatedAtUtc.Value > nowUtc.AddDays(-Math.Max(1, _governance.MerchantInvestigationCooldownDays));
        if (recentlyInvestigated)
        {
            return Reject(
                AITriggerSkipReason.MerchantRecentlyInvestigated,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        var cooldownUntilUtc = MaxUtc(input.MerchantCooldownUntilUtc, input.UnresolvedCooldownUntilUtc);
        if (cooldownUntilUtc.HasValue && cooldownUntilUtc.Value > nowUtc)
        {
            return Reject(
                AITriggerSkipReason.MerchantOnCooldown,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        if (runState is not null)
        {
            if (runState.AICallsThisRun >= Math.Max(1, _governance.MaxAICallsPerSyncRun))
            {
                return Reject(
                    AITriggerSkipReason.RunBudgetExceeded,
                    runState,
                    input.Request.ConnectionId,
                    userDailyAICallCount,
                    input.MerchantCooldownUntilUtc,
                    input.UnresolvedCooldownUntilUtc,
                    input.QueueState);
            }

            if (input.Request.ConnectionId.HasValue
                && runState.GetAICallsForConnection(input.Request.ConnectionId.Value) >= Math.Max(1, _governance.MaxAICallsPerConnectionPerRun))
            {
                return Reject(
                    AITriggerSkipReason.RunBudgetExceeded,
                    runState,
                    input.Request.ConnectionId,
                    userDailyAICallCount,
                    input.MerchantCooldownUntilUtc,
                    input.UnresolvedCooldownUntilUtc,
                    input.QueueState);
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
                return Reject(
                    AITriggerSkipReason.DailyBudgetExceeded,
                    runState,
                    input.Request.ConnectionId,
                    userDailyAICallCount,
                    input.MerchantCooldownUntilUtc,
                    input.UnresolvedCooldownUntilUtc,
                    input.QueueState);
            }
        }

        var expectedValueSatisfied = input.ExpectedValueScore >= _governance.ExpectedValueThreshold;
        var repeatedMerchant = input.MerchantOccurrenceCount >= Math.Max(2, _governance.MinimumOccurrencesForExpectedValue);
        var meaningfulSpend = Math.Abs(input.Request.Amount) >= Math.Abs(_governance.MeaningfulSpendThreshold);
        if (!input.Request.ForceAIInvestigation && !expectedValueSatisfied && !repeatedMerchant && !meaningfulSpend)
        {
            return Reject(
                AITriggerSkipReason.ExpectedValueTooLow,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                input.QueueState);
        }

        var queueSlice = ResolveQueueSliceLimit(runState, input.Request.ConnectionId);
        if (queueSlice <= 0 || input.QueuePosition > queueSlice)
        {
            return Reject(
                AITriggerSkipReason.RunBudgetExceeded,
                runState,
                input.Request.ConnectionId,
                userDailyAICallCount,
                input.MerchantCooldownUntilUtc,
                input.UnresolvedCooldownUntilUtc,
                $"queueLimit={queueSlice};{input.QueueState}");
        }

        var budgetState = BuildBudgetState(runState, input.Request.ConnectionId, userDailyAICallCount, input.BacklogState);
        var cooldownState = BuildCooldownState(input.MerchantCooldownUntilUtc, input.UnresolvedCooldownUntilUtc);
        return new AITriggerGateDecision(
            ShouldTriggerAI: true,
            SkipReason: null,
            BudgetState: budgetState,
            CooldownState: cooldownState,
            QueueState: input.QueueState,
            SuggestionOnly: _governance.SuggestionOnlyForD3 && input.PolicyEvaluation.TriggerMode == DomainTriggerMode.D3,
            UserDailyAICallCount: userDailyAICallCount);
    }

    private static AITriggerGateDecision Reject(
        AITriggerSkipReason reason,
        MerchantResolutionRunState? runState,
        Guid? connectionId,
        int userDailyAICallCount,
        DateTime? merchantCooldownUntilUtc,
        DateTime? unresolvedCooldownUntilUtc,
        string queueState)
    {
        return new AITriggerGateDecision(
            ShouldTriggerAI: false,
            SkipReason: reason,
            BudgetState: BuildBudgetState(runState, connectionId, userDailyAICallCount, backlogState: null),
            CooldownState: BuildCooldownState(merchantCooldownUntilUtc, unresolvedCooldownUntilUtc),
            QueueState: queueState,
            SuggestionOnly: reason == AITriggerSkipReason.UserConfirmationPreferred,
            UserDailyAICallCount: userDailyAICallCount);
    }

    private static string BuildBudgetState(
        MerchantResolutionRunState? runState,
        Guid? connectionId,
        int dailyCount,
        string? backlogState)
    {
        var runCount = runState?.AICallsThisRun ?? 0;
        var connectionCount = connectionId.HasValue && runState is not null
            ? runState.GetAICallsForConnection(connectionId.Value)
            : 0;

        var state = $"run={runCount};connection={connectionCount};daily24h={dailyCount}";
        if (!string.IsNullOrWhiteSpace(backlogState))
        {
            state = $"{state};{backlogState}";
        }

        return state;
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

    private int ResolveQueueSliceLimit(MerchantResolutionRunState? runState, Guid? connectionId)
    {
        var runTopSlice = Math.Max(1, _governance.QueueTopMerchantsPerRun);
        var connectionTopSlice = connectionId.HasValue
            ? Math.Max(1, _governance.QueueTopMerchantsPerConnectionPerRun)
            : runTopSlice;
        var runBudgetRemaining = runState is null
            ? runTopSlice
            : Math.Max(0, Math.Max(1, _governance.MaxAICallsPerSyncRun) - runState.AICallsThisRun);
        var connectionBudgetRemaining = connectionId.HasValue && runState is not null
            ? Math.Max(0, Math.Max(1, _governance.MaxAICallsPerConnectionPerRun) - runState.GetAICallsForConnection(connectionId.Value))
            : connectionTopSlice;

        var queueWindow = Math.Min(runTopSlice, connectionTopSlice);
        var budgetWindow = Math.Min(runBudgetRemaining, connectionBudgetRemaining);
        if (budgetWindow <= 0)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(queueWindow, budgetWindow));
    }
}
