using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IMerchantInvestigationQueueService
{
    Task<MerchantInvestigationQueueEvaluation> EvaluateAsync(
        MerchantInvestigationQueueEvaluationRequest request,
        CancellationToken cancellationToken);

    Task<MerchantInvestigationLockResult> TryAcquireLockAsync(
        Guid unresolvedMerchantId,
        CancellationToken cancellationToken);

    Task ReleaseLockAsync(
        Guid unresolvedMerchantId,
        Guid lockId,
        bool markFailed,
        CancellationToken cancellationToken);
}

public sealed record MerchantInvestigationQueueEvaluationRequest(
    MerchantResolutionRequest ResolutionRequest,
    UnresolvedMerchant UnresolvedMerchant,
    DomainTriggerMode TriggerMode);

public sealed record MerchantInvestigationQueueEvaluation(
    double PriorityScore,
    double ExpectedValueScore,
    int QueuePosition,
    int QueueDepth,
    MerchantInvestigationBacklogMetrics BacklogMetrics)
{
    public string ToQueueState()
    {
        return $"priority={PriorityScore:0.000};expectedValue={ExpectedValueScore:0.000};position={QueuePosition};depth={QueueDepth}";
    }
}

public sealed record MerchantInvestigationBacklogMetrics(
    int UnresolvedMerchantCount,
    int QueuedMerchantCount,
    int InvestigatedToday,
    int SkippedDueToBudgetToday,
    int SkippedDueToCooldownToday)
{
    public string ToMetricsState()
    {
        return $"unresolved={UnresolvedMerchantCount};queued={QueuedMerchantCount};investigatedToday={InvestigatedToday};skippedBudgetToday={SkippedDueToBudgetToday};skippedCooldownToday={SkippedDueToCooldownToday}";
    }
}

public sealed record MerchantInvestigationLockResult(
    bool Acquired,
    Guid? LockId,
    DateTime? ExistingLockAcquiredUtc);

public sealed class MerchantInvestigationQueueService(
    AppDbContext dbContext,
    IOptions<MerchantAIGovernanceOptions> governanceOptions) : IMerchantInvestigationQueueService
{
    private readonly MerchantAIGovernanceOptions _governance = governanceOptions.Value;

    public async Task<MerchantInvestigationQueueEvaluation> EvaluateAsync(
        MerchantInvestigationQueueEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var unresolved = request.UnresolvedMerchant;
        unresolved.QueueEnqueuedAtUtc ??= nowUtc;
        unresolved.QueueLastScoredUtc = nowUtc;
        unresolved.QueuePriorityScore = ComputePriorityScore(unresolved, request.TriggerMode, nowUtc);
        unresolved.QueueNextRetryUtc ??= unresolved.NextEligibleInvestigationUtc;

        var queueCandidates = await dbContext.UnresolvedMerchants
            .AsNoTracking()
            .Where(x => x.Status != UnresolvedMerchantStatus.Resolved)
            .Where(x => !x.QueueNextRetryUtc.HasValue || x.QueueNextRetryUtc.Value <= nowUtc)
            .Where(x => !x.NextEligibleInvestigationUtc.HasValue || x.NextEligibleInvestigationUtc.Value <= nowUtc)
            .OrderByDescending(x => x.QueuePriorityScore)
            .ThenByDescending(x => x.OccurrenceCount)
            .ThenBy(x => x.QueueEnqueuedAtUtc)
            .Select(x => x.Id)
            .Take(1_000)
            .ToListAsync(cancellationToken);

        var queueDepth = queueCandidates.Count;
        var queuePosition = queueCandidates.FindIndex(id => id == unresolved.Id) + 1;
        if (queuePosition <= 0)
        {
            queuePosition = queueDepth + 1;
        }

        var expectedValueScore = ComputeExpectedValueScore(unresolved, request.ResolutionRequest.RawDescriptor, nowUtc);
        var backlogMetrics = await ComputeBacklogMetricsAsync(cancellationToken);

        return new MerchantInvestigationQueueEvaluation(
            PriorityScore: Math.Round(unresolved.QueuePriorityScore, 6, MidpointRounding.AwayFromZero),
            ExpectedValueScore: Math.Round(expectedValueScore, 6, MidpointRounding.AwayFromZero),
            QueuePosition: queuePosition,
            QueueDepth: queueDepth,
            BacklogMetrics: backlogMetrics);
    }

    public async Task<MerchantInvestigationLockResult> TryAcquireLockAsync(
        Guid unresolvedMerchantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nowUtc = DateTime.UtcNow;
        var lockTimeout = TimeSpan.FromMinutes(Math.Max(1, _governance.InvestigationLockTimeoutMinutes));
        var unresolved = await dbContext.UnresolvedMerchants
            .SingleOrDefaultAsync(x => x.Id == unresolvedMerchantId, cancellationToken);
        if (unresolved is null)
        {
            return new MerchantInvestigationLockResult(
                Acquired: false,
                LockId: null,
                ExistingLockAcquiredUtc: null);
        }

        var lockActive = unresolved.InvestigationInProgress
                         && unresolved.InvestigationLockId.HasValue
                         && unresolved.InvestigationLockAcquiredUtc.HasValue
                         && unresolved.InvestigationLockAcquiredUtc.Value >= nowUtc.Subtract(lockTimeout);
        if (lockActive)
        {
            return new MerchantInvestigationLockResult(
                Acquired: false,
                LockId: null,
                ExistingLockAcquiredUtc: unresolved.InvestigationLockAcquiredUtc);
        }

        var lockId = Guid.NewGuid();
        unresolved.InvestigationInProgress = true;
        unresolved.InvestigationLockId = lockId;
        unresolved.InvestigationLockAcquiredUtc = nowUtc;
        unresolved.Status = UnresolvedMerchantStatus.Investigating;
        unresolved.LastInvestigationUtc = nowUtc;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MerchantInvestigationLockResult(
            Acquired: true,
            LockId: lockId,
            ExistingLockAcquiredUtc: null);
    }

    public async Task ReleaseLockAsync(
        Guid unresolvedMerchantId,
        Guid lockId,
        bool markFailed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unresolved = await dbContext.UnresolvedMerchants
            .SingleOrDefaultAsync(x => x.Id == unresolvedMerchantId, cancellationToken);
        if (unresolved is null
            || !unresolved.InvestigationLockId.HasValue
            || unresolved.InvestigationLockId.Value != lockId)
        {
            return;
        }

        unresolved.InvestigationInProgress = false;
        unresolved.InvestigationLockId = null;
        unresolved.InvestigationLockAcquiredUtc = null;
        if (markFailed)
        {
            unresolved.QueueRetryCount += 1;
            unresolved.QueueNextRetryUtc = unresolved.NextEligibleInvestigationUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<MerchantInvestigationBacklogMetrics> ComputeBacklogMetricsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var dayStartUtc = nowUtc.AddHours(-24);
        var unresolvedCount = await dbContext.UnresolvedMerchants
            .AsNoTracking()
            .CountAsync(x => x.Status != UnresolvedMerchantStatus.Resolved, cancellationToken);
        var queuedCount = await dbContext.UnresolvedMerchants
            .AsNoTracking()
            .CountAsync(
                x => x.Status != UnresolvedMerchantStatus.Resolved
                     && (!x.NextEligibleInvestigationUtc.HasValue || x.NextEligibleInvestigationUtc.Value <= nowUtc),
                cancellationToken);
        var investigatedToday = await dbContext.MerchantAIDecisionLogs
            .AsNoTracking()
            .CountAsync(
                x => x.AICallExecuted
                     && x.CreatedUtc >= dayStartUtc,
                cancellationToken);
        var skippedBudget = await dbContext.MerchantAIDecisionLogs
            .AsNoTracking()
            .CountAsync(
                x => !x.AICallExecuted
                     && x.CreatedUtc >= dayStartUtc
                     && x.AISkipReason == AITriggerSkipReason.RunBudgetExceeded.ToString(),
                cancellationToken);
        var skippedCooldown = await dbContext.MerchantAIDecisionLogs
            .AsNoTracking()
            .CountAsync(
                x => !x.AICallExecuted
                     && x.CreatedUtc >= dayStartUtc
                     && x.AISkipReason == AITriggerSkipReason.MerchantOnCooldown.ToString(),
                cancellationToken);

        return new MerchantInvestigationBacklogMetrics(
            UnresolvedMerchantCount: unresolvedCount,
            QueuedMerchantCount: queuedCount,
            InvestigatedToday: investigatedToday,
            SkippedDueToBudgetToday: skippedBudget,
            SkippedDueToCooldownToday: skippedCooldown);
    }

    private double ComputePriorityScore(UnresolvedMerchant unresolved, DomainTriggerMode triggerMode, DateTime nowUtc)
    {
        var countScore = Math.Clamp(unresolved.OccurrenceCount / 10d, 0d, 1d);
        var spendNormalizer = Math.Max(10m, _governance.SpendNormalizerAmount);
        var spendScore = Math.Clamp((double)(unresolved.TotalObservedSpendAbs / spendNormalizer), 0d, 1d);
        var recencyScore = ComputeRecencyScore(unresolved.LastSeenUtc, nowUtc);
        var unresolvedImpactScore = unresolved.InvestigationAttemptCount == 0
            ? 1d
            : Math.Clamp(1d - (unresolved.InvestigationAttemptCount / 8d), 0.20d, 1d);
        var domainImportanceScore = triggerMode switch
        {
            DomainTriggerMode.D2 => 1d,
            DomainTriggerMode.D3 => 0.75d,
            DomainTriggerMode.D1 => 0.50d,
            _ => 0.15d
        };

        var priority = (_governance.QueuePriorityCountWeight * countScore)
                       + (_governance.QueuePrioritySpendWeight * spendScore)
                       + (_governance.QueuePriorityRecencyWeight * recencyScore)
                       + (_governance.QueuePriorityImpactWeight * unresolvedImpactScore)
                       + (_governance.QueuePriorityDomainWeight * domainImportanceScore);

        if (unresolved.LastInvestigationFailureCode is not null)
        {
            priority -= 0.08d;
        }

        return Math.Clamp(priority, 0d, 2d);
    }

    private double ComputeExpectedValueScore(UnresolvedMerchant unresolved, string rawDescriptor, DateTime nowUtc)
    {
        var countScore = Math.Clamp(unresolved.OccurrenceCount / Math.Max(2d, _governance.MinimumOccurrencesForExpectedValue * 2d), 0d, 1d);
        var spendScore = Math.Clamp((double)(unresolved.TotalObservedSpendAbs / Math.Max(10m, _governance.SpendNormalizerAmount)), 0d, 1d);
        var recencyScore = ComputeRecencyScore(unresolved.LastSeenUtc, nowUtc);
        var recurrenceProbability = Math.Clamp((unresolved.OccurrenceCount - 1) / 5d, 0d, 1d);
        if (LooksLikelyFutureReuse(rawDescriptor))
        {
            recurrenceProbability = Math.Clamp(Math.Max(recurrenceProbability, 0.70d), 0d, 1d);
        }

        var lowConfidencePenalty = unresolved.LastInvestigationFailureCode is not null
                                   || unresolved.Status == UnresolvedMerchantStatus.AwaitingEvidence
            ? 1d
            : 0d;

        var score = (_governance.ExpectedValueCountWeight * countScore)
                    + (_governance.ExpectedValueSpendWeight * spendScore)
                    + (_governance.ExpectedValueRecencyWeight * recencyScore)
                    + (_governance.ExpectedValueReusabilityWeight * recurrenceProbability)
                    - (_governance.ExpectedValueLowConfidencePenaltyWeight * lowConfidencePenalty);
        return Math.Clamp(score, -1d, 2d);
    }

    private double ComputeRecencyScore(DateTime lastSeenUtc, DateTime nowUtc)
    {
        var horizonDays = Math.Max(1, _governance.ExpectedValueRecencyHorizonDays);
        var ageDays = Math.Max(0d, (nowUtc - lastSeenUtc).TotalDays);
        return Math.Clamp(1d - (ageDays / horizonDays), 0d, 1d);
    }

    private static bool LooksLikelyFutureReuse(string descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return false;
        }

        return descriptor.Contains("subscription", StringComparison.OrdinalIgnoreCase)
               || descriptor.Contains("insurance", StringComparison.OrdinalIgnoreCase)
               || descriptor.Contains("utility", StringComparison.OrdinalIgnoreCase)
               || descriptor.Contains("membership", StringComparison.OrdinalIgnoreCase)
               || descriptor.Contains("rent", StringComparison.OrdinalIgnoreCase)
               || descriptor.Contains("mortgage", StringComparison.OrdinalIgnoreCase);
    }
}
