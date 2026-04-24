namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionPlaceRankingContext(
    bool ApplyDistanceRanking,
    double? UserLatitude,
    double? UserLongitude,
    IReadOnlyList<string> PlaceTypeHints,
    string? BrandTerm = null,
    string? CanonicalConcept = null,
    IReadOnlyList<string>? ExcludedTypeHints = null,
    IReadOnlyList<string>? PreferenceHints = null,
    IReadOnlyList<string>? TimeHints = null);

public sealed record CompanionPlaceRankingResult(
    IReadOnlyList<CompanionPlaceCandidate> RankedCandidates,
    IReadOnlyDictionary<string, double> DistanceMetersByPlaceId,
    IReadOnlyList<string> Diagnostics);

public interface ICompanionPlaceRankingPolicy
{
    CompanionPlaceRankingResult Rank(
        IReadOnlyList<CompanionPlaceCandidate> candidates,
        CompanionPlaceRankingContext context);
}

public sealed class CompanionPlaceRankingPolicy : ICompanionPlaceRankingPolicy
{
    public CompanionPlaceRankingResult Rank(
        IReadOnlyList<CompanionPlaceCandidate> candidates,
        CompanionPlaceRankingContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        if (candidates.Count <= 1)
        {
            return new CompanionPlaceRankingResult(
                RankedCandidates: candidates,
                DistanceMetersByPlaceId: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                Diagnostics: context.ApplyDistanceRanking
                    ? ["places_ranking:gps_distance_applied", $"places_ranking:distance_ranked_candidate_count:{candidates.Count}"]
                    : ["places_ranking:distance_not_applicable_non_gps"]);
        }

        if (!context.ApplyDistanceRanking
            || !context.UserLatitude.HasValue
            || !context.UserLongitude.HasValue)
        {
            return new CompanionPlaceRankingResult(
                RankedCandidates: candidates,
                DistanceMetersByPlaceId: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                Diagnostics: ["places_ranking:distance_not_applicable_non_gps"]);
        }

        var distanceByPlaceId = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var scored = new List<(CompanionPlaceCandidate Candidate, double Score, double? DistanceMeters)>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var distanceMeters = TryComputeDistanceMeters(
                context.UserLatitude.Value,
                context.UserLongitude.Value,
                candidate.Location);
            if (distanceMeters.HasValue && !string.IsNullOrWhiteSpace(candidate.PlaceId))
            {
                distanceByPlaceId[candidate.PlaceId] = distanceMeters.Value;
            }

            var score = ComputeScore(candidate, distanceMeters, context);
            scored.Add((candidate, score, distanceMeters));
        }

        var ranked = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DistanceMeters ?? double.MaxValue)
            .ThenByDescending(item => item.Candidate.Rating ?? 0d)
            .ThenBy(item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Candidate)
            .ToArray();

        var diagnostics = new HashSet<string>(StringComparer.Ordinal)
        {
            "places_ranking:gps_distance_applied",
            "places_ranking:near_me_distance_ranked",
            $"places_ranking:distance_ranked_candidate_count:{ranked.Length}"
        };
        if (scored.Any(item => !item.DistanceMeters.HasValue))
        {
            diagnostics.Add("places_ranking:distance_missing_for_some_candidates");
        }

        return new CompanionPlaceRankingResult(
            RankedCandidates: ranked,
            DistanceMetersByPlaceId: distanceByPlaceId,
            Diagnostics: diagnostics.ToArray());
    }

    private static double ComputeScore(
        CompanionPlaceCandidate candidate,
        double? distanceMeters,
        CompanionPlaceRankingContext context)
    {
        var distanceScore = ScoreDistance(distanceMeters);
        var typeScore = ScoreTypeFit(candidate, context.PlaceTypeHints);
        var conceptScore = ScoreConceptFit(candidate, context.CanonicalConcept);
        var brandScore = ScoreBrandFit(candidate, context.BrandTerm);
        var preferenceScore = ScorePreferenceFit(candidate, context.PreferenceHints ?? []);
        var timeScore = ScoreTimeFit(candidate, context.TimeHints ?? []);
        var qualityScore = ScoreQuality(candidate);
        var exclusionPenalty = ScoreExclusionPenalty(candidate, context.ExcludedTypeHints ?? []);

        var combinedTypeFit = Math.Max(typeScore, conceptScore);
        var score = (distanceScore * 0.42d)
                    + (combinedTypeFit * 0.20d)
                    + (brandScore * 0.18d)
                    + (preferenceScore * 0.08d)
                    + (timeScore * 0.06d)
                    + (qualityScore * 0.12d)
                    - exclusionPenalty;
        return Math.Clamp(score, 0d, 1.5d);
    }

    private static double ScoreDistance(double? distanceMeters)
    {
        if (!distanceMeters.HasValue)
        {
            return 0.10d;
        }

        var distance = Math.Max(0d, distanceMeters.Value);
        return distance switch
        {
            <= 500d => 1.00d,
            <= 1_500d => 0.88d,
            <= 3_000d => 0.70d,
            <= 6_000d => 0.45d,
            <= 10_000d => 0.22d,
            _ => 0.08d
        };
    }

    private static double ScoreTypeFit(
        CompanionPlaceCandidate candidate,
        IReadOnlyList<string> placeTypeHints)
    {
        if (placeTypeHints.Count == 0)
        {
            return 0.55d;
        }

        var normalizedCandidateTypes = candidate.Types
            .Select(NormalizeToken)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedPrimaryType = NormalizeToken(candidate.PrimaryType);
        if (!string.IsNullOrWhiteSpace(normalizedPrimaryType))
        {
            normalizedCandidateTypes.Add(normalizedPrimaryType);
        }

        foreach (var hint in placeTypeHints.Select(NormalizeToken))
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            if (string.Equals(hint, "dog_friendly", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hint, "pet_friendly", StringComparison.OrdinalIgnoreCase))
            {
                if (candidate.AllowsDogs == true)
                {
                    return 1d;
                }

                continue;
            }

            if (string.Equals(hint, "brunch", StringComparison.OrdinalIgnoreCase))
            {
                if (candidate.ServesBrunch == true)
                {
                    return 1d;
                }

                continue;
            }

            if (normalizedCandidateTypes.Contains(hint))
            {
                return 1d;
            }
        }

        return 0.25d;
    }

    private static double ScoreConceptFit(
        CompanionPlaceCandidate candidate,
        string? canonicalConcept)
    {
        if (string.IsNullOrWhiteSpace(canonicalConcept))
        {
            return 0.40d;
        }

        var canonical = NormalizeToken(canonicalConcept);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return 0.40d;
        }

        var allTypes = candidate.Types
            .Select(NormalizeToken)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var primary = NormalizeToken(candidate.PrimaryType);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            allTypes.Add(primary);
        }

        if (allTypes.Contains(canonical))
        {
            return 1d;
        }

        return allTypes.Any(type =>
                type.Contains(canonical, StringComparison.OrdinalIgnoreCase)
                || canonical.Contains(type, StringComparison.OrdinalIgnoreCase))
            ? 0.72d
            : 0.24d;
    }

    private static double ScoreBrandFit(
        CompanionPlaceCandidate candidate,
        string? brandTerm)
    {
        if (string.IsNullOrWhiteSpace(brandTerm))
        {
            return 0.45d;
        }

        var brand = brandTerm.Trim();
        if (candidate.DisplayName.Contains(brand, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        if (!string.IsNullOrWhiteSpace(candidate.PrimaryTypeDisplayName)
            && candidate.PrimaryTypeDisplayName.Contains(brand, StringComparison.OrdinalIgnoreCase))
        {
            return 0.70d;
        }

        return 0.12d;
    }

    private static double ScorePreferenceFit(
        CompanionPlaceCandidate candidate,
        IReadOnlyList<string> preferenceHints)
    {
        if (preferenceHints.Count == 0)
        {
            return 0.55d;
        }

        var score = 0.35d;
        foreach (var hint in preferenceHints.Select(NormalizeToken))
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            if (hint is "dog_friendly" or "pet_friendly")
            {
                score = Math.Max(score, candidate.AllowsDogs == true ? 1d : 0.20d);
                continue;
            }

            if (hint == "budget")
            {
                var budgetScore = candidate.PriceLevel is null
                    ? 0.50d
                    : candidate.PriceLevel.Contains("INEXPENSIVE", StringComparison.OrdinalIgnoreCase)
                        ? 1d
                        : candidate.PriceLevel.Contains("MODERATE", StringComparison.OrdinalIgnoreCase)
                            ? 0.65d
                            : 0.20d;
                score = Math.Max(score, budgetScore);
            }
        }

        return score;
    }

    private static double ScoreTimeFit(
        CompanionPlaceCandidate candidate,
        IReadOnlyList<string> timeHints)
    {
        if (timeHints.Count == 0)
        {
            return candidate.OpeningHours.OpenNow.HasValue
                ? candidate.OpeningHours.OpenNow.Value ? 0.90d : 0.25d
                : 0.50d;
        }

        var requiresOpenNow = timeHints.Any(value =>
            string.Equals(value, "open_now", StringComparison.OrdinalIgnoreCase));
        if (!requiresOpenNow)
        {
            return 0.55d;
        }

        return candidate.OpeningHours.OpenNow.HasValue
            ? candidate.OpeningHours.OpenNow.Value ? 1d : 0.05d
            : 0.30d;
    }

    private static double ScoreExclusionPenalty(
        CompanionPlaceCandidate candidate,
        IReadOnlyList<string> excludedTypeHints)
    {
        if (excludedTypeHints.Count == 0)
        {
            return 0d;
        }

        var excludedSet = excludedTypeHints
            .Select(NormalizeToken)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excludedSet.Count == 0)
        {
            return 0d;
        }

        var candidateTypes = candidate.Types
            .Select(NormalizeToken)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var primary = NormalizeToken(candidate.PrimaryType);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            candidateTypes.Add(primary);
        }

        return candidateTypes.Any(candidateType => excludedSet.Contains(candidateType))
            ? 0.38d
            : 0d;
    }

    private static double ScoreQuality(CompanionPlaceCandidate candidate)
    {
        var ratingNormalized = candidate.Rating.HasValue
            ? Math.Clamp((candidate.Rating.Value - 3.0d) / 2.0d, 0d, 1d)
            : 0.40d;
        var ratingCountFactor = candidate.UserRatingCount.HasValue
            ? Math.Clamp(Math.Log10(candidate.UserRatingCount.Value + 1) / 3d, 0d, 1d)
            : 0.25d;

        return (ratingNormalized * 0.7d) + (ratingCountFactor * 0.3d);
    }

    private static double? TryComputeDistanceMeters(
        double sourceLatitude,
        double sourceLongitude,
        PlaceLocationSummary? target)
    {
        if (target is null)
        {
            return null;
        }

        // Haversine distance in meters.
        const double EarthRadiusMeters = 6_371_000d;
        var sourceLatRad = DegreesToRadians(sourceLatitude);
        var targetLatRad = DegreesToRadians(target.Latitude);
        var deltaLat = DegreesToRadians(target.Latitude - sourceLatitude);
        var deltaLon = DegreesToRadians(target.Longitude - sourceLongitude);

        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)
                + (Math.Cos(sourceLatRad) * Math.Cos(targetLatRad)
                   * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d));
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double value)
    {
        return value * (Math.PI / 180d);
    }

    private static string NormalizeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
