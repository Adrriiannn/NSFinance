using System.Text.Json;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class TransferPairingEngine
{
    public sealed record TransferPairingAnalysis(
        IReadOnlyDictionary<Guid, TransferPendingDecision> PendingDecisions,
        int CandidateEdgeCount,
        int AmbiguousCount);

    public TransferPairingAnalysis AnalyzeUnpairedTransactions(
        IReadOnlyDictionary<Guid, DeterministicTransactionFeature> featuresById,
        IReadOnlySet<Guid> pairedTransactionIds)
    {
        var features = featuresById.Values.ToList();
        if (features.Count == 0)
        {
            return new TransferPairingAnalysis(new Dictionary<Guid, TransferPendingDecision>(), 0, 0);
        }

        var pending = new Dictionary<Guid, TransferPendingDecision>();
        var candidateEdgeCount = 0;
        var ambiguousCount = 0;

        foreach (var feature in features)
        {
            if (pairedTransactionIds.Contains(feature.TransactionId))
            {
                continue;
            }

            if (!LooksTransferLike(feature))
            {
                continue;
            }

            var candidates = features
                .Where(x =>
                    x.TransactionId != feature.TransactionId
                    && !pairedTransactionIds.Contains(x.TransactionId)
                    && x.FinancialAccountId != feature.FinancialAccountId
                    && x.Currency == feature.Currency
                    && x.AbsoluteAmount == feature.AbsoluteAmount
                    && x.IsOutflow != feature.IsOutflow
                    && Math.Abs((x.BookedAtUtc - feature.BookedAtUtc).TotalHours) <= DeterministicCategorizationConstants.TransferCandidateWindowHours)
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = ScoreCandidate(feature, candidate),
                    WindowHours = Math.Abs((candidate.BookedAtUtc - feature.BookedAtUtc).TotalHours)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.WindowHours)
                .ToList();

            candidateEdgeCount += candidates.Count;
            if (candidates.Count == 0)
            {
                var explicitCounterpartyExpectation = HasExplicitCounterpartyExpectation(feature);
                var status = feature.IsPending
                    ? DeterministicClassificationStatus.DeferredWaitingForMoreContext
                    : feature.HasCounterpartyAccounts && explicitCounterpartyExpectation
                        ? DeterministicClassificationStatus.DeferredWaitingForCounterparty
                        : DeterministicClassificationStatus.EvaluatedNoMatchingRule;
                var reasonCode = status switch
                {
                    DeterministicClassificationStatus.DeferredWaitingForMoreContext => DeterministicClassificationReasonCodes.DeferredPendingBookedContext,
                    DeterministicClassificationStatus.DeferredWaitingForCounterparty => DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
                    _ => DeterministicClassificationReasonCodes.EvaluatedInsufficientSignals
                };
                var retryEligible = status is DeterministicClassificationStatus.DeferredWaitingForCounterparty
                    or DeterministicClassificationStatus.DeferredWaitingForMoreContext;

                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    status,
                    reasonCode,
                    retryEligible,
                    JsonSerializer.Serialize(new
                    {
                        pass = "defer_not_fail",
                        candidateCount = 0,
                        feature.HasTransferKeyword,
                        feature.HasProviderTransferHint,
                        feature.AccountHint,
                        feature.NearbySameAmountCount
                    }));
                continue;
            }

            if (candidates.Count > 1)
            {
                var bestScore = candidates[0].Score;
                var nextScore = candidates[1].Score;
                if ((bestScore - nextScore) <= 1)
                {
                    ambiguousCount++;
                    pending[feature.TransactionId] = new TransferPendingDecision(
                        feature.TransactionId,
                        DeterministicClassificationStatus.RejectedAmbiguousMatch,
                        DeterministicClassificationReasonCodes.RejectedAmbiguousCandidates,
                        false,
                        JsonSerializer.Serialize(new
                        {
                            pass = "conflict_resolution",
                            candidateCount = candidates.Count,
                            bestScore,
                            nextScore
                        }));
                    continue;
                }
            }

            var best = candidates[0];
            var bestScoreStrongEnough = best.Score >= 8;
            var explicitCounterpartyExpectationForBest = HasExplicitCounterpartyExpectation(feature);
            if (!feature.IsBooked || !best.Candidate.IsBooked || feature.IsPending || best.Candidate.IsPending)
            {
                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    DeterministicClassificationStatus.DeferredWaitingForMoreContext,
                    DeterministicClassificationReasonCodes.DeferredPendingBookedContext,
                    true,
                    JsonSerializer.Serialize(new
                    {
                        pass = "defer_not_fail",
                        candidateId = best.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = best.Score
                    }));
                continue;
            }

            if (!bestScoreStrongEnough || !explicitCounterpartyExpectationForBest)
            {
                pending[feature.TransactionId] = new TransferPendingDecision(
                    feature.TransactionId,
                    DeterministicClassificationStatus.EvaluatedNoMatchingRule,
                    DeterministicClassificationReasonCodes.EvaluatedInsufficientSignals,
                    false,
                    JsonSerializer.Serialize(new
                    {
                        pass = "defer_not_fail",
                        candidateId = best.Candidate.TransactionId,
                        candidateCount = candidates.Count,
                        bestScore = best.Score,
                        explicitCounterpartyExpectation = explicitCounterpartyExpectationForBest
                    }));
                continue;
            }

            pending[feature.TransactionId] = new TransferPendingDecision(
                feature.TransactionId,
                DeterministicClassificationStatus.DeferredWaitingForCounterparty,
                DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
                true,
                JsonSerializer.Serialize(new
                {
                    pass = "mutual_best_resolution",
                    candidateId = best.Candidate.TransactionId,
                    candidateCount = candidates.Count,
                    bestScore = best.Score
                }));
        }

        return new TransferPairingAnalysis(pending, candidateEdgeCount, ambiguousCount);
    }

    private static bool LooksTransferLike(DeterministicTransactionFeature feature)
    {
        return feature.HasTransferKeyword
               || feature.HasProviderTransferHint
               || feature.AccountHint is not null
               || feature.HasSavingsKeyword;
    }

    private static int ScoreCandidate(
        DeterministicTransactionFeature source,
        DeterministicTransactionFeature candidate)
    {
        var score = 0;

        // Pass A - strict pair.
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

        // Pass B - descriptor support.
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

        // Pass C - account hints and transfer wording.
        if (source.AccountHint is not null && candidate.AccountHint is not null && source.AccountHint == candidate.AccountHint)
        {
            score += 3;
        }

        if (source.HasTransferKeyword || candidate.HasTransferKeyword || source.HasProviderTransferHint || candidate.HasProviderTransferHint)
        {
            score += 2;
        }

        // Pass D - mutual confidence proxy by entropy.
        if (source.ReferenceEntropy > 0.35d && candidate.ReferenceEntropy > 0.35d)
        {
            score += 1;
        }

        return score;
    }

    private static bool HasExplicitCounterpartyExpectation(DeterministicTransactionFeature feature)
    {
        if (!feature.HasCounterpartyAccounts)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(feature.AccountHint))
        {
            return true;
        }

        return feature.HasTransferKeyword && feature.HasProviderTransferHint && feature.ReferenceEntropy >= 0.3d;
    }
}
