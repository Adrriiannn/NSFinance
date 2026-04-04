using System.Text.Json;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class TransferPairingEngine
{
    private const int MinScoreForPairing = 8;
    private const int MinScoreForStrongDeferredCounterparty = 9;
    private const int MutualBestMinimumMargin = 2;
    private const int AmbiguityMaxScoreGap = 1;
    private const int MaxClusterRowsPerSide = 8;

    public sealed record TransferPairingAnalysis(
        IReadOnlyDictionary<Guid, TransferPendingDecision> PendingDecisions,
        IReadOnlyDictionary<Guid, TransferPairDecision> ResolvedPairDecisions,
        int CandidateEdgeCount,
        int AmbiguousCount);

    public TransferPairingAnalysis AnalyzeUnpairedTransactions(
        IReadOnlyDictionary<Guid, DeterministicTransactionFeature> featuresById,
        IReadOnlySet<Guid> pairedTransactionIds)
    {
        var transferLikeFeatures = featuresById.Values
            .Where(LooksTransferLike)
            .ToList();
        if (transferLikeFeatures.Count == 0)
        {
            return new TransferPairingAnalysis(
                new Dictionary<Guid, TransferPendingDecision>(),
                new Dictionary<Guid, TransferPairDecision>(),
                0,
                0);
        }

        var groupStatsByAmountCurrency = transferLikeFeatures
            .GroupBy(CreateAmountCurrencyKey)
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

            var amountKey = CreateAmountCurrencyKey(source);
            groupStatsByAmountCurrency.TryGetValue(amountKey, out var groupStats);
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
                .Select(candidate => new CandidateEdge(
                    Source: source,
                    Candidate: candidate,
                    Score: ScoreCandidate(source, candidate),
                    WindowHours: Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours),
                    IsDuplicateClusterMember: duplicateClusterMember,
                    DuplicateClusterSize: duplicateClusterSize))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.WindowHours)
                .ThenBy(x => x.Candidate.BookedAtUtc)
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
                var hasExplicitCounterpartyExpectation = HasExplicitCounterpartyExpectation(feature, duplicateClusterMember);
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
                        pass = "defer_not_fail",
                        candidateCount = 0,
                        duplicateClusterMember,
                        duplicateClusterSize,
                        feature.HasTransferKeyword,
                        feature.HasProviderTransferHint,
                        feature.AccountHint,
                        feature.NearbySameAmountCount
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
                        pass = "duplicate_cluster_conflict_resolution",
                        candidateCount = candidates.Count,
                        duplicateClusterSize,
                        topCandidateId = topCandidate?.Candidate.TransactionId,
                        topCandidateScore = topCandidate?.Score
                    }));
                continue;
            }

            if (candidates.Count > 1)
            {
                var bestScore = candidates[0].Score;
                var nextScore = candidates[1].Score;
                if ((bestScore - nextScore) <= AmbiguityMaxScoreGap)
                {
                    ambiguousCount++;
                    pending[feature.TransactionId] = new TransferPendingDecision(
                        feature.TransactionId,
                        DeterministicClassificationStatus.RejectedAmbiguousMatch,
                        DeterministicClassificationReasonCodes.TransferRejectedAmbiguous,
                        false,
                        CandidateFamily: "bank_account_transfer",
                        CandidateCount: candidates.Count,
                        TopCandidateTransactionId: candidates[0].Candidate.TransactionId,
                        TopCandidateScore: candidates[0].Score,
                        IsDuplicateClusterMember: false,
                        DuplicateClusterSize: duplicateClusterSize,
                        EvidenceJson: JsonSerializer.Serialize(new
                        {
                            family = "bank_account_transfer",
                            pass = "conflict_resolution",
                            candidateCount = candidates.Count,
                            bestScore,
                            nextScore
                        }));
                    continue;
                }
            }

            var best = candidates[0];
            var bestScoreStrongEnough = best.Score >= MinScoreForStrongDeferredCounterparty;
            var explicitCounterpartyExpectation = HasExplicitCounterpartyExpectation(feature, duplicateClusterMember);
            if (!feature.IsBooked || !best.Candidate.IsBooked || feature.IsPending || best.Candidate.IsPending)
            {
                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    DeterministicClassificationStatus.DeferredWaitingForMoreContext,
                    DeterministicClassificationReasonCodes.DeferredPendingBookedContext,
                    true,
                    CandidateFamily: "bank_account_transfer",
                    CandidateCount: candidates.Count,
                    TopCandidateTransactionId: best.Candidate.TransactionId,
                    TopCandidateScore: best.Score,
                    IsDuplicateClusterMember: duplicateClusterMember,
                    DuplicateClusterSize: duplicateClusterSize,
                    EvidenceJson: JsonSerializer.Serialize(new
                    {
                        family = "bank_account_transfer",
                        pass = "defer_not_fail",
                        candidateId = best.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = best.Score
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
                    TopCandidateTransactionId: best.Candidate.TransactionId,
                    TopCandidateScore: best.Score,
                    IsDuplicateClusterMember: duplicateClusterMember,
                    DuplicateClusterSize: duplicateClusterSize,
                    EvidenceJson: JsonSerializer.Serialize(new
                    {
                        family = "bank_account_transfer",
                        pass = "defer_not_fail",
                        candidateId = best.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = best.Score,
                        explicitCounterpartyExpectation,
                        duplicateClusterMember
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
                TopCandidateTransactionId: best.Candidate.TransactionId,
                TopCandidateScore: best.Score,
                IsDuplicateClusterMember: duplicateClusterMember,
                DuplicateClusterSize: duplicateClusterSize,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "bank_account_transfer",
                    pass = "mutual_best_resolution",
                    candidateId = best.Candidate.TransactionId,
                    candidateCount = candidates.Count,
                    bestScore = best.Score
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

        // Pass T1: unique clear pair.
        foreach (var source in transferLikeFeatures.Where(x => x.IsOutflow))
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
                || !candidate.Candidate.IsBooked)
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
                source,
                candidate.Candidate,
                "bank_transfer.strict_unique_pair_v3",
                DeterministicClassificationReasonCodes.TransferPairStrictMatch,
                candidate.Score,
                pass: "strict_unique_pair");
        }

        // Pass T2: mutual best resolution.
        foreach (var source in transferLikeFeatures.Where(x => x.IsOutflow))
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

            var sourceMargin = best.Score - (availableSourceCandidates.Count > 1 ? availableSourceCandidates[1].Score : best.Score - MutualBestMinimumMargin);
            var reciprocalMargin = reciprocalBest.Score - (reciprocalAvailable.Count > 1 ? reciprocalAvailable[1].Score : reciprocalBest.Score - MutualBestMinimumMargin);
            if (best.Score < MinScoreForStrongDeferredCounterparty
                || sourceMargin < MutualBestMinimumMargin
                || reciprocalMargin < MutualBestMinimumMargin
                || !source.IsBooked
                || !best.Candidate.IsBooked)
            {
                continue;
            }

            ApplyPair(
                resolved,
                claimed,
                source,
                best.Candidate,
                "bank_transfer.mutual_best_pair_v3",
                DeterministicClassificationReasonCodes.TransferPairMutualBest,
                best.Score,
                pass: "mutual_best_pair");
        }

        // Pass T3: duplicate-cluster nearest-neighbor resolution.
        var unresolved = transferLikeFeatures
            .Where(x => !claimed.Contains(x.TransactionId))
            .ToList();
        var clusterGroups = unresolved
            .GroupBy(CreateAmountCurrencyKey)
            .Where(group => group.Count(x => x.IsOutflow) > 1 && group.Count(x => x.IsInflow) > 1)
            .ToList();

        foreach (var clusterGroup in clusterGroups)
        {
            var outflows = clusterGroup
                .Where(x => x.IsOutflow)
                .OrderBy(x => x.BookedAtUtc)
                .ThenBy(x => x.TransactionId)
                .ToList();
            var inflows = clusterGroup
                .Where(x => x.IsInflow)
                .OrderBy(x => x.BookedAtUtc)
                .ThenBy(x => x.TransactionId)
                .ToList();

            if (outflows.Count == 0
                || inflows.Count == 0
                || outflows.Count != inflows.Count
                || outflows.Count > MaxClusterRowsPerSide)
            {
                continue;
            }

            var selected = new List<(DeterministicTransactionFeature Debit, DeterministicTransactionFeature Credit, int Score)>(outflows.Count);
            var availableInflows = inflows.ToDictionary(x => x.TransactionId, x => x);
            var monotonicCreditTime = DateTime.MinValue;
            var clusterRejected = false;

            foreach (var debit in outflows)
            {
                if (!candidatesBySourceId.TryGetValue(debit.TransactionId, out var debitCandidates))
                {
                    clusterRejected = true;
                    break;
                }

                var candidate = debitCandidates
                    .Where(x => availableInflows.ContainsKey(x.Candidate.TransactionId))
                    .Where(x => x.Score >= MinScoreForPairing)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.WindowHours)
                    .FirstOrDefault();

                if (candidate is null)
                {
                    clusterRejected = true;
                    break;
                }

                if (candidate.Candidate.BookedAtUtc < monotonicCreditTime)
                {
                    clusterRejected = true;
                    break;
                }

                if (Math.Abs((candidate.Candidate.BookedAtUtc - debit.BookedAtUtc).TotalHours) > 24)
                {
                    clusterRejected = true;
                    break;
                }

                if (!debit.IsBooked || !candidate.Candidate.IsBooked)
                {
                    clusterRejected = true;
                    break;
                }

                selected.Add((debit, candidate.Candidate, candidate.Score));
                availableInflows.Remove(candidate.Candidate.TransactionId);
                monotonicCreditTime = candidate.Candidate.BookedAtUtc;
            }

            if (clusterRejected || selected.Count != outflows.Count)
            {
                continue;
            }

            foreach (var selectedPair in selected)
            {
                ApplyPair(
                    resolved,
                    claimed,
                    selectedPair.Debit,
                    selectedPair.Credit,
                    "bank_transfer.duplicate_cluster_pair_v3",
                    DeterministicClassificationReasonCodes.TransferPairDuplicateCluster,
                    selectedPair.Score,
                    pass: "duplicate_cluster_nearest_neighbor");
            }
        }

        return resolved;
    }

    private static void ApplyPair(
        Dictionary<Guid, TransferPairDecision> resolved,
        HashSet<Guid> claimed,
        DeterministicTransactionFeature debit,
        DeterministicTransactionFeature credit,
        string ruleKey,
        string reasonCode,
        int score,
        string pass)
    {
        var evidence = JsonSerializer.Serialize(new
        {
            family = "bank_account_transfer",
            pass,
            debitId = debit.TransactionId,
            creditId = credit.TransactionId,
            score
        });
        var decision = new TransferPairDecision(
            DebitTransactionId: debit.TransactionId,
            CreditTransactionId: credit.TransactionId,
            RuleKey: ruleKey,
            ReasonCode: reasonCode,
            Score: score,
            EvidenceJson: evidence);

        resolved[debit.TransactionId] = decision;
        resolved[credit.TransactionId] = decision;
        claimed.Add(debit.TransactionId);
        claimed.Add(credit.TransactionId);
    }

    private static bool LooksTransferLike(DeterministicTransactionFeature feature)
    {
        if (feature.LooksLikeExternalCounterparty)
        {
            return false;
        }

        return feature.HasTransferKeyword
               || feature.HasProviderTransferHint
               || feature.AccountHint is not null;
    }

    private static string CreateAmountCurrencyKey(DeterministicTransactionFeature feature)
    {
        return $"{feature.AbsoluteAmount:0.00}|{feature.Currency}";
    }

    private static int ScoreCandidate(
        DeterministicTransactionFeature source,
        DeterministicTransactionFeature candidate)
    {
        var score = 0;

        var absoluteHourDistance = Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours);
        if (absoluteHourDistance <= 3d)
        {
            score += 5;
        }
        else if (absoluteHourDistance <= 24d)
        {
            score += 3;
        }
        else
        {
            score += 1;
        }

        var tokenOverlap = source.Tokens.Count == 0
            ? 0d
            : source.Tokens.Intersect(candidate.Tokens, StringComparer.OrdinalIgnoreCase).Count() / (double)source.Tokens.Count;
        if (tokenOverlap >= 0.4d)
        {
            score += 3;
        }
        else if (tokenOverlap >= 0.2d)
        {
            score += 2;
        }

        if (source.AccountHint is not null && candidate.AccountHint is not null && source.AccountHint == candidate.AccountHint)
        {
            score += 3;
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
            score -= 4;
        }

        return score;
    }

    private static bool HasExplicitCounterpartyExpectation(
        DeterministicTransactionFeature feature,
        bool isDuplicateClusterMember)
    {
        if (!feature.HasCounterpartyAccounts || feature.LooksLikeExternalCounterparty)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(feature.AccountHint))
        {
            return true;
        }

        if (feature.HasTransferKeyword && feature.HasProviderTransferHint && feature.ReferenceEntropy >= 0.35d)
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

    private sealed record CandidateEdge(
        DeterministicTransactionFeature Source,
        DeterministicTransactionFeature Candidate,
        int Score,
        double WindowHours,
        bool IsDuplicateClusterMember,
        int DuplicateClusterSize);
}
