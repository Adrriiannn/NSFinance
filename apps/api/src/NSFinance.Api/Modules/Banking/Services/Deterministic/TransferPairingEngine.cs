using System.Text.Json;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class TransferPairingEngine
{
    private const int MinScoreForPairing = 8;
    private const int MinScoreForStrongDeferredCounterparty = 10;
    private const int MutualBestMinimumMargin = 2;
    private const int AmbiguityMaxScoreGap = 1;
    private const int MaxClusterRowsPerSide = 8;
    private const int ExternalCounterpartyPenalty = 6;
    private const int ExternalCounterpartyPenaltyWithSameUserOverride = 1;
    private const int SameUserUniverseOverrideScoreBoost = 8;

    public sealed record TransferPairingAnalysis(
        IReadOnlyDictionary<Guid, TransferPendingDecision> PendingDecisions,
        IReadOnlyDictionary<Guid, TransferPairDecision> ResolvedPairDecisions,
        int CandidateEdgeCount,
        int AmbiguousCount);

    public TransferPairingAnalysis AnalyzeUnpairedTransactions(
        IReadOnlyDictionary<Guid, DeterministicTransactionFeature> featuresById,
        IReadOnlySet<Guid> pairedTransactionIds)
    {
        var routingDecisionsById = BuildRoutingDecisions(featuresById.Values.ToList());
        var transferLikeFeatures = featuresById.Values
            .Where(feature =>
                routingDecisionsById.TryGetValue(feature.TransactionId, out var routing)
                && routing.IncludeInTransferMatching)
            .ToList();
        if (transferLikeFeatures.Count == 0)
        {
            return new TransferPairingAnalysis(
                new Dictionary<Guid, TransferPendingDecision>(),
                new Dictionary<Guid, TransferPairDecision>(),
                0,
                0);
        }

        var groupStatsByAmountCurrencyDay = transferLikeFeatures
            .GroupBy(CreateAmountCurrencyDayKey)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    OutflowCount = group.Count(x => x.IsOutflow),
                    InflowCount = group.Count(x => x.IsInflow),
                    Total = group.Count()
                },
                StringComparer.Ordinal);

        var candidatesBySourceId = new Dictionary<Guid, List<CandidateEdge>>(transferLikeFeatures.Count);
        var candidateEdgeCount = 0;

        foreach (var source in transferLikeFeatures)
        {
            if (pairedTransactionIds.Contains(source.TransactionId))
            {
                continue;
            }

            var amountDayKey = CreateAmountCurrencyDayKey(source);
            groupStatsByAmountCurrencyDay.TryGetValue(amountDayKey, out var groupStats);
            var duplicateClusterMember = groupStats is not null && groupStats.OutflowCount > 1 && groupStats.InflowCount > 1;
            var duplicateClusterSize = groupStats?.Total ?? 0;

            var candidates = transferLikeFeatures
                .Where(candidate =>
                    candidate.TransactionId != source.TransactionId
                    && !pairedTransactionIds.Contains(candidate.TransactionId)
                    && candidate.FinancialAccountId != source.FinancialAccountId
                    && candidate.Currency == source.Currency
                    && candidate.AbsoluteAmount == source.AbsoluteAmount
                    && candidate.IsOutflow != source.IsOutflow
                    && Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours) <= DeterministicCategorizationConstants.TransferCandidateWindowHours)
                .Select(candidate => BuildCandidateEdge(
                    source,
                    candidate,
                    duplicateClusterMember,
                    duplicateClusterSize,
                    routingDecisionsById[source.TransactionId],
                    routingDecisionsById[candidate.TransactionId]))
                .OrderByDescending(x => x.ReferenceConfidenceRank)
                .ThenByDescending(x => x.ReferenceOverlapScore)
                .ThenByDescending(x => x.HighConfidenceReferenceOverlap)
                .ThenByDescending(x => x.ProviderSpecificReferenceOverlap)
                .ThenByDescending(x => x.AccountReferenceOverlap)
                .ThenByDescending(x => x.PaymentMarkerOverlap)
                .ThenByDescending(x => x.MediumConfidenceOverlap)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.WindowHours)
                .ThenBy(x => x.StableOrderDistance)
                .ThenBy(x => x.Candidate.StableSequence)
                .ThenBy(x => x.Candidate.BookedAtUtc)
                .ThenBy(x => x.Candidate.TransactionId)
                .ToList();

            candidatesBySourceId[source.TransactionId] = candidates;
            candidateEdgeCount += candidates.Count;
        }

        var resolvedPairDecisions = ResolvePairs(
            transferLikeFeatures,
            candidatesBySourceId,
            pairedTransactionIds);
        var pairedByDeterministicResolution = resolvedPairDecisions.Keys.ToHashSet();

        var pending = new Dictionary<Guid, TransferPendingDecision>();
        var ambiguousCount = 0;

        foreach (var feature in transferLikeFeatures)
        {
            if (pairedTransactionIds.Contains(feature.TransactionId)
                || pairedByDeterministicResolution.Contains(feature.TransactionId))
            {
                continue;
            }

            if (!candidatesBySourceId.TryGetValue(feature.TransactionId, out var allCandidates))
            {
                continue;
            }

            var candidates = allCandidates
                .Where(x => !pairedByDeterministicResolution.Contains(x.Candidate.TransactionId))
                .ToList();
            var duplicateClusterMember = candidates.FirstOrDefault()?.IsDuplicateClusterMember ?? false;
            var duplicateClusterSize = candidates.FirstOrDefault()?.DuplicateClusterSize ?? 0;
            var topCandidate = candidates.FirstOrDefault();

            if (candidates.Count == 0)
            {
                var routingDecision = routingDecisionsById[feature.TransactionId];
                var hasExplicitCounterpartyExpectation = HasExplicitCounterpartyExpectation(
                    feature,
                    duplicateClusterMember,
                    routingDecision.SameUserCandidateUniverseOverrideApplied);
                var status = feature.IsPending
                    ? DeterministicClassificationStatus.DeferredWaitingForMoreContext
                    : feature.HasCounterpartyAccounts && hasExplicitCounterpartyExpectation
                        ? DeterministicClassificationStatus.DeferredWaitingForCounterparty
                        : DeterministicClassificationStatus.EvaluatedNoMatchingRule;
                var reasonCode = status switch
                {
                    DeterministicClassificationStatus.DeferredWaitingForMoreContext => DeterministicClassificationReasonCodes.DeferredPendingBookedContext,
                    DeterministicClassificationStatus.DeferredWaitingForCounterparty => DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
                    _ => DeterministicClassificationReasonCodes.TransferRejectedNoCounterpart
                };
                var retryEligible = status is DeterministicClassificationStatus.DeferredWaitingForCounterparty
                    or DeterministicClassificationStatus.DeferredWaitingForMoreContext;

                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    status,
                    reasonCode,
                    retryEligible,
                    CandidateFamily: "bank_account_transfer",
                    CandidateCount: 0,
                    TopCandidateTransactionId: null,
                    TopCandidateScore: null,
                    IsDuplicateClusterMember: duplicateClusterMember,
                    DuplicateClusterSize: duplicateClusterSize,
                    EvidenceJson: JsonSerializer.Serialize(new
                    {
                        family = "bank_account_transfer",
                        resolutionOutcome = "no_candidate",
                        providerCapabilities = BuildProviderCapabilitySummary(feature),
                        narrativeSignals = BuildNarrativeSignalSummary(feature),
                        hasExplicitCounterpartyExpectation,
                        candidateCount = 0,
                        duplicateClusterMember,
                        duplicateClusterSize,
                        timePrecisionMode = feature.ProviderTimestampPrecision.ToString().ToLowerInvariant(),
                        feature.HasTransferKeyword,
                        feature.HasProviderTransferHint,
                        feature.AccountHint,
                        feature.NearbySameAmountCount,
                        routingInitiallyBlockedExternalCounterpartyRisk = routingDecision.InitiallyBlockedByExternalCounterpartyRisk,
                        sameUserCandidateUniverseOverrideApplied = routingDecision.SameUserCandidateUniverseOverrideApplied,
                        hasPlausibleOppositeDirectionUniverse = routingDecision.HasPlausibleOppositeDirectionUniverse,
                        strongOppositeStructuredEvidencePresent = routingDecision.HasStrongOppositeStructuredEvidence,
                        sameUserCandidateUniverseSize = routingDecision.SameUserCandidateUniverseSize
                    }));
                continue;
            }

            if (duplicateClusterMember && candidates.Count > 1)
            {
                ambiguousCount++;
                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    DeterministicClassificationStatus.RejectedAmbiguousMatch,
                    DeterministicClassificationReasonCodes.TransferRejectedAmbiguousDuplicateCluster,
                    false,
                    CandidateFamily: "bank_account_transfer",
                    CandidateCount: candidates.Count,
                    TopCandidateTransactionId: topCandidate?.Candidate.TransactionId,
                    TopCandidateScore: topCandidate?.Score,
                    IsDuplicateClusterMember: true,
                    DuplicateClusterSize: duplicateClusterSize,
                    EvidenceJson: JsonSerializer.Serialize(new
                    {
                        family = "bank_account_transfer",
                        resolutionOutcome = "ambiguous_duplicate_cluster",
                        candidateCount = candidates.Count,
                        duplicateClusterSize,
                        topCandidateId = topCandidate?.Candidate.TransactionId,
                        topCandidateScore = topCandidate?.Score,
                        topCandidateReferenceConfidenceBand = topCandidate?.ReferenceConfidenceBand,
                        topCandidateReferenceOverlap = topCandidate?.ReferenceOverlapScore,
                        topCandidateWeakNamesOnlySupport = topCandidate?.NamesOnlyOverlapPenaltyApplied,
                        topCandidateTimePrecisionMode = topCandidate?.TimePrecisionMode,
                        stableOrderingUsed = false,
                        routingInitiallyBlockedExternalCounterpartyRisk = routingDecisionsById[feature.TransactionId].InitiallyBlockedByExternalCounterpartyRisk,
                        sameUserCandidateUniverseOverrideApplied = routingDecisionsById[feature.TransactionId].SameUserCandidateUniverseOverrideApplied
                    }));
                continue;
            }

            if (candidates.Count > 1)
            {
                var best = candidates[0];
                var next = candidates[1];
                if (MateriallyEquivalent(best, next) || (best.Score - next.Score) <= AmbiguityMaxScoreGap)
                {
                    ambiguousCount++;
                    pending[feature.TransactionId] = new TransferPendingDecision(
                        feature.TransactionId,
                        DeterministicClassificationStatus.RejectedAmbiguousMatch,
                        DeterministicClassificationReasonCodes.TransferRejectedAmbiguous,
                        false,
                        CandidateFamily: "bank_account_transfer",
                        CandidateCount: candidates.Count,
                        TopCandidateTransactionId: best.Candidate.TransactionId,
                        TopCandidateScore: best.Score,
                        IsDuplicateClusterMember: false,
                        DuplicateClusterSize: duplicateClusterSize,
                        EvidenceJson: JsonSerializer.Serialize(new
                        {
                            family = "bank_account_transfer",
                            resolutionOutcome = "ambiguous_candidate_scores",
                            candidateCount = candidates.Count,
                            bestScore = best.Score,
                            nextScore = next.Score,
                            bestReferenceOverlap = best.ReferenceOverlapScore,
                            nextReferenceOverlap = next.ReferenceOverlapScore,
                            bestReferenceConfidenceBand = best.ReferenceConfidenceBand,
                            nextReferenceConfidenceBand = next.ReferenceConfidenceBand,
                            bestWeakNamesOnlySupport = best.NamesOnlyOverlapPenaltyApplied,
                            nextWeakNamesOnlySupport = next.NamesOnlyOverlapPenaltyApplied,
                            bestTimePrecisionMode = best.TimePrecisionMode,
                            nextTimePrecisionMode = next.TimePrecisionMode,
                            routingInitiallyBlockedExternalCounterpartyRisk = routingDecisionsById[feature.TransactionId].InitiallyBlockedByExternalCounterpartyRisk,
                            sameUserCandidateUniverseOverrideApplied = routingDecisionsById[feature.TransactionId].SameUserCandidateUniverseOverrideApplied
                        }));
                    continue;
                }
            }

            var bestCandidate = candidates[0];
            var bestScoreStrongEnough = bestCandidate.Score >= MinScoreForStrongDeferredCounterparty;
            var routingDecisionForFeature = routingDecisionsById[feature.TransactionId];
            var explicitCounterpartyExpectation = HasExplicitCounterpartyExpectation(
                feature,
                duplicateClusterMember,
                routingDecisionForFeature.SameUserCandidateUniverseOverrideApplied);
            if (!feature.IsBooked || !bestCandidate.Candidate.IsBooked || feature.IsPending || bestCandidate.Candidate.IsPending)
            {
                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    DeterministicClassificationStatus.DeferredWaitingForMoreContext,
                    DeterministicClassificationReasonCodes.DeferredPendingBookedContext,
                    true,
                    CandidateFamily: "bank_account_transfer",
                    CandidateCount: candidates.Count,
                    TopCandidateTransactionId: bestCandidate.Candidate.TransactionId,
                    TopCandidateScore: bestCandidate.Score,
                    IsDuplicateClusterMember: duplicateClusterMember,
                    DuplicateClusterSize: duplicateClusterSize,
                    EvidenceJson: JsonSerializer.Serialize(new
                    {
                        family = "bank_account_transfer",
                        resolutionOutcome = "deferred_pending_context",
                        candidateId = bestCandidate.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = bestCandidate.Score,
                        referenceOverlap = bestCandidate.ReferenceOverlapScore,
                        timePrecisionMode = bestCandidate.TimePrecisionMode,
                        routingInitiallyBlockedExternalCounterpartyRisk = routingDecisionForFeature.InitiallyBlockedByExternalCounterpartyRisk,
                        sameUserCandidateUniverseOverrideApplied = routingDecisionForFeature.SameUserCandidateUniverseOverrideApplied
                    }));
                continue;
            }

            if (!bestScoreStrongEnough || !explicitCounterpartyExpectation)
            {
                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    DeterministicClassificationStatus.EvaluatedNoMatchingRule,
                    DeterministicClassificationReasonCodes.TransferRejectedNoCounterpart,
                    false,
                    CandidateFamily: "bank_account_transfer",
                    CandidateCount: candidates.Count,
                    TopCandidateTransactionId: bestCandidate.Candidate.TransactionId,
                    TopCandidateScore: bestCandidate.Score,
                    IsDuplicateClusterMember: duplicateClusterMember,
                    DuplicateClusterSize: duplicateClusterSize,
                    EvidenceJson: JsonSerializer.Serialize(new
                    {
                        family = "bank_account_transfer",
                        resolutionOutcome = "insufficient_pairing_confidence",
                        candidateId = bestCandidate.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = bestCandidate.Score,
                        bestReferenceOverlap = bestCandidate.ReferenceOverlapScore,
                        explicitCounterpartyExpectation,
                        duplicateClusterMember,
                        routingInitiallyBlockedExternalCounterpartyRisk = routingDecisionForFeature.InitiallyBlockedByExternalCounterpartyRisk,
                        sameUserCandidateUniverseOverrideApplied = routingDecisionForFeature.SameUserCandidateUniverseOverrideApplied
                    }));
                continue;
            }

            pending[feature.TransactionId] = new TransferPendingDecision(
                feature.TransactionId,
                DeterministicClassificationStatus.DeferredWaitingForCounterparty,
                DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
                true,
                CandidateFamily: "bank_account_transfer",
                CandidateCount: candidates.Count,
                TopCandidateTransactionId: bestCandidate.Candidate.TransactionId,
                TopCandidateScore: bestCandidate.Score,
                IsDuplicateClusterMember: duplicateClusterMember,
                DuplicateClusterSize: duplicateClusterSize,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "bank_account_transfer",
                    resolutionOutcome = "deferred_missing_counterparty",
                    candidateId = bestCandidate.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = bestCandidate.Score,
                        bestReferenceOverlap = bestCandidate.ReferenceOverlapScore,
                        bestTimePrecisionMode = bestCandidate.TimePrecisionMode,
                        routingInitiallyBlockedExternalCounterpartyRisk = routingDecisionForFeature.InitiallyBlockedByExternalCounterpartyRisk,
                        sameUserCandidateUniverseOverrideApplied = routingDecisionForFeature.SameUserCandidateUniverseOverrideApplied
                }));
        }

        return new TransferPairingAnalysis(
            pending,
            resolvedPairDecisions,
            candidateEdgeCount,
            ambiguousCount);
    }

    private static Dictionary<Guid, TransferPairDecision> ResolvePairs(
        IReadOnlyList<DeterministicTransactionFeature> transferLikeFeatures,
        IReadOnlyDictionary<Guid, List<CandidateEdge>> candidatesBySourceId,
        IReadOnlySet<Guid> prePaired)
    {
        var resolved = new Dictionary<Guid, TransferPairDecision>();
        var claimed = prePaired.ToHashSet();

        foreach (var source in transferLikeFeatures
                     .Where(x => x.IsOutflow)
                     .OrderBy(x => x.StableSequence)
                     .ThenBy(x => x.BookedAtUtc)
                     .ThenBy(x => x.TransactionId))
        {
            if (claimed.Contains(source.TransactionId)
                || !candidatesBySourceId.TryGetValue(source.TransactionId, out var sourceCandidates))
            {
                continue;
            }

            var availableSourceCandidates = sourceCandidates
                .Where(x => !claimed.Contains(x.Candidate.TransactionId))
                .ToList();
            if (availableSourceCandidates.Count != 1)
            {
                continue;
            }

            var candidate = availableSourceCandidates[0];
            if (candidate.Score < MinScoreForPairing
                || !source.IsBooked
                || !candidate.Candidate.IsBooked
                || claimed.Contains(candidate.Candidate.TransactionId))
            {
                continue;
            }

            if (!candidatesBySourceId.TryGetValue(candidate.Candidate.TransactionId, out var reciprocalCandidates))
            {
                continue;
            }

            var reciprocalAvailable = reciprocalCandidates
                .Where(x => !claimed.Contains(x.Candidate.TransactionId))
                .ToList();
            if (reciprocalAvailable.Count != 1 || reciprocalAvailable[0].Candidate.TransactionId != source.TransactionId)
            {
                continue;
            }

            ApplyPair(
                resolved,
                claimed,
                candidate,
                "bank_transfer.strict_unique_pair_v5",
                DeterministicClassificationReasonCodes.TransferPairStrictMatch,
                pass: "strict_unique_pair",
                stableOrderingUsed: false,
                clusterSize: candidate.DuplicateClusterSize,
                tieBreakReason: "strict_unique_pair",
                clusterTimePrecisionMode: candidate.TimePrecisionMode,
                clusterCandidateEdgeCount: 1,
                clusterAssignmentCount: 1,
                namesOnlyWeakSupport: candidate.NamesOnlyOverlapPenaltyApplied);
        }

        foreach (var source in transferLikeFeatures
                     .Where(x => x.IsOutflow)
                     .OrderBy(x => x.StableSequence)
                     .ThenBy(x => x.BookedAtUtc)
                     .ThenBy(x => x.TransactionId))
        {
            if (claimed.Contains(source.TransactionId)
                || !candidatesBySourceId.TryGetValue(source.TransactionId, out var sourceCandidates))
            {
                continue;
            }

            var availableSourceCandidates = sourceCandidates
                .Where(x => !claimed.Contains(x.Candidate.TransactionId))
                .ToList();
            if (availableSourceCandidates.Count == 0)
            {
                continue;
            }

            var best = availableSourceCandidates[0];
            if (best.IsDuplicateClusterMember)
            {
                // Duplicate clusters are resolved in the dedicated one-to-one cluster pass so
                // reference-confidence ranking and fallback rules are applied consistently.
                continue;
            }

            if (!candidatesBySourceId.TryGetValue(best.Candidate.TransactionId, out var reciprocalCandidates))
            {
                continue;
            }

            var reciprocalAvailable = reciprocalCandidates
                .Where(x => !claimed.Contains(x.Candidate.TransactionId))
                .ToList();
            if (reciprocalAvailable.Count == 0)
            {
                continue;
            }

            var reciprocalBest = reciprocalAvailable[0];
            if (reciprocalBest.Candidate.TransactionId != source.TransactionId)
            {
                continue;
            }

            var sourceMargin = best.Score - (availableSourceCandidates.Count > 1
                ? availableSourceCandidates[1].Score
                : best.Score - MutualBestMinimumMargin);
            var reciprocalMargin = reciprocalBest.Score - (reciprocalAvailable.Count > 1
                ? reciprocalAvailable[1].Score
                : reciprocalBest.Score - MutualBestMinimumMargin);
            if (best.Score < MinScoreForStrongDeferredCounterparty
                || sourceMargin < MutualBestMinimumMargin
                || reciprocalMargin < MutualBestMinimumMargin
                || !source.IsBooked
                || !best.Candidate.IsBooked
                || claimed.Contains(best.Candidate.TransactionId))
            {
                continue;
            }

            ApplyPair(
                resolved,
                claimed,
                best,
                "bank_transfer.mutual_best_pair_v5",
                DeterministicClassificationReasonCodes.TransferPairMutualBest,
                pass: "mutual_best_pair",
                stableOrderingUsed: false,
                clusterSize: best.DuplicateClusterSize,
                tieBreakReason: "mutual_best_pair",
                clusterTimePrecisionMode: best.TimePrecisionMode,
                clusterCandidateEdgeCount: 1,
                clusterAssignmentCount: 1,
                namesOnlyWeakSupport: best.NamesOnlyOverlapPenaltyApplied);
        }

        var unresolved = transferLikeFeatures
            .Where(x => !claimed.Contains(x.TransactionId))
            .ToList();
        var clusterGroups = unresolved
            .GroupBy(CreateAmountCurrencyDayKey)
            .Where(group => group.Count(x => x.IsOutflow) > 1 && group.Count(x => x.IsInflow) > 1)
            .ToList();

        foreach (var clusterGroup in clusterGroups)
        {
            var outflows = clusterGroup
                .Where(x => x.IsOutflow && !claimed.Contains(x.TransactionId))
                .OrderBy(x => x.StableSequence)
                .ThenBy(x => x.BookedAtUtc)
                .ThenBy(x => x.TransactionId)
                .ToList();
            var inflows = clusterGroup
                .Where(x => x.IsInflow && !claimed.Contains(x.TransactionId))
                .OrderBy(x => x.StableSequence)
                .ThenBy(x => x.BookedAtUtc)
                .ThenBy(x => x.TransactionId)
                .ToList();

            if (outflows.Count == 0
                || inflows.Count == 0
                || outflows.Count != inflows.Count
                || outflows.Count > MaxClusterRowsPerSide)
            {
                continue;
            }

            var inflowsById = inflows.ToDictionary(x => x.TransactionId, x => x);
            var candidatesByOutflowId = new Dictionary<Guid, List<CandidateEdge>>();
            var clusterRejected = false;

            foreach (var outflow in outflows)
            {
                if (!candidatesBySourceId.TryGetValue(outflow.TransactionId, out var sourceCandidates))
                {
                    clusterRejected = true;
                    break;
                }

                var filtered = sourceCandidates
                    .Where(edge => inflowsById.ContainsKey(edge.Candidate.TransactionId))
                    .Where(edge => edge.Score >= MinScoreForPairing)
                    .ToList();
                if (filtered.Count == 0)
                {
                    clusterRejected = true;
                    break;
                }

                candidatesByOutflowId[outflow.TransactionId] = filtered;
            }

            if (clusterRejected)
            {
                continue;
            }

            var assignments = BuildClusterAssignments(
                outflows,
                candidatesByOutflowId);
            if (assignments.Count == 0)
            {
                continue;
            }

            var bestCore = assignments
                .OrderByDescending(x => x.CoreScore.HighConfidenceReferenceOverlap)
                .ThenByDescending(x => x.CoreScore.ProviderSpecificReferenceOverlap)
                .ThenByDescending(x => x.CoreScore.AccountReferenceOverlap)
                .ThenByDescending(x => x.CoreScore.PaymentMarkerOverlap)
                .ThenByDescending(x => x.CoreScore.MediumConfidenceOverlap)
                .ThenByDescending(x => x.CoreScore.ReferenceOverlapScore)
                .ThenByDescending(x => x.CoreScore.TotalScore)
                .ThenBy(x => x.CoreScore.TotalWindowMinutes)
                .First();

            var contenders = assignments
                .Where(assignment => assignment.CoreScore == bestCore.CoreScore)
                .ToList();
            var clusterTimePrecisionMode = ResolveClusterTimePrecisionMode(contenders);
            var stableOrderingUsed = false;
            var tieBreakReason = ResolveClusterTieBreakReason(bestCore.CoreScore);
            ClusterAssignment chosen;
            if (contenders.Count == 1)
            {
                chosen = contenders[0];
            }
            else
            {
                var stableOrderFallbackAllowed = CanUseStableOrderFallback(
                    contenders,
                    clusterTimePrecisionMode);
                if (!stableOrderFallbackAllowed)
                {
                    continue;
                }

                stableOrderingUsed = true;
                tieBreakReason = "stable_order_fallback_after_reference_tie";
                chosen = contenders
                    .OrderBy(x => x.StableOrderDistanceSum)
                    .ThenBy(x => x.StableOrderCrossingPenalty)
                    .ThenBy(x => x.EdgeCount)
                    .ThenBy(x => string.Join("|", x.Edges.Select(edge => edge.Candidate.TransactionId.ToString("N"))), StringComparer.Ordinal)
                    .First();
            }

            foreach (var edge in chosen.Edges)
            {
                if (claimed.Contains(edge.Source.TransactionId) || claimed.Contains(edge.Candidate.TransactionId))
                {
                    clusterRejected = true;
                    break;
                }
            }

            if (clusterRejected)
            {
                continue;
            }

            var clusterCandidateEdgeCount = candidatesByOutflowId.Sum(entry => entry.Value.Count);
            var namesOnlyWeakSupport = chosen.Edges.All(edge => edge.NamesOnlyOverlapPenaltyApplied);
            foreach (var edge in chosen.Edges)
            {
                ApplyPair(
                    resolved,
                    claimed,
                    edge,
                    "bank_transfer.duplicate_cluster_one_to_one_v5",
                    DeterministicClassificationReasonCodes.TransferPairDuplicateCluster,
                    pass: "duplicate_cluster_one_to_one",
                    stableOrderingUsed,
                    clusterSize: outflows.Count + inflows.Count,
                    tieBreakReason,
                    clusterTimePrecisionMode,
                    clusterCandidateEdgeCount,
                    assignments.Count,
                    namesOnlyWeakSupport);
            }
        }

        return resolved;
    }

    private static List<ClusterAssignment> BuildClusterAssignments(
        IReadOnlyList<DeterministicTransactionFeature> outflows,
        IReadOnlyDictionary<Guid, List<CandidateEdge>> candidatesByOutflowId)
    {
        var assignments = new List<ClusterAssignment>();
        var usedInflows = new HashSet<Guid>();
        var selectedEdges = new List<CandidateEdge>();

        void Explore(
            int index,
            int totalScore,
            int highOverlap,
            int providerOverlap,
            int accountOverlap,
            int paymentOverlap,
            int mediumOverlap,
            int referenceOverlapScore,
            int windowMinutes,
            long stableDistance,
            int stableCrossingPenalty)
        {
            if (index >= outflows.Count)
            {
                assignments.Add(new ClusterAssignment(
                    selectedEdges.ToArray(),
                    selectedEdges.Count,
                    new CoreClusterScore(
                        totalScore,
                        highOverlap,
                        providerOverlap,
                        accountOverlap,
                        paymentOverlap,
                        mediumOverlap,
                        referenceOverlapScore,
                        windowMinutes),
                    stableDistance,
                    stableCrossingPenalty));
                return;
            }

            var outflow = outflows[index];
            if (!candidatesByOutflowId.TryGetValue(outflow.TransactionId, out var candidates))
            {
                return;
            }

            foreach (var edge in candidates)
            {
                var inflowId = edge.Candidate.TransactionId;
                if (!usedInflows.Add(inflowId))
                {
                    continue;
                }

                selectedEdges.Add(edge);
                var crossingPenalty = edge.Candidate.StableSequence < edge.Source.StableSequence ? 1 : 0;
                Explore(
                    index + 1,
                    totalScore + edge.Score,
                    highOverlap + edge.HighConfidenceReferenceOverlap,
                    providerOverlap + edge.ProviderSpecificReferenceOverlap,
                    accountOverlap + edge.AccountReferenceOverlap,
                    paymentOverlap + edge.PaymentMarkerOverlap,
                    mediumOverlap + edge.MediumConfidenceOverlap,
                    referenceOverlapScore + edge.ReferenceOverlapScore,
                    windowMinutes + (int)Math.Round(edge.WindowHours * 60d, MidpointRounding.AwayFromZero),
                    stableDistance + edge.StableOrderDistance,
                    stableCrossingPenalty + crossingPenalty);
                selectedEdges.RemoveAt(selectedEdges.Count - 1);
                usedInflows.Remove(inflowId);
            }
        }

        Explore(
            0,
            totalScore: 0,
            highOverlap: 0,
            providerOverlap: 0,
            accountOverlap: 0,
            paymentOverlap: 0,
            mediumOverlap: 0,
            referenceOverlapScore: 0,
            windowMinutes: 0,
            stableDistance: 0,
            stableCrossingPenalty: 0);

        return assignments;
    }

    private static void ApplyPair(
        Dictionary<Guid, TransferPairDecision> resolved,
        HashSet<Guid> claimed,
        CandidateEdge edge,
        string ruleKey,
        string reasonCode,
        string pass,
        bool stableOrderingUsed,
        int clusterSize,
        string tieBreakReason,
        string clusterTimePrecisionMode,
        int clusterCandidateEdgeCount,
        int clusterAssignmentCount,
        bool namesOnlyWeakSupport)
    {
        var debit = edge.Source.IsOutflow ? edge.Source : edge.Candidate;
        var credit = edge.Source.IsInflow ? edge.Source : edge.Candidate;
        var highConfidenceInboundReferencesPresent =
            (edge.Source.IsInflow && edge.Source.HasHighConfidenceReferenceSignals)
            || (edge.Candidate.IsInflow && edge.Candidate.HasHighConfidenceReferenceSignals);
        var evidence = JsonSerializer.Serialize(new
        {
            family = "bank_account_transfer",
            resolutionOutcome = "paired",
            pass,
            debitId = debit.TransactionId,
            creditId = credit.TransactionId,
            score = edge.Score,
            candidateEdgeCount = clusterCandidateEdgeCount,
            clusterAssignmentCount,
            clusterMembership = new
            {
                isDuplicateClusterMember = edge.IsDuplicateClusterMember,
                clusterSize
            },
            providerCapabilities = new
            {
                debit = BuildProviderCapabilitySummary(debit),
                credit = BuildProviderCapabilitySummary(credit)
            },
            extractedNarrativeSignals = new
            {
                debit = BuildNarrativeSignalSummary(debit),
                credit = BuildNarrativeSignalSummary(credit)
            },
            referenceOverlapSummary = new
            {
                highConfidence = edge.HighConfidenceReferenceOverlap,
                providerSpecific = edge.ProviderSpecificReferenceOverlap,
                accountFragments = edge.AccountReferenceOverlap,
                paymentMarkers = edge.PaymentMarkerOverlap,
                mediumConfidence = edge.MediumConfidenceOverlap,
                lowConfidence = edge.LowConfidenceOverlap,
                weightedReferenceScore = edge.ReferenceOverlapScore,
                referenceConfidenceBand = edge.ReferenceConfidenceBand,
                namesOnlyWeakSupport = edge.NamesOnlyOverlapPenaltyApplied
            },
            timePrecisionMode = clusterTimePrecisionMode,
            timeDistanceHours = Math.Round(edge.WindowHours, 3, MidpointRounding.AwayFromZero),
            stableOrderingUsed,
            stableOrderDistance = edge.StableOrderDistance,
            weakNameSupportOnly = namesOnlyWeakSupport,
            finalTieBreakReason = tieBreakReason,
            routingInitiallyBlockedExternalCounterpartyRisk =
                edge.SourceInitiallyBlockedExternalCounterpartyRisk
                || edge.CandidateInitiallyBlockedExternalCounterpartyRisk,
            sameUserCandidateUniverseOverrideApplied = edge.SameUserCandidateUniverseOverrideApplied,
            hasPlausibleSameUserCandidateUniverse = edge.SameUserCandidateUniverseSize > 1,
            strongOppositeStructuredEvidencePresent = edge.StrongOppositeStructuredEvidencePresent,
            sameUserCandidateUniverseSize = edge.SameUserCandidateUniverseSize,
            highConfidenceInboundReferencesPresent
        });

        var decision = new TransferPairDecision(
            DebitTransactionId: debit.TransactionId,
            CreditTransactionId: credit.TransactionId,
            RuleKey: ruleKey,
            ReasonCode: reasonCode,
            Score: edge.Score,
            EvidenceJson: evidence);

        resolved[debit.TransactionId] = decision;
        resolved[credit.TransactionId] = decision;
        claimed.Add(debit.TransactionId);
        claimed.Add(credit.TransactionId);
    }

    private static Dictionary<Guid, TransferRoutingDecision> BuildRoutingDecisions(
        IReadOnlyList<DeterministicTransactionFeature> features)
    {
        var decisions = new Dictionary<Guid, TransferRoutingDecision>(features.Count);
        var sameUserCandidateUniverseSize = features
            .Select(x => x.FinancialAccountId)
            .Distinct()
            .Count();

        foreach (var feature in features)
        {
            var initiallyBlockedByExternalCounterpartyRisk = feature.LooksLikeExternalCounterparty;
            var includeByBaseSignals = LooksTransferLikeWithoutExternalBlock(feature);
            var overrideDecision = EvaluateSameUserCandidateUniverseOverride(
                feature,
                features,
                sameUserCandidateUniverseSize);
            var includeInTransferMatching = initiallyBlockedByExternalCounterpartyRisk
                ? overrideDecision.OverrideApplied
                : includeByBaseSignals;

            decisions[feature.TransactionId] = new TransferRoutingDecision(
                IncludeInTransferMatching: includeInTransferMatching,
                InitiallyBlockedByExternalCounterpartyRisk: initiallyBlockedByExternalCounterpartyRisk,
                SameUserCandidateUniverseOverrideApplied: overrideDecision.OverrideApplied,
                HasPlausibleOppositeDirectionUniverse: overrideDecision.HasPlausibleOppositeDirectionUniverse,
                HasStrongOppositeStructuredEvidence: overrideDecision.HasStrongOppositeStructuredEvidence,
                SameUserCandidateUniverseSize: sameUserCandidateUniverseSize);
        }

        return decisions;
    }

    private static SameUserOverrideEvaluation EvaluateSameUserCandidateUniverseOverride(
        DeterministicTransactionFeature source,
        IReadOnlyList<DeterministicTransactionFeature> allFeatures,
        int sameUserCandidateUniverseSize)
    {
        if (!source.LooksLikeExternalCounterparty
            || !source.HasCounterpartyAccounts
            || sameUserCandidateUniverseSize < 2)
        {
            return new SameUserOverrideEvaluation(
                OverrideApplied: false,
                HasPlausibleOppositeDirectionUniverse: false,
                HasStrongOppositeStructuredEvidence: false);
        }

        var oppositeCandidates = allFeatures
            .Where(candidate =>
                candidate.TransactionId != source.TransactionId
                && candidate.FinancialAccountId != source.FinancialAccountId
                && candidate.Currency == source.Currency
                && candidate.AbsoluteAmount == source.AbsoluteAmount
                && candidate.IsOutflow != source.IsOutflow
                && Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours) <= DeterministicCategorizationConstants.TransferCandidateWindowHours)
            .ToList();
        var hasPlausibleOppositeDirectionUniverse = oppositeCandidates.Count > 0;
        if (!hasPlausibleOppositeDirectionUniverse)
        {
            return new SameUserOverrideEvaluation(
                OverrideApplied: false,
                HasPlausibleOppositeDirectionUniverse: false,
                HasStrongOppositeStructuredEvidence: false);
        }

        var hasStrongOppositeStructuredEvidence = oppositeCandidates.Any(IsStrongStructuredTransferEvidence);
        var oppositeSideTransferPlausible = oppositeCandidates.Any(LooksTransferLikeWithoutExternalBlock);
        var overrideApplied = hasStrongOppositeStructuredEvidence && oppositeSideTransferPlausible;

        return new SameUserOverrideEvaluation(
            OverrideApplied: overrideApplied,
            HasPlausibleOppositeDirectionUniverse: hasPlausibleOppositeDirectionUniverse,
            HasStrongOppositeStructuredEvidence: hasStrongOppositeStructuredEvidence);
    }

    private static bool IsStrongStructuredTransferEvidence(DeterministicTransactionFeature feature)
    {
        return feature.HasHighConfidenceReferenceSignals
               || feature.HasProviderSpecificTransferMarker
               || feature.NarrativeSignals.MachineReferenceTokens.Count > 0
               || feature.NarrativeSignals.ProviderSpecificReferenceTokens.Count > 0
               || feature.NarrativeSignals.PaymentSystemMarkers.Count > 0;
    }

    private static bool LooksTransferLikeWithoutExternalBlock(DeterministicTransactionFeature feature)
    {
        if (feature.HasTransferKeyword
            || feature.HasProviderTransferHint
            || feature.AccountHint is not null)
        {
            return true;
        }

        if (feature.HasCounterpartyAccounts
            && (feature.HasHighConfidenceReferenceSignals || feature.HasProviderSpecificTransferMarker))
        {
            return true;
        }

        return false;
    }

    private static string CreateAmountCurrencyDayKey(DeterministicTransactionFeature feature)
    {
        return $"{feature.AbsoluteAmount:0.00}|{feature.Currency}|{feature.BookedAtUtc:yyyy-MM-dd}";
    }

    private static CandidateEdge BuildCandidateEdge(
        DeterministicTransactionFeature source,
        DeterministicTransactionFeature candidate,
        bool duplicateClusterMember,
        int duplicateClusterSize,
        TransferRoutingDecision sourceRoutingDecision,
        TransferRoutingDecision candidateRoutingDecision)
    {
        var scoring = ScoreCandidate(source, candidate, sourceRoutingDecision, candidateRoutingDecision);
        var sameUserOverrideApplied = sourceRoutingDecision.SameUserCandidateUniverseOverrideApplied
                                      || candidateRoutingDecision.SameUserCandidateUniverseOverrideApplied;
        return new CandidateEdge(
            Source: source,
            Candidate: candidate,
            Score: scoring.Score,
            TimeScore: scoring.TimeScore,
            WindowHours: scoring.WindowHours,
            TimePrecisionMode: scoring.TimePrecisionMode,
            HighConfidenceReferenceOverlap: scoring.HighConfidenceReferenceOverlap,
            ProviderSpecificReferenceOverlap: scoring.ProviderSpecificReferenceOverlap,
            AccountReferenceOverlap: scoring.AccountReferenceOverlap,
            PaymentMarkerOverlap: scoring.PaymentMarkerOverlap,
            MediumConfidenceOverlap: scoring.MediumConfidenceOverlap,
            LowConfidenceOverlap: scoring.LowConfidenceOverlap,
            ReferenceOverlapScore: scoring.ReferenceOverlapScore,
            ReferenceConfidenceBand: scoring.ReferenceConfidenceBand,
            ReferenceConfidenceRank: scoring.ReferenceConfidenceRank,
            StableOrderDistance: Math.Abs(source.StableSequence - candidate.StableSequence),
            NamesOnlyOverlapPenaltyApplied: scoring.NamesOnlyOverlapPenaltyApplied,
            SourceInitiallyBlockedExternalCounterpartyRisk: sourceRoutingDecision.InitiallyBlockedByExternalCounterpartyRisk,
            CandidateInitiallyBlockedExternalCounterpartyRisk: candidateRoutingDecision.InitiallyBlockedByExternalCounterpartyRisk,
            SameUserCandidateUniverseOverrideApplied: sameUserOverrideApplied,
            StrongOppositeStructuredEvidencePresent: sourceRoutingDecision.HasStrongOppositeStructuredEvidence
                                                    || candidateRoutingDecision.HasStrongOppositeStructuredEvidence,
            SameUserCandidateUniverseSize: Math.Max(
                sourceRoutingDecision.SameUserCandidateUniverseSize,
                candidateRoutingDecision.SameUserCandidateUniverseSize),
            IsDuplicateClusterMember: duplicateClusterMember,
            DuplicateClusterSize: duplicateClusterSize);
    }

    private static CandidateScore ScoreCandidate(
        DeterministicTransactionFeature source,
        DeterministicTransactionFeature candidate,
        TransferRoutingDecision sourceRoutingDecision,
        TransferRoutingDecision candidateRoutingDecision)
    {
        var windowHours = Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours);
        var timePrecisionMode = ResolveTimePrecisionMode(source.ProviderTimestampPrecision, candidate.ProviderTimestampPrecision);
        var timeScore = ResolveTimeScore(timePrecisionMode, windowHours);
        var sameUserOverrideApplied = sourceRoutingDecision.SameUserCandidateUniverseOverrideApplied
                                      || candidateRoutingDecision.SameUserCandidateUniverseOverrideApplied;

        var highConfidenceReferenceOverlap = CountOverlap(
            source.NarrativeSignals.HighConfidenceTokens,
            candidate.NarrativeSignals.HighConfidenceTokens);
        var providerSpecificReferenceOverlap = CountOverlap(
            source.NarrativeSignals.ProviderSpecificReferenceTokens,
            candidate.NarrativeSignals.ProviderSpecificReferenceTokens);
        var accountReferenceOverlap = CountOverlap(
            source.NarrativeSignals.AccountLikeTokens,
            candidate.NarrativeSignals.AccountLikeTokens);
        var paymentMarkerOverlap = CountOverlap(
            source.NarrativeSignals.PaymentSystemMarkers,
            candidate.NarrativeSignals.PaymentSystemMarkers);
        var mediumConfidenceOverlap = CountOverlap(
            source.NarrativeSignals.MediumConfidenceTokens,
            candidate.NarrativeSignals.MediumConfidenceTokens);
        var lowConfidenceOverlap = CountOverlap(
            source.NarrativeSignals.LowConfidenceTokens,
            candidate.NarrativeSignals.LowConfidenceTokens);

        var referenceOverlapScore =
            (highConfidenceReferenceOverlap * 8)
            + (providerSpecificReferenceOverlap * 7)
            + (accountReferenceOverlap * 6)
            + (paymentMarkerOverlap * 5)
            + (mediumConfidenceOverlap * 3)
            + Math.Min(1, lowConfidenceOverlap);

        var score = timeScore;
        score += highConfidenceReferenceOverlap * 8;
        score += providerSpecificReferenceOverlap * 7;
        score += accountReferenceOverlap * 6;
        score += paymentMarkerOverlap * 5;
        score += mediumConfidenceOverlap * 2;
        score += Math.Min(1, lowConfidenceOverlap);

        var tokenOverlap = source.Tokens.Count == 0
            ? 0d
            : source.Tokens.Intersect(candidate.Tokens, StringComparer.OrdinalIgnoreCase).Count() / (double)source.Tokens.Count;
        if (tokenOverlap >= 0.4d)
        {
            score += 2;
        }
        else if (tokenOverlap >= 0.2d)
        {
            score += 1;
        }

        if (source.AccountHint is not null && candidate.AccountHint is not null && source.AccountHint == candidate.AccountHint)
        {
            score += 3;
            accountReferenceOverlap += 1;
        }

        if (source.HasTransferKeyword || candidate.HasTransferKeyword || source.HasProviderTransferHint || candidate.HasProviderTransferHint)
        {
            score += 2;
        }

        if (source.ReferenceEntropy > 0.35d && candidate.ReferenceEntropy > 0.35d)
        {
            score += 1;
        }

        if (source.LooksLikeExternalCounterparty || candidate.LooksLikeExternalCounterparty)
        {
            score -= sameUserOverrideApplied
                ? ExternalCounterpartyPenaltyWithSameUserOverride
                : ExternalCounterpartyPenalty;
        }

        if (sameUserOverrideApplied)
        {
            score += SameUserUniverseOverrideScoreBoost;
        }

        var namesOnlyOverlap = mediumConfidenceOverlap > 0
                               && highConfidenceReferenceOverlap == 0
                               && providerSpecificReferenceOverlap == 0
                               && accountReferenceOverlap == 0
                               && paymentMarkerOverlap == 0
                               && lowConfidenceOverlap <= 1;
        if (namesOnlyOverlap)
        {
            score -= 4;
            score = Math.Min(score, MinScoreForPairing - 1);
        }

        var referenceConfidenceBand = ResolveReferenceConfidenceBand(
            highConfidenceReferenceOverlap,
            providerSpecificReferenceOverlap,
            accountReferenceOverlap,
            paymentMarkerOverlap,
            mediumConfidenceOverlap,
            lowConfidenceOverlap);
        var referenceConfidenceRank = referenceConfidenceBand switch
        {
            "high_confidence" => 3,
            "medium_confidence" => 2,
            "low_confidence" => 1,
            _ => 0
        };

        return new CandidateScore(
            Score: score,
            TimeScore: timeScore,
            WindowHours: windowHours,
            TimePrecisionMode: timePrecisionMode,
            HighConfidenceReferenceOverlap: highConfidenceReferenceOverlap,
            ProviderSpecificReferenceOverlap: providerSpecificReferenceOverlap,
            AccountReferenceOverlap: accountReferenceOverlap,
            PaymentMarkerOverlap: paymentMarkerOverlap,
            MediumConfidenceOverlap: mediumConfidenceOverlap,
            LowConfidenceOverlap: lowConfidenceOverlap,
            ReferenceOverlapScore: referenceOverlapScore,
            ReferenceConfidenceBand: referenceConfidenceBand,
            ReferenceConfidenceRank: referenceConfidenceRank,
            NamesOnlyOverlapPenaltyApplied: namesOnlyOverlap);
    }

    private static bool MateriallyEquivalent(CandidateEdge best, CandidateEdge next)
    {
        return best.Score == next.Score
               && best.ReferenceOverlapScore == next.ReferenceOverlapScore
               && best.ReferenceConfidenceRank == next.ReferenceConfidenceRank
               && best.HighConfidenceReferenceOverlap == next.HighConfidenceReferenceOverlap
               && best.ProviderSpecificReferenceOverlap == next.ProviderSpecificReferenceOverlap
               && best.AccountReferenceOverlap == next.AccountReferenceOverlap
               && best.PaymentMarkerOverlap == next.PaymentMarkerOverlap
               && best.MediumConfidenceOverlap == next.MediumConfidenceOverlap
               && string.Equals(best.TimePrecisionMode, next.TimePrecisionMode, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveReferenceConfidenceBand(
        int highConfidenceReferenceOverlap,
        int providerSpecificReferenceOverlap,
        int accountReferenceOverlap,
        int paymentMarkerOverlap,
        int mediumConfidenceOverlap,
        int lowConfidenceOverlap)
    {
        if (highConfidenceReferenceOverlap > 0
            || providerSpecificReferenceOverlap > 0
            || accountReferenceOverlap > 0
            || paymentMarkerOverlap > 0)
        {
            return "high_confidence";
        }

        if (mediumConfidenceOverlap > 0)
        {
            return "medium_confidence";
        }

        if (lowConfidenceOverlap > 0)
        {
            return "low_confidence";
        }

        return "none";
    }

    private static string ResolveTimePrecisionMode(
        DeterministicProviderTimestampPrecision left,
        DeterministicProviderTimestampPrecision right)
    {
        if (left == DeterministicProviderTimestampPrecision.DateOnly
            || right == DeterministicProviderTimestampPrecision.DateOnly)
        {
            return "date_only";
        }

        if (left == DeterministicProviderTimestampPrecision.CoarseDateTime
            || right == DeterministicProviderTimestampPrecision.CoarseDateTime)
        {
            return "coarse";
        }

        if (left == DeterministicProviderTimestampPrecision.PreciseDateTime
            && right == DeterministicProviderTimestampPrecision.PreciseDateTime)
        {
            return "precise";
        }

        return "unknown";
    }

    private static int ResolveTimeScore(string timePrecisionMode, double windowHours)
    {
        return timePrecisionMode switch
        {
            "date_only" or "coarse" => windowHours <= 24d
                ? 2
                : windowHours <= 72d
                    ? 1
                    : 0,
            "precise" => windowHours <= 3d
                ? 5
                : windowHours <= 24d
                    ? 3
                    : 1,
            _ => windowHours <= 12d
                ? 3
                : windowHours <= 48d
                    ? 2
                    : 1
        };
    }

    private static string ResolveClusterTimePrecisionMode(IReadOnlyList<ClusterAssignment> contenders)
    {
        var precisionModes = contenders
            .SelectMany(assignment => assignment.Edges.Select(edge => edge.TimePrecisionMode))
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (precisionModes.Contains("date_only", StringComparer.OrdinalIgnoreCase))
        {
            return "date_only";
        }

        if (precisionModes.Contains("coarse", StringComparer.OrdinalIgnoreCase))
        {
            return "coarse";
        }

        if (precisionModes.Contains("precise", StringComparer.OrdinalIgnoreCase)
            && precisionModes.All(mode => string.Equals(mode, "precise", StringComparison.OrdinalIgnoreCase)))
        {
            return "precise";
        }

        return "unknown";
    }

    private static bool CanUseStableOrderFallback(
        IReadOnlyList<ClusterAssignment> contenders,
        string clusterTimePrecisionMode)
    {
        if (contenders.Count <= 1)
        {
            return false;
        }

        var highConfidenceTied = contenders
            .Select(x => x.CoreScore.HighConfidenceReferenceOverlap)
            .Distinct()
            .Count() == 1;
        var providerSpecificTied = contenders
            .Select(x => x.CoreScore.ProviderSpecificReferenceOverlap)
            .Distinct()
            .Count() == 1;
        var accountTied = contenders
            .Select(x => x.CoreScore.AccountReferenceOverlap)
            .Distinct()
            .Count() == 1;
        var paymentTied = contenders
            .Select(x => x.CoreScore.PaymentMarkerOverlap)
            .Distinct()
            .Count() == 1;
        var mediumStructuredTied = contenders
            .Select(x => x.CoreScore.MediumConfidenceOverlap)
            .Distinct()
            .Count() == 1;
        if (!highConfidenceTied
            || !providerSpecificTied
            || !accountTied
            || !paymentTied
            || !mediumStructuredTied)
        {
            return false;
        }

        var coarsePrecision = clusterTimePrecisionMode is "date_only" or "coarse" or "unknown";
        var nonDiscriminatingTime = contenders
            .Select(x => x.CoreScore.TotalWindowMinutes)
            .Distinct()
            .Count() == 1;

        return coarsePrecision || nonDiscriminatingTime;
    }

    private static string ResolveClusterTieBreakReason(CoreClusterScore coreScore)
    {
        if (coreScore.HighConfidenceReferenceOverlap > 0
            || coreScore.ProviderSpecificReferenceOverlap > 0
            || coreScore.AccountReferenceOverlap > 0
            || coreScore.PaymentMarkerOverlap > 0)
        {
            return "high_confidence_reference_overlap";
        }

        if (coreScore.MediumConfidenceOverlap > 0)
        {
            return "medium_confidence_structured_overlap";
        }

        if (coreScore.TotalWindowMinutes > 0)
        {
            return "time_proximity";
        }

        return "score_rank";
    }

    private static int CountOverlap(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        return left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
    }

    private static bool HasExplicitCounterpartyExpectation(
        DeterministicTransactionFeature feature,
        bool isDuplicateClusterMember,
        bool sameUserCandidateUniverseOverrideApplied)
    {
        if (!feature.HasCounterpartyAccounts)
        {
            return false;
        }

        if (feature.LooksLikeExternalCounterparty && !sameUserCandidateUniverseOverrideApplied)
        {
            return false;
        }

        if (feature.HasHighConfidenceReferenceSignals)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(feature.AccountHint)
            && (feature.HasProviderTransferHint || feature.HasTransferKeyword))
        {
            return true;
        }

        if (feature.HasTransferKeyword
            && feature.HasProviderTransferHint
            && feature.ReferenceEntropy >= 0.35d)
        {
            return true;
        }

        if (feature.HasProviderSpecificTransferMarker && feature.HasMediumConfidenceReferenceSignals)
        {
            return true;
        }

        if (isDuplicateClusterMember
            && feature.SameAmountSameDayOutflowCount > 1
            && feature.SameAmountSameDayInflowCount > 1)
        {
            return true;
        }

        return false;
    }

    private static ProviderCapabilitySummary BuildProviderCapabilitySummary(DeterministicTransactionFeature feature)
    {
        return new ProviderCapabilitySummary(
            feature.ProviderKey,
            feature.ProviderTimestampPrecision.ToString().ToLowerInvariant(),
            feature.ProviderSupportsMachineReferenceTokens,
            feature.ProviderSupportsPaymentSystemMarkers,
            feature.ProviderSupportsReliableCounterpartyReferenceFragments,
            feature.ProviderSupportsProviderSpecificTransferMarkers);
    }

    private static NarrativeSignalSummary BuildNarrativeSignalSummary(DeterministicTransactionFeature feature)
    {
        return new NarrativeSignalSummary(
            MachineReferenceTokens: feature.NarrativeSignals.MachineReferenceTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            AccountLikeTokens: feature.NarrativeSignals.AccountLikeTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            IbanLikeFragments: feature.NarrativeSignals.IbanLikeFragments.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            PaymentSystemMarkers: feature.NarrativeSignals.PaymentSystemMarkers.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            BeneficiaryNameTokens: feature.NarrativeSignals.BeneficiaryNameTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            OriginatorNameTokens: feature.NarrativeSignals.OriginatorNameTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            FreeTextReferenceTokens: feature.NarrativeSignals.FreeTextReferenceTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ProviderSpecificReferenceTokens: feature.NarrativeSignals.ProviderSpecificReferenceTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            MerchantLikeTokens: feature.NarrativeSignals.MerchantLikeTokens.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            SignalConfidenceMap: feature.NarrativeSignals.SignalConfidenceMap
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.Value switch
                    {
                        NarrativeSignalConfidenceTier.HighConfidence => "high_confidence",
                        NarrativeSignalConfidenceTier.MediumConfidence => "medium_confidence",
                        _ => "low_confidence"
                    },
                    StringComparer.OrdinalIgnoreCase));
    }

    private sealed record CandidateEdge(
        DeterministicTransactionFeature Source,
        DeterministicTransactionFeature Candidate,
        int Score,
        int TimeScore,
        double WindowHours,
        string TimePrecisionMode,
        int HighConfidenceReferenceOverlap,
        int ProviderSpecificReferenceOverlap,
        int AccountReferenceOverlap,
        int PaymentMarkerOverlap,
        int MediumConfidenceOverlap,
        int LowConfidenceOverlap,
        int ReferenceOverlapScore,
        string ReferenceConfidenceBand,
        int ReferenceConfidenceRank,
        long StableOrderDistance,
        bool NamesOnlyOverlapPenaltyApplied,
        bool SourceInitiallyBlockedExternalCounterpartyRisk,
        bool CandidateInitiallyBlockedExternalCounterpartyRisk,
        bool SameUserCandidateUniverseOverrideApplied,
        bool StrongOppositeStructuredEvidencePresent,
        int SameUserCandidateUniverseSize,
        bool IsDuplicateClusterMember,
        int DuplicateClusterSize);

    private sealed record CandidateScore(
        int Score,
        int TimeScore,
        double WindowHours,
        string TimePrecisionMode,
        int HighConfidenceReferenceOverlap,
        int ProviderSpecificReferenceOverlap,
        int AccountReferenceOverlap,
        int PaymentMarkerOverlap,
        int MediumConfidenceOverlap,
        int LowConfidenceOverlap,
        int ReferenceOverlapScore,
        string ReferenceConfidenceBand,
        int ReferenceConfidenceRank,
        bool NamesOnlyOverlapPenaltyApplied);

    private sealed record ClusterAssignment(
        IReadOnlyList<CandidateEdge> Edges,
        int EdgeCount,
        CoreClusterScore CoreScore,
        long StableOrderDistanceSum,
        int StableOrderCrossingPenalty);

    private sealed record CoreClusterScore(
        int TotalScore,
        int HighConfidenceReferenceOverlap,
        int ProviderSpecificReferenceOverlap,
        int AccountReferenceOverlap,
        int PaymentMarkerOverlap,
        int MediumConfidenceOverlap,
        int ReferenceOverlapScore,
        int TotalWindowMinutes);

    private sealed record TransferRoutingDecision(
        bool IncludeInTransferMatching,
        bool InitiallyBlockedByExternalCounterpartyRisk,
        bool SameUserCandidateUniverseOverrideApplied,
        bool HasPlausibleOppositeDirectionUniverse,
        bool HasStrongOppositeStructuredEvidence,
        int SameUserCandidateUniverseSize);

    private sealed record SameUserOverrideEvaluation(
        bool OverrideApplied,
        bool HasPlausibleOppositeDirectionUniverse,
        bool HasStrongOppositeStructuredEvidence);

    private sealed record ProviderCapabilitySummary(
        string ProviderKey,
        string TimestampPrecision,
        bool SupportsMachineReferenceTokens,
        bool SupportsPaymentSystemMarkers,
        bool SupportsReliableCounterpartyReferenceFragments,
        bool SupportsProviderSpecificTransferMarkers);

    private sealed record NarrativeSignalSummary(
        IReadOnlyList<string> MachineReferenceTokens,
        IReadOnlyList<string> AccountLikeTokens,
        IReadOnlyList<string> IbanLikeFragments,
        IReadOnlyList<string> PaymentSystemMarkers,
        IReadOnlyList<string> BeneficiaryNameTokens,
        IReadOnlyList<string> OriginatorNameTokens,
        IReadOnlyList<string> FreeTextReferenceTokens,
        IReadOnlyList<string> ProviderSpecificReferenceTokens,
        IReadOnlyList<string> MerchantLikeTokens,
        IReadOnlyDictionary<string, string> SignalConfidenceMap);
}
