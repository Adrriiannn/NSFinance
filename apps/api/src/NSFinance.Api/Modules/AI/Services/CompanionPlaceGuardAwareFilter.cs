namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceGuardAwareFilter(IChatTelemetry telemetry) : ICompanionPlaceGuardAwareFilter
{
    private const int UnknownRetentionThreshold = 10;

    public IReadOnlyList<CompanionPlacePoolCandidate> Apply(
        CompanionPlaceSearchStrategy? strategy,
        IReadOnlyList<CompanionPlacePoolCandidate> rankedCandidates,
        CompanionGuardEvaluationResult evidence)
    {
        if (evidence.AppliedGuardIds.Count == 0 || evidence.EvidenceByPlaceId.Count == 0)
        {
            return rankedCandidates;
        }

        var scored = rankedCandidates
            .Select((candidate, index) => new
            {
                Candidate = candidate,
                OriginalIndex = index,
                Evidence = evidence.EvidenceByPlaceId.TryGetValue(candidate.PlaceId, out var items) ? items : []
            })
            .Select(item => new
            {
                item.Candidate,
                item.OriginalIndex,
                Status = ResolveStatus(item.Evidence),
                Score = ResolveScore(item.Evidence)
            })
            .ToArray();

        var withoutConfirmedConflicts = scored
            .Where(static item => item.Status != CompanionGuardEvidenceStatus.ConfirmedConflict
                                  && !(item.Status == CompanionGuardEvidenceStatus.LikelyConflict && item.Score >= 0.80d))
            .ToArray();

        var knownMatches = withoutConfirmedConflicts.Count(static item => item.Status is CompanionGuardEvidenceStatus.ConfirmedMatch or CompanionGuardEvidenceStatus.LikelyMatch);
        var includeUnknowns = ShouldKeepUnknowns(evidence, knownMatches);
        var filtered = withoutConfirmedConflicts
            .Where(item => includeUnknowns || item.Status != CompanionGuardEvidenceStatus.Unknown)
            .OrderByDescending(static item => StatusRank(item.Status))
            .ThenByDescending(static item => item.Score)
            .ThenBy(static item => item.OriginalIndex)
            .Select(static item => item.Candidate)
            .ToArray();

        _ = telemetry.TrackAsync(
            "places.guard_filter.applied",
            new Dictionary<string, object?>
            {
                ["appliedGuardIds"] = evidence.AppliedGuardIds.ToArray(),
                ["candidateCount"] = rankedCandidates.Count,
                ["returnedCardCount"] = filtered.Length,
                ["confirmedConflictRemovedCount"] = scored.Count(static item => item.Status == CompanionGuardEvidenceStatus.ConfirmedConflict),
                ["likelyConflictRemovedCount"] = scored.Count(static item => item.Status == CompanionGuardEvidenceStatus.LikelyConflict && item.Score >= 0.80d),
                ["unknownsKept"] = includeUnknowns
            },
            CancellationToken.None);

        return filtered;
    }

    private static CompanionGuardEvidenceStatus ResolveStatus(IReadOnlyList<CompanionGuardEvidence> evidence)
    {
        if (evidence.Any(static item => item.Status == CompanionGuardEvidenceStatus.ConfirmedConflict))
        {
            return CompanionGuardEvidenceStatus.ConfirmedConflict;
        }

        if (evidence.Any(static item => item.Status == CompanionGuardEvidenceStatus.LikelyConflict))
        {
            return CompanionGuardEvidenceStatus.LikelyConflict;
        }

        if (evidence.Any(static item => item.Status == CompanionGuardEvidenceStatus.ConfirmedMatch))
        {
            return CompanionGuardEvidenceStatus.ConfirmedMatch;
        }

        if (evidence.Any(static item => item.Status == CompanionGuardEvidenceStatus.LikelyMatch))
        {
            return CompanionGuardEvidenceStatus.LikelyMatch;
        }

        return CompanionGuardEvidenceStatus.Unknown;
    }

    private static double ResolveScore(IReadOnlyList<CompanionGuardEvidence> evidence)
    {
        return evidence.Count == 0 ? 0d : evidence.Max(static item => item.Confidence);
    }

    private static bool ShouldKeepUnknowns(CompanionGuardEvaluationResult evidence, int knownMatches)
    {
        if (knownMatches == 0)
        {
            return true;
        }

        // Parking requests read as a strict availability question. Once we have
        // positive evidence, unknown parking candidates should not dilute the list.
        if (evidence.AppliedGuardIds.Contains("parking_availability", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return knownMatches < UnknownRetentionThreshold;
    }

    private static int StatusRank(CompanionGuardEvidenceStatus status)
    {
        return status switch
        {
            CompanionGuardEvidenceStatus.ConfirmedMatch => 4,
            CompanionGuardEvidenceStatus.LikelyMatch => 3,
            CompanionGuardEvidenceStatus.Unknown => 2,
            CompanionGuardEvidenceStatus.LikelyConflict => 1,
            CompanionGuardEvidenceStatus.ConfirmedConflict => 0,
            _ => 0
        };
    }
}
