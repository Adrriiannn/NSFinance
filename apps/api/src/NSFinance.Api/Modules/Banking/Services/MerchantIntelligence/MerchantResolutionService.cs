using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using System.Text.Json;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantResolutionService(
    AppDbContext dbContext,
    MerchantDescriptorNormalizer normalizer,
    IMerchantRegistryService merchantRegistryService,
    IMerchantInvestigationService investigationService,
    IDomainTriggerPolicyService domainTriggerPolicyService,
    IAITriggerGateService aiTriggerGateService,
    IMerchantAcceptancePolicy acceptancePolicy,
    ILogger<MerchantResolutionService> logger,
    IOptions<MerchantOperationalResilienceOptions>? resilienceOptions = null,
    IOptions<MerchantAIGovernanceOptions>? governanceOptions = null,
    IOptions<AIIntegrationOptions>? aiOptions = null,
    IOperationalFailureRecorder? failureRecorder = null,
    IMerchantInvestigationQueueService? merchantInvestigationQueueService = null) : IMerchantResolutionService
{
    private const int MaxFuzzyAliasCandidates = 80;
    private const int MaxFamilyCandidates = 30;
    private const double FuzzyAcceptanceThreshold = 0.82d;
    private const double FamilyAcceptanceThreshold = 0.76d;
    private static readonly HashSet<string> DangerousFamilyRoots = new(StringComparer.Ordinal)
    {
        "amazon",
        "google",
        "apple",
        "microsoft",
        "paypal"
    };
    private static readonly HashSet<string> SafeAliasTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BillingDescriptor",
        "MerchantName",
        "Abbreviation",
        "ProcessorDescriptor",
        "Domain"
    };

    private readonly MerchantOperationalResilienceOptions _resilienceOptions = resilienceOptions?.Value ?? new MerchantOperationalResilienceOptions();
    private readonly MerchantAIGovernanceOptions _governanceOptions = governanceOptions?.Value ?? new MerchantAIGovernanceOptions();
    private readonly AIIntegrationOptions _aiOptions = aiOptions?.Value ?? new AIIntegrationOptions();
    private readonly IOperationalFailureRecorder? _failureRecorder = failureRecorder;
    private readonly IMerchantInvestigationQueueService _merchantInvestigationQueueService = merchantInvestigationQueueService ?? new PassiveMerchantInvestigationQueueService();

    public async Task<MerchantResolutionResult> ResolveAsync(string rawDescriptor, CancellationToken cancellationToken)
    {
        return await ResolveAsync(MerchantResolutionRequest.CreateLegacy(rawDescriptor), cancellationToken);
    }

    public async Task<MerchantResolutionResult> ResolveAsync(
        MerchantResolutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var rawDescriptor = request.RawDescriptor;
        var normalizedDescriptor = normalizer.Normalize(rawDescriptor);
        if (normalizedDescriptor.Length == 0)
        {
            return new MerchantResolutionResult(
                MerchantId: null,
                ResolutionConfidence: 0d,
                ResolutionType: MerchantResolutionType.None,
                MatchedAlias: null,
                IsResolved: false,
                UnresolvedMerchantId: null,
                NormalizedDescriptor: string.Empty,
                AcceptanceDecisionType: null,
                ReasonCodes: ["descriptor_empty"],
                FinalState: MerchantResolutionFinalState.Unresolved);
        }

        var exact = await TryResolveExactAliasAsync(normalizedDescriptor, cancellationToken);
        if (exact.IsResolved)
        {
            await TouchResolvedMerchantAsync(exact.MerchantId!.Value, "exact_alias", cancellationToken);
            logger.LogDebug(
                "Merchant resolution short-circuit exact alias; fuzzy/family paths skipped normalizedDescriptor={NormalizedDescriptor}",
                normalizedDescriptor);
            return exact with
            {
                FinalState = MerchantResolutionFinalState.RegistryResolvedTerminal
            };
        }

        var fuzzy = await TryResolveFuzzyAliasAsync(normalizedDescriptor, cancellationToken);
        if (fuzzy.IsResolved)
        {
            await TouchResolvedMerchantAsync(fuzzy.MerchantId!.Value, "fuzzy_alias", cancellationToken);
            return fuzzy with
            {
                FinalState = MerchantResolutionFinalState.RegistryResolvedTerminal
            };
        }

        var family = await TryResolveFamilyMatchAsync(normalizedDescriptor, cancellationToken);
        if (family.IsResolved)
        {
            await TouchResolvedMerchantAsync(family.MerchantId!.Value, "family_match", cancellationToken);
            return family with
            {
                FinalState = MerchantResolutionFinalState.RegistryResolvedTerminal
            };
        }

        return await ResolveThroughUnresolvedLifecycleAsync(request, normalizedDescriptor, cancellationToken);
    }

    private async Task<MerchantResolutionResult> TryResolveExactAliasAsync(string normalizedDescriptor, CancellationToken cancellationToken)
    {
        var exactMatch = await dbContext.MerchantAliases
            .AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedAliasText == normalizedDescriptor)
            .Join(
                dbContext.Merchants.AsNoTracking(),
                alias => alias.MerchantId,
                merchant => merchant.Id,
                (alias, merchant) => new
                {
                    Alias = alias,
                    MerchantStatus = merchant.MerchantStatus
                })
            .Where(x => x.MerchantStatus != MerchantStatus.Retired && x.MerchantStatus != MerchantStatus.Unresolved)
            .OrderByDescending(x => x.Alias.IsExactMatchPreferred)
            .ThenByDescending(x => x.Alias.Confidence)
            .ThenByDescending(x => x.Alias.LastSeenUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (exactMatch is null)
        {
            return UnresolvedResult(normalizedDescriptor, ["exact_alias_not_found"]);
        }

        var confidence = exactMatch.Alias.IsExactMatchPreferred
            ? Math.Max(0.96d, exactMatch.Alias.Confidence)
            : Math.Max(0.90d, exactMatch.Alias.Confidence);

        logger.LogDebug(
            "Merchant resolved by exact alias merchantId={MerchantId} aliasId={AliasId} normalizedAlias={Alias}",
            exactMatch.Alias.MerchantId,
            exactMatch.Alias.Id,
            exactMatch.Alias.NormalizedAliasText);

        return new MerchantResolutionResult(
            MerchantId: exactMatch.Alias.MerchantId,
            ResolutionConfidence: Math.Round(Math.Clamp(confidence, 0d, 1d), 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.ExactAlias,
            MatchedAlias: exactMatch.Alias.AliasText,
            IsResolved: true,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes: ["matched_exact_alias"]);
    }

    private async Task<MerchantResolutionResult> TryResolveFuzzyAliasAsync(string normalizedDescriptor, CancellationToken cancellationToken)
    {
        var descriptorTokens = normalizer.Tokenize(normalizedDescriptor);
        var firstToken = descriptorTokens.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return UnresolvedResult(normalizedDescriptor, ["fuzzy_alias_not_attempted"]);
        }
        var dangerousFamilyDescriptor = IsDangerousFamilyToken(firstToken);

        var fuzzyCandidates = await dbContext.MerchantAliases
            .AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedAliasText.StartsWith(firstToken))
            .Join(
                dbContext.Merchants.AsNoTracking(),
                alias => alias.MerchantId,
                merchant => merchant.Id,
                (alias, merchant) => new
                {
                    Alias = alias,
                    merchant.MerchantStatus,
                    merchant.MerchantUsageType
                })
            .Where(x => x.MerchantStatus != MerchantStatus.Retired && x.MerchantStatus != MerchantStatus.Unresolved)
            .OrderByDescending(x => x.Alias.IsExactMatchPreferred)
            .ThenByDescending(x => x.Alias.Confidence)
            .Take(MaxFuzzyAliasCandidates)
            .ToListAsync(cancellationToken);

        var scoredCandidates = fuzzyCandidates
            .Select(candidate =>
            {
                var aliasTokens = normalizer.Tokenize(candidate.Alias.NormalizedAliasText);
                var tokenSimilarity = ComputeJaccard(descriptorTokens, aliasTokens);
                var startsWith = normalizedDescriptor.StartsWith(candidate.Alias.NormalizedAliasText, StringComparison.Ordinal)
                                 || candidate.Alias.NormalizedAliasText.StartsWith(normalizedDescriptor, StringComparison.Ordinal);
                var aliasRootToken = aliasTokens.FirstOrDefault();
                var dangerousFamilyAlias = IsDangerousFamilyToken(aliasRootToken);
                var score = (tokenSimilarity * 0.72d)
                            + (Math.Clamp(candidate.Alias.Confidence, 0d, 1d) * 0.18d)
                            + (candidate.Alias.IsExactMatchPreferred ? 0.08d : 0d)
                            + (startsWith ? 0.06d : 0d);

                return new
                {
                    candidate.Alias,
                    candidate.MerchantUsageType,
                    Score = Math.Clamp(score, 0d, 1d),
                    TokenSimilarity = tokenSimilarity,
                    StartsWith = startsWith,
                    DangerousFamilyAlias = dangerousFamilyAlias
                };
            })
            .ToList();

        var best = scoredCandidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.TokenSimilarity)
            .FirstOrDefault();

        if (scoredCandidates.Count > 0)
        {
            var topDiagnostics = scoredCandidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TokenSimilarity)
                .Take(3)
                .Select(x => $"alias={x.Alias.NormalizedAliasText}|score={x.Score:0.000}|token={x.TokenSimilarity:0.000}|startsWith={x.StartsWith}|usage={x.MerchantUsageType}|danger={x.DangerousFamilyAlias}")
                .ToArray();
            logger.LogDebug(
                "Merchant fuzzy candidate diagnostics normalizedDescriptor={NormalizedDescriptor} descriptorDangerous={DescriptorDangerous} candidates={Candidates}",
                normalizedDescriptor,
                dangerousFamilyDescriptor,
                string.Join(" || ", topDiagnostics));
        }

        var minimumScore = FuzzyAcceptanceThreshold;
        var minimumTokenSimilarity = 0.72d;
        if (dangerousFamilyDescriptor)
        {
            minimumScore = 0.90d;
            minimumTokenSimilarity = 0.86d;
        }

        var dangerousFamilyGuardFailed = best is not null
                                         && (dangerousFamilyDescriptor || best.DangerousFamilyAlias)
                                         && (!best.StartsWith
                                             || best.TokenSimilarity < 0.90d
                                             || best.MerchantUsageType == MerchantUsageType.MixedUse);

        if (best is null
            || best.Score < minimumScore
            || best.TokenSimilarity < minimumTokenSimilarity
            || dangerousFamilyGuardFailed)
        {
            var reasons = new List<string> { "fuzzy_alias_not_found" };
            if (dangerousFamilyDescriptor || best?.DangerousFamilyAlias == true)
            {
                reasons.Add("fuzzy_alias_dangerous_family_guard");
            }

            logger.LogInformation(
                "Merchant fuzzy resolution fallback to unresolved normalizedDescriptor={NormalizedDescriptor} bestScore={BestScore} bestTokenSimilarity={BestTokenSimilarity} dangerousGuardFailed={DangerousGuardFailed} minScore={MinimumScore} minToken={MinimumToken}",
                normalizedDescriptor,
                best?.Score,
                best?.TokenSimilarity,
                dangerousFamilyGuardFailed,
                minimumScore,
                minimumTokenSimilarity);

            return UnresolvedResult(normalizedDescriptor, reasons);
        }

        logger.LogDebug(
            "Merchant fuzzy resolution selected merchantId={MerchantId} alias={Alias} score={Score} tokenSimilarity={TokenSimilarity} descriptorDangerous={DescriptorDangerous}",
            best.Alias.MerchantId,
            best.Alias.NormalizedAliasText,
            best.Score,
            best.TokenSimilarity,
            dangerousFamilyDescriptor);

        return new MerchantResolutionResult(
            MerchantId: best.Alias.MerchantId,
            ResolutionConfidence: Math.Round(best.Score, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.FuzzyAlias,
            MatchedAlias: best.Alias.AliasText,
            IsResolved: true,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes:
            [
                "matched_fuzzy_alias",
                best.MerchantUsageType == MerchantUsageType.MixedUse ? "mixed_use_alias_resolved" : "alias_resolved"
            ]);
    }

    private async Task<MerchantResolutionResult> TryResolveFamilyMatchAsync(string normalizedDescriptor, CancellationToken cancellationToken)
    {
        var descriptorTokens = normalizer.Tokenize(normalizedDescriptor);
        var firstToken = descriptorTokens.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return UnresolvedResult(normalizedDescriptor, ["family_match_not_attempted"]);
        }
        var dangerousFamilyDescriptor = IsDangerousFamilyToken(firstToken);

        var familyCandidates = await dbContext.Merchants
            .AsNoTracking()
            .Where(x => x.MerchantStatus != MerchantStatus.Retired && x.MerchantStatus != MerchantStatus.Unresolved)
            .Where(x => x.NormalizedCanonicalName.StartsWith(firstToken))
            .OrderByDescending(x => x.MerchantUsageType == MerchantUsageType.MixedUse)
            .ThenBy(x => x.NormalizedCanonicalName.Length)
            .Take(MaxFamilyCandidates)
            .Select(x => new
            {
                x.Id,
                x.CanonicalName,
                x.NormalizedCanonicalName
            })
            .ToListAsync(cancellationToken);

        var scoredFamilyCandidates = familyCandidates
            .Select(candidate =>
            {
                var candidateTokens = normalizer.Tokenize(candidate.NormalizedCanonicalName);
                var similarity = ComputeJaccard(descriptorTokens, candidateTokens);
                var score = (similarity * 0.86d)
                            + (candidate.NormalizedCanonicalName == normalizedDescriptor ? 0.1d : 0d);
                return new
                {
                    candidate.Id,
                    candidate.CanonicalName,
                    candidate.NormalizedCanonicalName,
                    Score = Math.Clamp(score, 0d, 1d),
                    Similarity = similarity
                };
            })
            .ToList();

        var best = scoredFamilyCandidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Similarity)
            .FirstOrDefault();

        if (scoredFamilyCandidates.Count > 0)
        {
            var topDiagnostics = scoredFamilyCandidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Similarity)
                .Take(3)
                .Select(x => $"canonical={x.NormalizedCanonicalName}|score={x.Score:0.000}|similarity={x.Similarity:0.000}")
                .ToArray();
            logger.LogDebug(
                "Merchant family candidate diagnostics normalizedDescriptor={NormalizedDescriptor} descriptorDangerous={DescriptorDangerous} candidates={Candidates}",
                normalizedDescriptor,
                dangerousFamilyDescriptor,
                string.Join(" || ", topDiagnostics));
        }

        var dangerousFamilyGuardFailed = dangerousFamilyDescriptor
                                         && best is not null
                                         && !string.Equals(best.NormalizedCanonicalName, normalizedDescriptor, StringComparison.Ordinal);

        if (best is null || best.Score < FamilyAcceptanceThreshold || best.Similarity < 0.75d || dangerousFamilyGuardFailed)
        {
            var reasons = new List<string> { "family_match_not_found" };
            if (dangerousFamilyGuardFailed)
            {
                reasons.Add("family_match_dangerous_family_guard");
            }

            logger.LogInformation(
                "Merchant family resolution fallback to unresolved normalizedDescriptor={NormalizedDescriptor} bestScore={BestScore} bestSimilarity={BestSimilarity} dangerousGuardFailed={DangerousGuardFailed}",
                normalizedDescriptor,
                best?.Score,
                best?.Similarity,
                dangerousFamilyGuardFailed);

            return UnresolvedResult(normalizedDescriptor, reasons);
        }

        logger.LogDebug(
            "Merchant family resolution selected merchantId={MerchantId} canonical={Canonical} score={Score} similarity={Similarity}",
            best.Id,
            best.CanonicalName,
            best.Score,
            best.Similarity);

        return new MerchantResolutionResult(
            MerchantId: best.Id,
            ResolutionConfidence: Math.Round(best.Score, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.FamilyMatch,
            MatchedAlias: best.CanonicalName,
            IsResolved: true,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes: ["matched_family"]);
    }

    private async Task<MerchantResolutionResult> ResolveThroughUnresolvedLifecycleAsync(
        MerchantResolutionRequest request,
        string normalizedDescriptor,
        CancellationToken cancellationToken)
    {
        var rawDescriptor = request.RawDescriptor;
        var nowUtc = DateTime.UtcNow;
        var unresolved = await dbContext.UnresolvedMerchants
            .SingleOrDefaultAsync(x => x.NormalizedDescriptor == normalizedDescriptor, cancellationToken);
        if (unresolved is null)
        {
            unresolved = new UnresolvedMerchant
            {
                Id = Guid.NewGuid(),
                RawDescriptor = normalizer.SanitizeForStorage(rawDescriptor),
                NormalizedDescriptor = normalizedDescriptor,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc,
                OccurrenceCount = 1,
                Status = UnresolvedMerchantStatus.New,
                InvestigationAttemptCount = 0,
                TotalObservedSpendAbs = Math.Abs(request.Amount),
                QueueEnqueuedAtUtc = nowUtc,
                QueueLastScoredUtc = nowUtc
            };
            dbContext.UnresolvedMerchants.Add(unresolved);
        }
        else
        {
            unresolved.LastSeenUtc = nowUtc;
            unresolved.OccurrenceCount += 1;
            unresolved.TotalObservedSpendAbs += Math.Abs(request.Amount);
        }

        var domainPolicy = domainTriggerPolicyService.Evaluate(
            CollectDomainCandidates(request),
            normalizedDescriptor);
        var merchantLikeDescriptor = request.DescriptorMerchantLike && IsMerchantLikeDescriptor(normalizedDescriptor);
        var knownMerchant = await dbContext.Merchants
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedMerchantKey == normalizedDescriptor, cancellationToken);
        var queueEvaluation = await _merchantInvestigationQueueService.EvaluateAsync(
            new MerchantInvestigationQueueEvaluationRequest(
                ResolutionRequest: request,
                UnresolvedMerchant: unresolved,
                TriggerMode: domainPolicy.TriggerMode),
            cancellationToken);
        logger.LogInformation(
            "[AI_QUEUE] merchantKey={MerchantKey} priority={Priority} expectedValue={ExpectedValue} queuePosition={QueuePosition} queueDepth={QueueDepth} backlog={Backlog}",
            normalizedDescriptor,
            queueEvaluation.PriorityScore,
            queueEvaluation.ExpectedValueScore,
            queueEvaluation.QueuePosition,
            queueEvaluation.QueueDepth,
            queueEvaluation.BacklogMetrics.ToMetricsState());

        var gateDecision = await aiTriggerGateService.EvaluateAsync(
            new AITriggerGateInput(
                Request: request,
                MerchantKey: normalizedDescriptor,
                NormalizedDescriptor: normalizedDescriptor,
                PolicyEvaluation: domainPolicy,
                DeterministicResolved: request.DeterministicTerminal,
                RegistryResolved: false,
                DescriptorMerchantLike: merchantLikeDescriptor,
                MerchantInvestigatedAtUtc: knownMerchant?.InvestigatedAtUtc,
                MerchantCooldownUntilUtc: knownMerchant?.InvestigationCooldownUntilUtc,
                UnresolvedCooldownUntilUtc: unresolved.NextEligibleInvestigationUtc,
                MerchantOccurrenceCount: unresolved.OccurrenceCount,
                ExpectedValueScore: queueEvaluation.ExpectedValueScore,
                QueuePosition: queueEvaluation.QueuePosition,
                QueueDepth: queueEvaluation.QueueDepth,
                QueueState: queueEvaluation.ToQueueState(),
                BacklogState: queueEvaluation.BacklogMetrics.ToMetricsState()),
            cancellationToken);

        if (!gateDecision.ShouldTriggerAI)
        {
            unresolved.Status = UnresolvedMerchantStatus.AwaitingEvidence;
            unresolved.Notes = gateDecision.SkipReason?.ToString() ?? "ai_gate_denied";
            if (gateDecision.SkipReason == AITriggerSkipReason.RunBudgetExceeded)
            {
                unresolved.LastBudgetSkipUtc = nowUtc;
            }

            if (gateDecision.SkipReason == AITriggerSkipReason.MerchantOnCooldown)
            {
                unresolved.LastCooldownSkipUtc = nowUtc;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var skipReason = gateDecision.SkipReason ?? AITriggerSkipReason.DomainPolicyDisallowsAI;
            var deniedFinalState = skipReason == AITriggerSkipReason.DeterministicTerminal
                ? MerchantResolutionFinalState.DeterministicTerminal
                : skipReason == AITriggerSkipReason.UserConfirmationPreferred
                    ? MerchantResolutionFinalState.NeedsUserConfirmation
                    : MerchantResolutionFinalState.Unresolved;
            await PersistAIDecisionLogAsync(
                request,
                normalizedDescriptor,
                domainPolicy,
                deterministicResult: request.DeterministicResultCode ?? (request.DeterministicTerminal ? "deterministic_terminal" : "not_terminal"),
                registryResult: "registry_miss",
                gateDecision,
                deniedFinalState,
                modelUsed: null,
                cancellationToken);

            return new MerchantResolutionResult(
                MerchantId: null,
                ResolutionConfidence: 0d,
                ResolutionType: MerchantResolutionType.None,
                MatchedAlias: null,
                IsResolved: false,
                UnresolvedMerchantId: unresolved.Id,
                NormalizedDescriptor: normalizedDescriptor,
                AcceptanceDecisionType: MerchantAcceptanceDecisionType.Unresolved,
                ReasonCodes: [MapSkipReason(skipReason)],
                FinalState: deniedFinalState,
                TriggerMode: domainPolicy.TriggerMode,
                AIGateSkipReason: skipReason,
                AIGateDecision: false,
                ModelUsed: null);
        }

        var lockResult = await _merchantInvestigationQueueService.TryAcquireLockAsync(unresolved.Id, cancellationToken);
        if (!lockResult.Acquired || !lockResult.LockId.HasValue)
        {
            unresolved.Status = UnresolvedMerchantStatus.AwaitingEvidence;
            unresolved.Notes = "investigation_lock_active";
            await dbContext.SaveChangesAsync(cancellationToken);

            var lockDeniedDecision = new AITriggerGateDecision(
                ShouldTriggerAI: false,
                SkipReason: AITriggerSkipReason.DuplicateMerchantInRun,
                BudgetState: gateDecision.BudgetState,
                CooldownState: gateDecision.CooldownState,
                QueueState: $"{gateDecision.QueueState};lock=active",
                SuggestionOnly: gateDecision.SuggestionOnly,
                UserDailyAICallCount: gateDecision.UserDailyAICallCount);
            await PersistAIDecisionLogAsync(
                request,
                normalizedDescriptor,
                domainPolicy,
                deterministicResult: request.DeterministicResultCode ?? (request.DeterministicTerminal ? "deterministic_terminal" : "not_terminal"),
                registryResult: "registry_miss",
                lockDeniedDecision,
                MerchantResolutionFinalState.Unresolved,
                modelUsed: null,
                cancellationToken);

            return new MerchantResolutionResult(
                MerchantId: null,
                ResolutionConfidence: 0d,
                ResolutionType: MerchantResolutionType.None,
                MatchedAlias: null,
                IsResolved: false,
                UnresolvedMerchantId: unresolved.Id,
                NormalizedDescriptor: normalizedDescriptor,
                AcceptanceDecisionType: MerchantAcceptanceDecisionType.Unresolved,
                ReasonCodes: [MapSkipReason(AITriggerSkipReason.DuplicateMerchantInRun)],
                FinalState: MerchantResolutionFinalState.Unresolved,
                TriggerMode: domainPolicy.TriggerMode,
                AIGateSkipReason: AITriggerSkipReason.DuplicateMerchantInRun,
                AIGateDecision: false,
                ModelUsed: null);
        }

        var investigationLockId = lockResult.LockId.Value;
        unresolved.InvestigationAttemptCount += 1;
        unresolved.NextEligibleInvestigationUtc = nowUtc.AddMinutes(ResolveBackoffMinutes(unresolved.InvestigationAttemptCount, isRejected: false));
        await dbContext.SaveChangesAsync(cancellationToken);
        request.RunState?.MarkAICallExecuted(request.ConnectionId);

        var releaseLockAsFailed = true;
        try
        {
            var investigationResult = await investigationService.InvestigateAsync(
                new MerchantInvestigationRequest(
                    RawDescriptor: normalizer.SanitizeForStorage(rawDescriptor),
                    NormalizedDescriptor: normalizedDescriptor,
                    TriggerSource: "resolution_miss"),
                cancellationToken);

            var modelUsed = ResolveInvestigationModelName();
            var decision = acceptancePolicy.Evaluate(investigationResult);
            logger.LogInformation(
                "Merchant investigation decision normalizedDescriptor={NormalizedDescriptor} unresolvedId={UnresolvedMerchantId} recommendation={Recommendation} candidates={CandidateCount} overallConfidence={OverallConfidence} ambiguity={AmbiguityLevel} parserRejected={ParserRejected} decision={Decision} reasonCodes={ReasonCodes} attemptCount={AttemptCount}",
                normalizedDescriptor,
                unresolved.Id,
                investigationResult.Recommendation,
                investigationResult.Candidates.Count,
                investigationResult.OverallConfidence,
                investigationResult.AmbiguityLevel,
                investigationResult.ParserRejected,
                decision.DecisionType,
                string.Join(",", decision.ReasonCodes),
                unresolved.InvestigationAttemptCount);

            if (decision.DecisionType is MerchantAcceptanceDecisionType.AcceptedTrusted or MerchantAcceptanceDecisionType.AcceptedCautious
                && decision.SelectedCandidate is not null)
            {
                try
                {
                    var resolved = await ApplyAcceptedInvestigationAsync(
                        unresolved,
                        rawDescriptor,
                        normalizedDescriptor,
                        decision,
                        investigationResult,
                        domainPolicy,
                        gateDecision.SuggestionOnly,
                        cancellationToken);

                    await PersistAIDecisionLogAsync(
                        request,
                        normalizedDescriptor,
                        domainPolicy,
                        deterministicResult: request.DeterministicResultCode ?? (request.DeterministicTerminal ? "deterministic_terminal" : "not_terminal"),
                        registryResult: "registry_miss",
                        gateDecision,
                        resolved.FinalState,
                        modelUsed,
                        cancellationToken);

                    releaseLockAsFailed = false;
                    return resolved;
                }
                catch (Exception ex)
                {
                    unresolved.Status = UnresolvedMerchantStatus.AwaitingEvidence;
                    unresolved.Notes = "apply_accepted_investigation_failed";
                    unresolved.LastInvestigationFailureCode = "apply_accepted_investigation_failed";
                    unresolved.LastInvestigationFailureUtc = nowUtc;
                    unresolved.NextEligibleInvestigationUtc = nowUtc.AddMinutes(ResolveBackoffMinutes(unresolved.InvestigationAttemptCount, isRejected: true));
                    await dbContext.SaveChangesAsync(cancellationToken);

                    await RecordOperationalFailureAsync(
                        OperationalFailureArea.MerchantResolution,
                        OperationalFailureSeverity.Error,
                        "apply_accepted_investigation_failed",
                        $"apply_accepted_investigation_failed:{normalizedDescriptor}",
                        normalizedDescriptor,
                        ex.Message,
                        cancellationToken);

                    logger.LogError(
                        ex,
                        "Merchant resolution apply-accepted failed normalizedDescriptor={NormalizedDescriptor} unresolvedId={UnresolvedMerchantId} decision={Decision}",
                        normalizedDescriptor,
                        unresolved.Id,
                        decision.DecisionType);

                    await PersistAIDecisionLogAsync(
                        request,
                        normalizedDescriptor,
                        domainPolicy,
                        deterministicResult: request.DeterministicResultCode ?? (request.DeterministicTerminal ? "deterministic_terminal" : "not_terminal"),
                        registryResult: "registry_miss",
                        gateDecision,
                        MerchantResolutionFinalState.Unresolved,
                        modelUsed,
                        cancellationToken);

                    return new MerchantResolutionResult(
                        MerchantId: null,
                        ResolutionConfidence: Math.Round(decision.Confidence, 4, MidpointRounding.AwayFromZero),
                        ResolutionType: MerchantResolutionType.None,
                        MatchedAlias: null,
                        IsResolved: false,
                        UnresolvedMerchantId: unresolved.Id,
                        NormalizedDescriptor: normalizedDescriptor,
                        AcceptanceDecisionType: MerchantAcceptanceDecisionType.Rejected,
                        ReasonCodes: ["apply_accepted_investigation_failed"],
                        FinalState: MerchantResolutionFinalState.Unresolved,
                        TriggerMode: domainPolicy.TriggerMode,
                        AIGateSkipReason: null,
                        AIGateDecision: true,
                        ModelUsed: modelUsed);
                }
            }

            unresolved.Status = decision.DecisionType == MerchantAcceptanceDecisionType.Rejected
                ? UnresolvedMerchantStatus.AwaitingEvidence
                : UnresolvedMerchantStatus.Investigating;
            unresolved.NextEligibleInvestigationUtc = decision.DecisionType switch
            {
                MerchantAcceptanceDecisionType.Rejected => nowUtc.AddHours(Math.Max(1, _governanceOptions.FailureCooldownHours)),
                MerchantAcceptanceDecisionType.LowConfidence => nowUtc.AddHours(Math.Max(1, _governanceOptions.LowConfidenceCooldownHours)),
                _ => nowUtc.AddDays(Math.Max(1, _governanceOptions.MerchantInvestigationCooldownDays))
            };

            if (decision.DecisionType == MerchantAcceptanceDecisionType.Rejected)
            {
                unresolved.LastInvestigationFailureCode = "decision_rejected";
                unresolved.LastInvestigationFailureUtc = nowUtc;
            }

            unresolved.Notes = decision.ReasonCodes.Count == 0
                ? null
                : string.Join(",", decision.ReasonCodes);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (decision.DecisionType is MerchantAcceptanceDecisionType.Rejected or MerchantAcceptanceDecisionType.LowConfidence)
            {
                await RecordOperationalFailureAsync(
                    OperationalFailureArea.MerchantResolution,
                    decision.DecisionType == MerchantAcceptanceDecisionType.Rejected
                        ? OperationalFailureSeverity.Warning
                        : OperationalFailureSeverity.Info,
                    $"decision_{decision.DecisionType.ToString().ToLowerInvariant()}",
                    $"decision_{decision.DecisionType.ToString().ToLowerInvariant()}:{normalizedDescriptor}",
                    normalizedDescriptor,
                    string.Join(",", decision.ReasonCodes),
                    cancellationToken);
            }

            logger.LogInformation(
                "Merchant unresolved normalizedDescriptor={NormalizedDescriptor} unresolvedId={UnresolvedMerchantId} decision={Decision} reasonCodes={ReasonCodes} nextEligible={NextEligible}",
                normalizedDescriptor,
                unresolved.Id,
                decision.DecisionType,
                string.Join(",", decision.ReasonCodes),
                unresolved.NextEligibleInvestigationUtc);

            var finalState = gateDecision.SuggestionOnly
                ? MerchantResolutionFinalState.AIEnrichedSuggestionOnly
                : decision.DecisionType is MerchantAcceptanceDecisionType.LowConfidence or MerchantAcceptanceDecisionType.Rejected or MerchantAcceptanceDecisionType.Unresolved
                    ? MerchantResolutionFinalState.NeedsUserConfirmation
                    : MerchantResolutionFinalState.Unresolved;

            await PersistAIDecisionLogAsync(
                request,
                normalizedDescriptor,
                domainPolicy,
                deterministicResult: request.DeterministicResultCode ?? (request.DeterministicTerminal ? "deterministic_terminal" : "not_terminal"),
                registryResult: "registry_miss",
                gateDecision,
                finalState,
                modelUsed,
                cancellationToken);

            return new MerchantResolutionResult(
                MerchantId: null,
                ResolutionConfidence: Math.Round(decision.Confidence, 4, MidpointRounding.AwayFromZero),
                ResolutionType: MerchantResolutionType.None,
                MatchedAlias: null,
                IsResolved: false,
                UnresolvedMerchantId: unresolved.Id,
                NormalizedDescriptor: normalizedDescriptor,
                AcceptanceDecisionType: decision.DecisionType,
                ReasonCodes: decision.ReasonCodes,
                FinalState: finalState,
                TriggerMode: domainPolicy.TriggerMode,
                AIGateSkipReason: null,
                AIGateDecision: true,
                ModelUsed: modelUsed);
        }
        finally
        {
            await _merchantInvestigationQueueService.ReleaseLockAsync(
                unresolved.Id,
                investigationLockId,
                markFailed: releaseLockAsFailed,
                cancellationToken);
        }
    }

    private async Task<MerchantResolutionResult> ApplyAcceptedInvestigationAsync(
        UnresolvedMerchant unresolved,
        string rawDescriptor,
        string normalizedDescriptor,
        MerchantAcceptanceDecision decision,
        MerchantInvestigationResult investigationResult,
        DomainTriggerPolicyEvaluation domainPolicy,
        bool suggestionOnly,
        CancellationToken cancellationToken)
    {
        var selectedCandidate = decision.SelectedCandidate!;
        Merchant merchant;
        if (selectedCandidate.ExistingMerchantId.HasValue)
        {
            merchant = await dbContext.Merchants
                          .SingleOrDefaultAsync(x => x.Id == selectedCandidate.ExistingMerchantId.Value, cancellationToken)
                      ?? await merchantRegistryService.CreateMerchantAsync(
                          new MerchantCreateRequest(
                              CanonicalName: selectedCandidate.CanonicalName,
                              DisplayName: selectedCandidate.DisplayName,
                              MerchantStatus: decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedTrusted
                                  ? MerchantStatus.Active
                                  : MerchantStatus.LowConfidence,
                              MerchantType: selectedCandidate.MerchantType,
                              MerchantUsageType: selectedCandidate.MerchantUsageType,
                              PrimaryCountryCode: selectedCandidate.PrimaryCountryCode,
                              OfficialWebsite: selectedCandidate.OfficialWebsite,
                              DescriptionSummary: selectedCandidate.DescriptionSummary,
                              ParentMerchantId: null),
                          cancellationToken);
        }
        else
        {
            merchant = await merchantRegistryService.CreateMerchantAsync(
                new MerchantCreateRequest(
                    CanonicalName: selectedCandidate.CanonicalName,
                    DisplayName: selectedCandidate.DisplayName,
                    MerchantStatus: decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedTrusted
                        ? MerchantStatus.Active
                        : MerchantStatus.LowConfidence,
                    MerchantType: selectedCandidate.MerchantType,
                    MerchantUsageType: selectedCandidate.MerchantUsageType,
                    PrimaryCountryCode: selectedCandidate.PrimaryCountryCode,
                    OfficialWebsite: selectedCandidate.OfficialWebsite,
                    DescriptionSummary: selectedCandidate.DescriptionSummary,
                    ParentMerchantId: null),
                          cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;
        merchant.ValidationAttemptCount += 1;
        merchant.LastValidatedUtc = nowUtc;
        merchant.LastValidationResultCode = decision.DecisionType switch
        {
            MerchantAcceptanceDecisionType.AcceptedTrusted => "investigation_trusted",
            MerchantAcceptanceDecisionType.AcceptedCautious => "investigation_cautious",
            _ => merchant.LastValidationResultCode
        };
        merchant.NextValidationDueUtc = nowUtc.AddDays(ResolveValidationDueDays(merchant.MerchantStatus, decision.DecisionType));
        if (decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedTrusted && merchant.MerchantStatus == MerchantStatus.LowConfidence)
        {
            merchant.MerchantStatus = MerchantStatus.Active;
        }
        else if (decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedCautious && merchant.MerchantStatus == MerchantStatus.Active)
        {
            merchant.MerchantStatus = MerchantStatus.LowConfidence;
        }

        merchant.UpdatedUtc = nowUtc;
        merchant.CanonicalMerchantName = merchant.CanonicalName;
        merchant.NormalizedMerchantKey = normalizedDescriptor;
        merchant.WebsiteDomain = ExtractWebsiteDomain(selectedCandidate.OfficialWebsite);
        merchant.CountryCode = selectedCandidate.PrimaryCountryCode;
        merchant.MerchantVertical = selectedCandidate.MerchantType.ToString();
        merchant.GoodsServicesType = selectedCandidate.MerchantUsageType.ToString();
        merchant.MerchantSummary = selectedCandidate.DescriptionSummary;
        merchant.CategoryCandidates = JsonSerializer.Serialize(
            investigationResult.Candidates
                .Take(5)
                .Select(x => new
                {
                    canonicalName = x.CanonicalName,
                    confidence = Math.Round(Math.Clamp(x.Confidence, 0d, 1d), 4, MidpointRounding.AwayFromZero),
                    ambiguity = Math.Round(Math.Clamp(x.AmbiguityScore, 0d, 1d), 4, MidpointRounding.AwayFromZero)
                }));
        merchant.TopDomainCode = domainPolicy.DomainCandidates.Count == 0
            ? null
            : domainPolicy.DomainCandidates[0];
        merchant.TopCategoryCode = null;
        merchant.TopSubcategoryCode = null;
        merchant.Confidence = Math.Round(Math.Clamp(decision.Confidence, 0d, 1d), 4, MidpointRounding.AwayFromZero);
        merchant.EvidenceQuality = Math.Round(Math.Clamp(
            investigationResult.Evidence.Count == 0 ? decision.Confidence : investigationResult.Evidence.Average(x => x.Confidence),
            0d,
            1d), 4, MidpointRounding.AwayFromZero);
        merchant.AmbiguityFlags = investigationResult.AmbiguityLevel >= 0.35d
            ? "ambiguous_intent_or_entity"
            : null;
        merchant.InvestigationModel = ResolveInvestigationModelName();
        merchant.InvestigatedAtUtc = nowUtc;
        merchant.LastUsedAtUtc = nowUtc;
        merchant.InvestigationCooldownUntilUtc = nowUtc.AddDays(Math.Max(1, _governanceOptions.MerchantInvestigationCooldownDays));
        merchant.LastFailureUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await merchantRegistryService.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                MerchantId: merchant.Id,
                AliasText: rawDescriptor,
                AliasType: MerchantAliasType.BillingDescriptor,
                Confidence: Math.Clamp(decision.Confidence, 0.6d, 1d),
                IsExactMatchPreferred: false,
                Source: $"investigation:{decision.DecisionType}",
                IsActive: true,
                TrustLevel: decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedTrusted
                    ? MerchantAliasTrustLevel.Trusted
                    : MerchantAliasTrustLevel.Cautious,
                LifecycleReason: "investigation_primary_descriptor"),
            cancellationToken);

        var autoAttachedAliases = 0;
        var skippedAliases = 0;
        foreach (var suggestion in investigationResult.AliasSuggestions ?? [])
        {
            if (!IsSafeAliasSuggestion(suggestion))
            {
                skippedAliases += 1;
                continue;
            }

            await merchantRegistryService.AttachAliasAsync(
                new MerchantAliasCreateRequest(
                    MerchantId: merchant.Id,
                    AliasText: suggestion.AliasText,
                    AliasType: MapAliasType(suggestion.AliasType),
                    Confidence: Math.Clamp(suggestion.Confidence, 0.55d, 0.93d),
                    IsExactMatchPreferred: false,
                    Source: $"investigation:{decision.DecisionType}:alias_suggestion",
                    IsActive: true,
                    TrustLevel: MerchantAliasTrustLevel.Cautious,
                    LifecycleReason: "investigation_alias_suggestion"),
                cancellationToken);
            autoAttachedAliases += 1;
        }

        foreach (var evidence in investigationResult.Evidence)
        {
            await merchantRegistryService.AddEvidenceAsync(
                new MerchantEvidenceCreateRequest(
                    MerchantId: merchant.Id,
                    EvidenceType: evidence.EvidenceType,
                    EvidenceSummary: evidence.EvidenceSummary,
                    Confidence: evidence.Confidence,
                    SourceReference: string.IsNullOrWhiteSpace(evidence.SourceClass)
                        ? evidence.SourceReference
                        : $"{evidence.SourceClass}|{evidence.SourceReference}"),
                cancellationToken);
        }

        unresolved.Status = suggestionOnly
            ? UnresolvedMerchantStatus.AwaitingEvidence
            : UnresolvedMerchantStatus.Resolved;
        unresolved.Notes = suggestionOnly
            ? $"suggestion_only:{merchant.Id:N}"
            : $"resolved:{merchant.Id:N}";
        unresolved.NextEligibleInvestigationUtc = nowUtc.AddDays(Math.Max(1, _governanceOptions.MerchantInvestigationCooldownDays));
        unresolved.LastInvestigationFailureCode = null;
        unresolved.LastInvestigationFailureUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Merchant resolved through investigation normalizedDescriptor={NormalizedDescriptor} merchantId={MerchantId} decision={Decision} aliasesAutoAttached={AliasesAutoAttached} aliasesSkipped={AliasesSkipped}",
            normalizedDescriptor,
            merchant.Id,
            decision.DecisionType,
            autoAttachedAliases,
            skippedAliases);

        return new MerchantResolutionResult(
            MerchantId: suggestionOnly ? null : merchant.Id,
            ResolutionConfidence: Math.Round(decision.Confidence, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.FamilyMatch,
            MatchedAlias: rawDescriptor,
            IsResolved: !suggestionOnly,
            UnresolvedMerchantId: unresolved.Id,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: decision.DecisionType,
            ReasonCodes: suggestionOnly
                ? [.. decision.ReasonCodes, "user_confirmation_preferred"]
                : decision.ReasonCodes,
            FinalState: suggestionOnly
                ? MerchantResolutionFinalState.AIEnrichedSuggestionOnly
                : MerchantResolutionFinalState.AIResolvedTerminal,
            TriggerMode: domainPolicy.TriggerMode,
            AIGateSkipReason: suggestionOnly ? AITriggerSkipReason.UserConfirmationPreferred : null,
            AIGateDecision: true,
            ModelUsed: ResolveInvestigationModelName());
    }

    private bool ShouldSkipInvestigationDueToCooldown(
        UnresolvedMerchant unresolved,
        DateTime nowUtc,
        DateTime referenceLastSeenUtc,
        out string reason)
    {
        reason = string.Empty;
        if (!unresolved.NextEligibleInvestigationUtc.HasValue || unresolved.NextEligibleInvestigationUtc.Value <= nowUtc)
        {
            return false;
        }

        var accelerationThreshold = Math.Max(2, _resilienceOptions.HighOccurrenceAccelerationThreshold);
        if (unresolved.OccurrenceCount >= accelerationThreshold)
        {
            var acceleratedEligibleUtc = referenceLastSeenUtc.AddMinutes(Math.Max(5, _resilienceOptions.HighOccurrenceAccelerationMinutes));
            if (acceleratedEligibleUtc <= nowUtc)
            {
                return false;
            }
        }

        reason = $"cooldown_active_until:{unresolved.NextEligibleInvestigationUtc.Value:O}";
        return true;
    }

    private int ResolveBackoffMinutes(int attemptCount, bool isRejected)
    {
        if (isRejected)
        {
            return Math.Max(10, _resilienceOptions.RejectedCooldownMinutes);
        }

        var safeAttempt = Math.Max(1, attemptCount);
        var baseMinutes = Math.Max(5, _resilienceOptions.UnresolvedBaseCooldownMinutes);
        var multiplier = Math.Pow(2d, Math.Min(6, safeAttempt - 1));
        var backoff = (int)Math.Round(baseMinutes * multiplier, MidpointRounding.AwayFromZero);
        return Math.Min(Math.Max(baseMinutes, backoff), Math.Max(baseMinutes, _resilienceOptions.UnresolvedMaxCooldownMinutes));
    }

    private int ResolveValidationDueDays(MerchantStatus status, MerchantAcceptanceDecisionType decisionType)
    {
        if (decisionType == MerchantAcceptanceDecisionType.AcceptedTrusted)
        {
            return Math.Max(14, _resilienceOptions.ActiveMerchantValidationDays);
        }

        if (decisionType == MerchantAcceptanceDecisionType.AcceptedCautious)
        {
            return Math.Max(7, _resilienceOptions.CautiousMerchantValidationDays);
        }

        return status == MerchantStatus.LowConfidence
            ? Math.Max(7, _resilienceOptions.LowConfidenceMerchantValidationDays)
            : Math.Max(14, _resilienceOptions.ActiveMerchantValidationDays);
    }

    private async Task TouchResolvedMerchantAsync(Guid merchantId, string resolutionSource, CancellationToken cancellationToken)
    {
        var merchant = await dbContext.Merchants
            .SingleOrDefaultAsync(x => x.Id == merchantId, cancellationToken);
        if (merchant is null)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        merchant.UpdatedUtc = nowUtc;
        if (!merchant.LastValidatedUtc.HasValue)
        {
            merchant.LastValidatedUtc = nowUtc;
        }

        merchant.NextValidationDueUtc ??= nowUtc.AddDays(Math.Max(14, _resilienceOptions.ActiveMerchantValidationDays));

        var revalidationReasons = await ResolveRevalidationReasonsAsync(merchant, nowUtc, resolutionSource, cancellationToken);
        if (revalidationReasons.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var canRevalidateNow = !merchant.LastValidatedUtc.HasValue
                               || merchant.LastValidatedUtc.Value.AddMinutes(Math.Max(15, _resilienceOptions.RevalidationMinimumIntervalMinutes)) <= nowUtc;
        if (!canRevalidateNow)
        {
            var deferredDueUtc = nowUtc.AddMinutes(Math.Max(15, _resilienceOptions.RevalidationMinimumIntervalMinutes));
            merchant.LastValidationResultCode = "revalidation_deferred_interval_guard";
            if (!merchant.NextValidationDueUtc.HasValue || merchant.NextValidationDueUtc.Value > deferredDueUtc)
            {
                merchant.NextValidationDueUtc = deferredDueUtc;
            }

            dbContext.MerchantRevalidationRecords.Add(new MerchantRevalidationRecord
            {
                Id = Guid.NewGuid(),
                MerchantId = merchant.Id,
                AttemptedUtc = nowUtc,
                TriggerReason = string.Join(",", revalidationReasons),
                Outcome = MerchantRevalidationOutcome.Failed,
                DecisionCode = null,
                PreviousStatus = merchant.MerchantStatus,
                NewStatus = merchant.MerchantStatus,
                StatusChanged = false,
                AliasTrustChanges = 0,
                RequiresUnresolvedReview = false,
                ContradictionDetected = false,
                LeadingEvidenceSummary = "Revalidation deferred by minimum interval guard.",
                ResultCode = "revalidation_deferred_interval_guard",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    reasons = revalidationReasons,
                    minIntervalMinutes = _resilienceOptions.RevalidationMinimumIntervalMinutes
                })
            });

            await RecordOperationalFailureAsync(
                OperationalFailureArea.MerchantResolution,
                OperationalFailureSeverity.Info,
                "merchant_revalidation_deferred",
                $"merchant_revalidation_deferred:{merchant.Id:N}",
                merchant.Id.ToString("N"),
                $"merchant resolved via {resolutionSource}; revalidation deferred due to interval guard",
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ExecuteMerchantRevalidationAsync(
            merchant,
            revalidationReasons,
            resolutionSource,
            nowUtc,
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ResolveRevalidationReasonsAsync(
        Merchant merchant,
        DateTime nowUtc,
        string resolutionSource,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var isStale = merchant.NextValidationDueUtc.HasValue && merchant.NextValidationDueUtc.Value <= nowUtc;
        if (isStale)
        {
            reasons.Add("stale_validation_due");
        }

        if (merchant.MerchantStatus == MerchantStatus.LowConfidence)
        {
            reasons.Add("low_confidence_status");
        }

        if (resolutionSource is "fuzzy_alias" or "family_match")
        {
            reasons.Add("non_exact_resolution_touch");
        }

        var conflictLookbackUtc = nowUtc.AddDays(-Math.Max(1, _resilienceOptions.RevalidationAliasConflictLookbackDays));
        var hasConflictPressure = await dbContext.MerchantAliasConflicts
            .AsNoTracking()
            .AnyAsync(
                x => x.Status == MerchantAliasConflictStatus.Open
                     && (x.ExistingMerchantId == merchant.Id || x.ProposedMerchantId == merchant.Id)
                     && x.LastSeenUtc >= conflictLookbackUtc,
                cancellationToken);
        if (hasConflictPressure)
        {
            reasons.Add("alias_conflict_activity");
        }

        var unresolvedThreshold = Math.Max(2, _resilienceOptions.RevalidationUnresolvedPressureThreshold);
        if (unresolvedThreshold > 0)
        {
            var merchantTokens = normalizer.Tokenize(merchant.NormalizedCanonicalName).Take(3).ToArray();
            if (merchantTokens.Length > 0)
            {
                var unresolvedCandidates = await dbContext.UnresolvedMerchants
                    .AsNoTracking()
                    .Where(x => x.OccurrenceCount >= unresolvedThreshold)
                    .Where(x => x.LastSeenUtc >= nowUtc.AddDays(-45))
                    .OrderByDescending(x => x.OccurrenceCount)
                    .Take(200)
                    .ToListAsync(cancellationToken);

                var unresolvedPressure = unresolvedCandidates.Any(candidate =>
                    merchantTokens.Any(token => candidate.NormalizedDescriptor.Contains(token, StringComparison.Ordinal)));
                if (unresolvedPressure)
                {
                    reasons.Add("related_unresolved_pressure");
                }
            }
        }

        return reasons;
    }

    private async Task ExecuteMerchantRevalidationAsync(
        Merchant merchant,
        IReadOnlyList<string> reasons,
        string resolutionSource,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var previousStatus = merchant.MerchantStatus;
        var revalidationDescriptor = BuildRevalidationDescriptor(merchant);
        var revalidationRequest = new MerchantResolutionRequest(
            RawDescriptor: revalidationDescriptor,
            UserId: null,
            ConnectionId: null,
            SyncRunId: Guid.NewGuid(),
            TransactionId: null,
            NormalizedTransactionId: null,
            TaxonomyDomainId: merchant.TopDomainCode,
            TaxonomyCategoryId: merchant.TopCategoryCode,
            TaxonomySubcategoryId: merchant.TopSubcategoryCode,
            DeterministicTerminal: false,
            DeterministicResultCode: "merchant_revalidation",
            ManualOverridePresent: false,
            Amount: -100m,
            DescriptorMerchantLike: true,
            TriggerSource: "merchant_revalidation",
            RunState: new MerchantResolutionRunState(Guid.NewGuid()));
        var normalizedRevalidationDescriptor = normalizer.Normalize(revalidationDescriptor);
        var domainPolicy = domainTriggerPolicyService.Evaluate(
            merchant.TopDomainCode.HasValue ? [merchant.TopDomainCode.Value] : [],
            normalizedRevalidationDescriptor);
        var revalidationGateDecision = await aiTriggerGateService.EvaluateAsync(
            new AITriggerGateInput(
                Request: revalidationRequest,
                MerchantKey: merchant.NormalizedMerchantKey,
                NormalizedDescriptor: normalizedRevalidationDescriptor,
                PolicyEvaluation: domainPolicy,
                DeterministicResolved: false,
                RegistryResolved: false,
                DescriptorMerchantLike: true,
                MerchantInvestigatedAtUtc: merchant.InvestigatedAtUtc,
                MerchantCooldownUntilUtc: merchant.InvestigationCooldownUntilUtc,
                UnresolvedCooldownUntilUtc: null,
                MerchantOccurrenceCount: 1,
                ExpectedValueScore: Math.Max(1d, _governanceOptions.ExpectedValueThreshold),
                QueuePosition: 1,
                QueueDepth: 1,
                QueueState: "revalidation=true;position=1;depth=1",
                BacklogState: "revalidation=true"),
            cancellationToken);
        if (!revalidationGateDecision.ShouldTriggerAI)
        {
            merchant.ValidationAttemptCount += 1;
            merchant.LastValidatedUtc = nowUtc;
            merchant.LastValidationResultCode = $"revalidation_gate_{MapSkipReason(revalidationGateDecision.SkipReason ?? AITriggerSkipReason.DomainPolicyDisallowsAI)}";
            merchant.NextValidationDueUtc = nowUtc.AddDays(Math.Max(7, _resilienceOptions.CautiousMerchantValidationDays));
            merchant.UpdatedUtc = nowUtc;

            dbContext.MerchantRevalidationRecords.Add(new MerchantRevalidationRecord
            {
                Id = Guid.NewGuid(),
                MerchantId = merchant.Id,
                AttemptedUtc = nowUtc,
                TriggerReason = string.Join(",", reasons),
                Outcome = MerchantRevalidationOutcome.Failed,
                DecisionCode = null,
                PreviousStatus = previousStatus,
                NewStatus = merchant.MerchantStatus,
                StatusChanged = false,
                AliasTrustChanges = 0,
                RequiresUnresolvedReview = false,
                ContradictionDetected = false,
                LeadingEvidenceSummary = $"Revalidation skipped by AI gate ({revalidationGateDecision.SkipReason}).",
                ResultCode = "revalidation_skipped_by_ai_gate",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    reasons,
                    gateSkipReason = revalidationGateDecision.SkipReason?.ToString(),
                    budgetState = revalidationGateDecision.BudgetState,
                    cooldownState = revalidationGateDecision.CooldownState
                })
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            await PersistAIDecisionLogAsync(
                revalidationRequest,
                normalizedRevalidationDescriptor,
                domainPolicy,
                deterministicResult: "merchant_revalidation",
                registryResult: "registry_revalidation",
                revalidationGateDecision,
                MerchantResolutionFinalState.Unresolved,
                modelUsed: null,
                cancellationToken);
            return;
        }

        MerchantInvestigationResult investigationResult;
        MerchantAcceptanceDecision decision;

        try
        {
            investigationResult = await investigationService.InvestigateAsync(
                new MerchantInvestigationRequest(
                    RawDescriptor: revalidationDescriptor,
                    NormalizedDescriptor: normalizer.Normalize(revalidationDescriptor),
                    TriggerSource: "merchant_revalidation"),
                cancellationToken);
            decision = acceptancePolicy.Evaluate(investigationResult);
        }
        catch (Exception ex)
        {
            merchant.ValidationAttemptCount += 1;
            merchant.LastValidatedUtc = nowUtc;
            merchant.LastValidationResultCode = "revalidation_investigation_failed";
            merchant.NextValidationDueUtc = nowUtc.AddDays(Math.Max(7, _resilienceOptions.CautiousMerchantValidationDays));
            merchant.UpdatedUtc = nowUtc;

            dbContext.MerchantRevalidationRecords.Add(new MerchantRevalidationRecord
            {
                Id = Guid.NewGuid(),
                MerchantId = merchant.Id,
                AttemptedUtc = nowUtc,
                TriggerReason = string.Join(",", reasons),
                Outcome = MerchantRevalidationOutcome.Failed,
                DecisionCode = null,
                PreviousStatus = previousStatus,
                NewStatus = merchant.MerchantStatus,
                StatusChanged = false,
                AliasTrustChanges = 0,
                RequiresUnresolvedReview = true,
                ContradictionDetected = false,
                LeadingEvidenceSummary = ex.Message,
                ResultCode = "revalidation_investigation_failed",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    reasons,
                    resolutionSource,
                    exception = ex.GetType().Name
                })
            });

            await RecordOperationalFailureAsync(
                OperationalFailureArea.MerchantResolution,
                OperationalFailureSeverity.Error,
                "merchant_revalidation_failed",
                $"merchant_revalidation_failed:{merchant.Id:N}",
                merchant.Id.ToString("N"),
                ex.Message,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var outcome = ResolveRevalidationOutcome(previousStatus, decision.DecisionType);
        var requiresUnresolvedReview = decision.DecisionType is MerchantAcceptanceDecisionType.Unresolved
            or MerchantAcceptanceDecisionType.LowConfidence
            or MerchantAcceptanceDecisionType.Rejected;
        var contradictionDetected = decision.ReasonCodes.Contains("contradictory_evidence", StringComparer.OrdinalIgnoreCase);
        var sourceRiskDetected = decision.ReasonCodes.Any(code =>
            code is "weak_source_trust_profile"
                or "domain_name_mismatch_risk"
                or "suspicious_identity_signal"
                or "generic_merchant_name_risk"
                or "single_source_overconfidence_risk");
        var aliasTrustChanges = await ApplyRevalidationAliasSafetyAsync(
            merchant.Id,
            decision.DecisionType,
            sourceRiskDetected,
            nowUtc,
            cancellationToken);

        merchant.ValidationAttemptCount += 1;
        merchant.LastValidatedUtc = nowUtc;
        merchant.MerchantStatus = outcome.NewStatus;
        merchant.LastValidationResultCode = outcome.ResultCode;
        merchant.NextValidationDueUtc = nowUtc.AddDays(ResolveValidationDueDays(merchant.MerchantStatus, decision.DecisionType));
        merchant.UpdatedUtc = nowUtc;

        if (requiresUnresolvedReview || contradictionDetected || sourceRiskDetected)
        {
            dbContext.MerchantEvidence.Add(new MerchantEvidence
            {
                Id = Guid.NewGuid(),
                MerchantId = merchant.Id,
                EvidenceType = MerchantEvidenceType.Deterministic,
                EvidenceSummary = BuildTrustEvidenceSummary(decision, requiresUnresolvedReview, sourceRiskDetected),
                Confidence = Math.Clamp(decision.Confidence, 0d, 1d),
                SourceReference = $"revalidation:{string.Join(",", reasons)}",
                CapturedUtc = nowUtc
            });
        }

        dbContext.MerchantRevalidationRecords.Add(new MerchantRevalidationRecord
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            AttemptedUtc = nowUtc,
            TriggerReason = string.Join(",", reasons),
            Outcome = outcome.Outcome,
            DecisionCode = decision.DecisionType.ToString(),
            PreviousStatus = previousStatus,
            NewStatus = outcome.NewStatus,
            StatusChanged = previousStatus != outcome.NewStatus,
            AliasTrustChanges = aliasTrustChanges,
            RequiresUnresolvedReview = requiresUnresolvedReview,
            ContradictionDetected = contradictionDetected,
            LeadingEvidenceSummary = investigationResult.Evidence.FirstOrDefault()?.EvidenceSummary
                                     ?? decision.SelectedCandidate?.WhyItMayMatch
                                     ?? investigationResult.FailureReason,
            ResultCode = outcome.ResultCode,
            DetailsJson = JsonSerializer.Serialize(new
            {
                reasons,
                decision = decision.DecisionType.ToString(),
                confidence = decision.Confidence,
                reasonCodes = decision.ReasonCodes,
                recommendation = investigationResult.Recommendation.ToString(),
                sourceRiskDetected,
                resolutionSource
            })
        });

        if (requiresUnresolvedReview || contradictionDetected || sourceRiskDetected || aliasTrustChanges > 0)
        {
            await RecordOperationalFailureAsync(
                OperationalFailureArea.MerchantResolution,
                contradictionDetected ? OperationalFailureSeverity.Warning : OperationalFailureSeverity.Info,
                "merchant_revalidation_signal",
                $"merchant_revalidation_signal:{merchant.Id:N}:{outcome.ResultCode}",
                merchant.Id.ToString("N"),
                $"Revalidation outcome={outcome.ResultCode} reasonCodes={string.Join(",", decision.ReasonCodes)} aliasChanges={aliasTrustChanges}",
                cancellationToken);
        }

        logger.LogInformation(
            "Merchant revalidation executed merchantId={MerchantId} previousStatus={PreviousStatus} newStatus={NewStatus} outcome={Outcome} decision={Decision} reasons={Reasons} requiresUnresolvedReview={RequiresUnresolvedReview} aliasChanges={AliasChanges}",
            merchant.Id,
            previousStatus,
            outcome.NewStatus,
            outcome.Outcome,
            decision.DecisionType,
            string.Join(",", reasons),
            requiresUnresolvedReview,
            aliasTrustChanges);

        merchant.InvestigationModel = ResolveInvestigationModelName();
        merchant.InvestigatedAtUtc = nowUtc;
        merchant.LastUsedAtUtc = nowUtc;
        merchant.InvestigationCooldownUntilUtc = nowUtc.AddDays(Math.Max(1, _governanceOptions.MerchantInvestigationCooldownDays));
        if (decision.DecisionType is MerchantAcceptanceDecisionType.Rejected or MerchantAcceptanceDecisionType.LowConfidence)
        {
            merchant.FailureCount += 1;
            merchant.LastFailureUtc = nowUtc;
            merchant.InvestigationCooldownUntilUtc = decision.DecisionType == MerchantAcceptanceDecisionType.Rejected
                ? nowUtc.AddHours(Math.Max(1, _governanceOptions.FailureCooldownHours))
                : nowUtc.AddHours(Math.Max(1, _governanceOptions.LowConfidenceCooldownHours));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await PersistAIDecisionLogAsync(
            revalidationRequest,
            normalizedRevalidationDescriptor,
            domainPolicy,
            deterministicResult: "merchant_revalidation",
            registryResult: "registry_revalidation",
            revalidationGateDecision,
            MerchantResolutionFinalState.AIResolvedTerminal,
            modelUsed: ResolveInvestigationModelName(),
            cancellationToken);
    }

    private async Task<int> ApplyRevalidationAliasSafetyAsync(
        Guid merchantId,
        MerchantAcceptanceDecisionType decisionType,
        bool sourceRiskDetected,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var aliases = await dbContext.MerchantAliases
            .Where(x => x.MerchantId == merchantId && x.IsActive)
            .ToListAsync(cancellationToken);

        var changes = 0;
        foreach (var alias in aliases)
        {
            var isBroadDangerous = IsBroadDangerousAliasText(alias.NormalizedAliasText);
            var lowConfidenceAlias = alias.Confidence < 0.70d;

            if (decisionType == MerchantAcceptanceDecisionType.AcceptedTrusted && !sourceRiskDetected)
            {
                if (alias.TrustLevel == MerchantAliasTrustLevel.Observed && alias.Confidence >= 0.88d && !isBroadDangerous)
                {
                    alias.TrustLevel = MerchantAliasTrustLevel.Cautious;
                    alias.LifecycleReason = "revalidation_promoted_alias_trust";
                    alias.LastSeenUtc = nowUtc;
                    changes += 1;
                }

                continue;
            }

            if (isBroadDangerous || lowConfidenceAlias || sourceRiskDetected)
            {
                alias.IsActive = false;
                alias.TrustLevel = MerchantAliasTrustLevel.Rejected;
                alias.SupersededUtc = nowUtc;
                alias.LifecycleReason = "revalidation_risky_alias_deactivated";
                alias.LastSeenUtc = nowUtc;
                changes += 1;
                continue;
            }

            if (alias.TrustLevel == MerchantAliasTrustLevel.Trusted)
            {
                alias.TrustLevel = MerchantAliasTrustLevel.Cautious;
                alias.LifecycleReason = "revalidation_trust_downgraded";
                alias.LastSeenUtc = nowUtc;
                changes += 1;
            }
        }

        return changes;
    }

    private static (MerchantRevalidationOutcome Outcome, MerchantStatus NewStatus, string ResultCode) ResolveRevalidationOutcome(
        MerchantStatus previousStatus,
        MerchantAcceptanceDecisionType decisionType)
    {
        return decisionType switch
        {
            MerchantAcceptanceDecisionType.AcceptedTrusted when previousStatus == MerchantStatus.LowConfidence
                => (MerchantRevalidationOutcome.PromotedToTrusted, MerchantStatus.Active, "promote_cautious_to_trusted"),
            MerchantAcceptanceDecisionType.AcceptedTrusted
                => (MerchantRevalidationOutcome.KeepTrusted, MerchantStatus.Active, "keep_trusted"),
            MerchantAcceptanceDecisionType.AcceptedCautious when previousStatus == MerchantStatus.Active
                => (MerchantRevalidationOutcome.DowngradedToCautious, MerchantStatus.LowConfidence, "downgrade_to_cautious"),
            MerchantAcceptanceDecisionType.AcceptedCautious
                => (MerchantRevalidationOutcome.KeepCautious, MerchantStatus.LowConfidence, "keep_cautious"),
            MerchantAcceptanceDecisionType.LowConfidence
                => (MerchantRevalidationOutcome.MarkedForUnresolvedReview, MerchantStatus.LowConfidence, "mark_for_unresolved_review"),
            MerchantAcceptanceDecisionType.Unresolved
                => (MerchantRevalidationOutcome.MarkedForUnresolvedReview, MerchantStatus.LowConfidence, "mark_for_unresolved_review"),
            MerchantAcceptanceDecisionType.Rejected
                => (MerchantRevalidationOutcome.MarkedForUnresolvedReview, MerchantStatus.LowConfidence, "mark_for_unresolved_review"),
            _ => (MerchantRevalidationOutcome.Failed, previousStatus, "revalidation_failed")
        };
    }

    private static string BuildTrustEvidenceSummary(
        MerchantAcceptanceDecision decision,
        bool requiresUnresolvedReview,
        bool sourceRiskDetected)
    {
        var reasons = decision.ReasonCodes.Count == 0
            ? "no_reason_codes"
            : string.Join(",", decision.ReasonCodes);
        if (requiresUnresolvedReview && sourceRiskDetected)
        {
            return $"Revalidation flagged unresolved-review and source trust risk. reasons={reasons}";
        }

        if (requiresUnresolvedReview)
        {
            return $"Revalidation flagged unresolved review. reasons={reasons}";
        }

        if (sourceRiskDetected)
        {
            return $"Revalidation flagged source trust mismatch. reasons={reasons}";
        }

        return $"Revalidation completed with caution. reasons={reasons}";
    }

    private static string BuildRevalidationDescriptor(Merchant merchant)
    {
        var descriptorParts = new List<string>(3) { merchant.CanonicalName };
        if (!string.IsNullOrWhiteSpace(merchant.DisplayName)
            && !merchant.DisplayName.Equals(merchant.CanonicalName, StringComparison.OrdinalIgnoreCase))
        {
            descriptorParts.Add(merchant.DisplayName);
        }

        if (!string.IsNullOrWhiteSpace(merchant.OfficialWebsite)
            && Uri.TryCreate(merchant.OfficialWebsite, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            descriptorParts.Add(uri.Host);
        }

        return string.Join(' ', descriptorParts);
    }

    private async Task RecordOperationalFailureAsync(
        OperationalFailureArea area,
        OperationalFailureSeverity severity,
        string failureType,
        string fingerprint,
        string? subjectKey,
        string? message,
        CancellationToken cancellationToken)
    {
        if (_failureRecorder is null)
        {
            return;
        }

        await _failureRecorder.RecordAsync(
            new OperationalFailureRecordInput(
                Area: area,
                Severity: severity,
                FailureType: failureType,
                Fingerprint: fingerprint,
                CorrelationId: null,
                SubjectKey: subjectKey,
                FailureMessage: message,
                DetailsJson: null),
            cancellationToken);
    }

    private IReadOnlyCollection<int> CollectDomainCandidates(MerchantResolutionRequest request)
    {
        var candidates = new List<int>(2);
        if (request.TaxonomyDomainId.HasValue && request.TaxonomyDomainId.Value > 0)
        {
            candidates.Add(request.TaxonomyDomainId.Value);
        }

        return candidates;
    }

    private static bool IsMerchantLikeDescriptor(string normalizedDescriptor)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescriptor))
        {
            return false;
        }

        if (normalizedDescriptor.Contains("transfer", StringComparison.OrdinalIgnoreCase)
            || normalizedDescriptor.Contains("internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var letterCount = normalizedDescriptor.Count(char.IsLetter);
        var digitCount = normalizedDescriptor.Count(char.IsDigit);
        return letterCount >= 3 && digitCount <= Math.Max(8, normalizedDescriptor.Length / 2);
    }

    private static string MapSkipReason(AITriggerSkipReason reason)
    {
        return reason switch
        {
            AITriggerSkipReason.DeterministicTerminal => "deterministic_terminal",
            AITriggerSkipReason.RegistryResolved => "registry_resolved",
            AITriggerSkipReason.DomainPolicyDisallowsAI => "domain_policy_disallows_ai",
            AITriggerSkipReason.DescriptorNotMerchantLike => "descriptor_not_merchant_like",
            AITriggerSkipReason.MerchantRecentlyInvestigated => "merchant_recently_investigated",
            AITriggerSkipReason.MerchantOnCooldown => "merchant_on_cooldown",
            AITriggerSkipReason.RunBudgetExceeded => "run_budget_exceeded",
            AITriggerSkipReason.DailyBudgetExceeded => "daily_budget_exceeded",
            AITriggerSkipReason.DuplicateMerchantInRun => "duplicate_merchant_in_run",
            AITriggerSkipReason.ExpectedValueTooLow => "expected_value_too_low",
            AITriggerSkipReason.ManualOverridePresent => "manual_override_present",
            AITriggerSkipReason.UserConfirmationPreferred => "user_confirmation_preferred",
            _ => "ai_gate_denied"
        };
    }

    private async Task PersistAIDecisionLogAsync(
        MerchantResolutionRequest request,
        string normalizedDescriptor,
        DomainTriggerPolicyEvaluation domainPolicy,
        string deterministicResult,
        string registryResult,
        AITriggerGateDecision gateDecision,
        MerchantResolutionFinalState finalState,
        string? modelUsed,
        CancellationToken cancellationToken)
    {
        var skipReason = gateDecision.SkipReason?.ToString() ?? "None";
        var domainCandidatesText = domainPolicy.DomainCandidates.Count == 0
            ? "none"
            : string.Join(",", domainPolicy.DomainCandidates);
        var combinedBudgetState = string.IsNullOrWhiteSpace(gateDecision.QueueState)
            ? gateDecision.BudgetState
            : $"{gateDecision.BudgetState};{gateDecision.QueueState}";

        logger.LogInformation(
            "[AI_DECISION] transactionId={TransactionId} normalizedTransactionId={NormalizedTransactionId} userId={UserId} connectionId={ConnectionId} syncRunId={SyncRunId} descriptor={Descriptor} normalizedDescriptor={NormalizedDescriptor} merchantKey={MerchantKey} domainCandidates={DomainCandidates} triggerMode={TriggerMode} deterministicResult={DeterministicResult} registryResult={RegistryResult} aiGateDecision={AIGateDecision} aiSkipReason={AISkipReason} budgetState={BudgetState} cooldownState={CooldownState} queueState={QueueState} modelUsed={ModelUsed} finalState={FinalState}",
            request.TransactionId,
            request.NormalizedTransactionId,
            request.UserId,
            request.ConnectionId,
            request.SyncRunId,
            request.RawDescriptor,
            normalizedDescriptor,
            normalizedDescriptor,
            domainCandidatesText,
            domainPolicy.TriggerMode,
            deterministicResult,
            registryResult,
            gateDecision.ShouldTriggerAI,
            skipReason,
            combinedBudgetState,
            gateDecision.CooldownState,
            gateDecision.QueueState,
            modelUsed ?? "none",
            finalState);

        dbContext.MerchantAIDecisionLogs.Add(new MerchantAIDecisionLog
        {
            Id = Guid.NewGuid(),
            TransactionId = request.TransactionId,
            NormalizedTransactionId = request.NormalizedTransactionId,
            UserId = request.UserId,
            ConnectionId = request.ConnectionId,
            SyncRunId = request.SyncRunId,
            Descriptor = normalizer.SanitizeForStorage(request.RawDescriptor),
            NormalizedDescriptor = normalizedDescriptor,
            MerchantKey = normalizedDescriptor,
            DomainCandidates = domainCandidatesText,
            TriggerMode = domainPolicy.TriggerMode.ToString(),
            DeterministicResult = deterministicResult,
            RegistryResult = registryResult,
            AIGateDecision = gateDecision.ShouldTriggerAI,
            AISkipReason = skipReason,
            BudgetState = combinedBudgetState,
            CooldownState = gateDecision.CooldownState,
            ModelUsed = modelUsed,
            FinalState = finalState.ToString(),
            AICallExecuted = gateDecision.ShouldTriggerAI,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string ResolveInvestigationModelName()
    {
        return string.IsNullOrWhiteSpace(_aiOptions.Routing.HeavyModelName)
            ? "unknown_model"
            : _aiOptions.Routing.HeavyModelName;
    }

    private static string? ExtractWebsiteDomain(string? website)
    {
        if (string.IsNullOrWhiteSpace(website))
        {
            return null;
        }

        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(uri.Host) ? null : uri.Host.ToLowerInvariant();
    }

    private static MerchantResolutionResult UnresolvedResult(string normalizedDescriptor, IReadOnlyList<string> reasonCodes)
    {
        return new MerchantResolutionResult(
            MerchantId: null,
            ResolutionConfidence: 0d,
            ResolutionType: MerchantResolutionType.None,
            MatchedAlias: null,
            IsResolved: false,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes: reasonCodes,
            FinalState: MerchantResolutionFinalState.Unresolved);
    }

    private static double ComputeJaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0d;
        }

        var intersection = left.Count(token => right.Contains(token));
        if (intersection == 0)
        {
            return 0d;
        }

        var union = left.Count + right.Count - intersection;
        return union <= 0 ? 0d : intersection / (double)union;
    }

    private static bool IsDangerousFamilyToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return DangerousFamilyRoots.Contains(token.Trim().ToLowerInvariant());
    }

    private static MerchantAliasType MapAliasType(string aliasType)
    {
        return Enum.TryParse<MerchantAliasType>(aliasType, ignoreCase: true, out var parsed)
            ? parsed
            : MerchantAliasType.BillingDescriptor;
    }

    private static bool IsSafeAliasSuggestion(MerchantInvestigationAliasSuggestion suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion.AliasText)
            || suggestion.AliasText.Length < 6
            || suggestion.Confidence < 0.80d)
        {
            return false;
        }

        if (!SafeAliasTypes.Contains(suggestion.AliasType))
        {
            return false;
        }

        return !IsBroadDangerousAliasText(suggestion.AliasText);
    }

    private static bool IsBroadDangerousAliasText(string aliasText)
    {
        var normalized = aliasText.Trim().ToLowerInvariant();
        if (DangerousFamilyRoots.Contains(normalized))
        {
            return true;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 1 && DangerousFamilyRoots.Contains(tokens[0]);
    }

    private sealed class PassiveMerchantInvestigationQueueService : IMerchantInvestigationQueueService
    {
        public Task<MerchantInvestigationQueueEvaluation> EvaluateAsync(
            MerchantInvestigationQueueEvaluationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repeated = request.UnresolvedMerchant.OccurrenceCount >= 2;
            var meaningfulSpend = Math.Abs(request.ResolutionRequest.Amount) >= 75m;
            var expectedValue = repeated || meaningfulSpend ? 1d : 0d;
            var backlog = new MerchantInvestigationBacklogMetrics(
                UnresolvedMerchantCount: 0,
                QueuedMerchantCount: 0,
                InvestigatedToday: 0,
                SkippedDueToBudgetToday: 0,
                SkippedDueToCooldownToday: 0);
            return Task.FromResult(
                new MerchantInvestigationQueueEvaluation(
                    PriorityScore: 1d,
                    ExpectedValueScore: expectedValue,
                    QueuePosition: 1,
                    QueueDepth: 1,
                    BacklogMetrics: backlog));
        }

        public Task<MerchantInvestigationLockResult> TryAcquireLockAsync(Guid unresolvedMerchantId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new MerchantInvestigationLockResult(
                    Acquired: true,
                    LockId: Guid.NewGuid(),
                    ExistingLockAcquiredUtc: null));
        }

        public Task ReleaseLockAsync(Guid unresolvedMerchantId, Guid lockId, bool markFailed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
