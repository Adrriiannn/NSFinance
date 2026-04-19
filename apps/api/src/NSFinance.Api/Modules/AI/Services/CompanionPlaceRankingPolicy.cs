namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionPlaceRankingContext(
    bool ApplyDistanceRanking,
    double? UserLatitude,
    double? UserLongitude,
    IReadOnlyList<string> PlaceTypeHints);

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

            var score = ComputeScore(candidate, distanceMeters, context.PlaceTypeHints);
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
        IReadOnlyList<string> placeTypeHints)
    {
        var distanceScore = ScoreDistance(distanceMeters);
        var typeScore = ScoreTypeFit(candidate, placeTypeHints);
        var qualityScore = ScoreQuality(candidate);
        var openNowScore = candidate.OpeningHours.OpenNow.HasValue
            ? candidate.OpeningHours.OpenNow.Value ? 1d : 0.20d
            : 0.50d;

        return (distanceScore * 0.58d)
               + (typeScore * 0.22d)
               + (qualityScore * 0.14d)
               + (openNowScore * 0.06d);
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
