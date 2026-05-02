using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceIntelligenceRankingService : ICompanionPlaceIntelligenceRankingService
{
    public CompanionPlaceIntelligenceRankingResult Rank(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        var ranked = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = Score(intent, candidate)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => intent.RankingGoal.Contains("distance", StringComparison.OrdinalIgnoreCase)
                ? item.Candidate.DistanceMeters ?? double.MaxValue
                : double.MaxValue)
            .ThenByDescending(item => item.Candidate.Rating ?? 0d)
            .ThenBy(item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Candidate)
            .ToArray();

        return new CompanionPlaceIntelligenceRankingResult(
            RankedCandidates: ranked,
            Diagnostics: ["places_ranking_v2_completed"]);
    }

    private static double Score(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var concept = ScoreConcept(intent, candidate);
        var distance = ScoreDistance(candidate.DistanceMeters);
        var rating = ScoreRating(candidate);
        var open = candidate.OpenNow == true ? 0.08d : 0d;
        var soft = ScoreSoftPreferences(intent, candidate);

        if (intent.RankingGoal == "brand_match_then_distance")
        {
            return (ScoreBrand(intent, candidate) * 0.60d) + (distance * 0.25d) + (rating * 0.15d);
        }

        if (intent.RankingGoal == "concept_fit_then_distance")
        {
            return (concept * 0.52d) + (rating * 0.22d) + (distance * 0.16d) + (soft * 0.10d);
        }

        if (intent.RankingGoal == "relevance_rating_then_distance")
        {
            return (concept * 0.45d) + (rating * 0.35d) + (distance * 0.12d) + open;
        }

        if (intent.RankingGoal == "parking_match_then_distance")
        {
            return (ScoreParking(candidate) * 0.55d) + (distance * 0.30d) + (rating * 0.15d);
        }

        if (intent.RankingGoal == "distance")
        {
            return (distance * 0.70d) + (concept * 0.20d) + (rating * 0.10d);
        }

        return (concept * 0.40d) + (distance * 0.30d) + (rating * 0.20d) + (soft * 0.10d) + open;
    }

    private static double ScoreBrand(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(intent.BrandOrEntity))
        {
            return 0.45d;
        }

        return candidate.DisplayName.Contains(intent.BrandOrEntity, StringComparison.OrdinalIgnoreCase) ? 1d : 0.05d;
    }

    private static double ScoreConcept(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var query = Normalize(intent.PlaceQuery);
        var haystack = Normalize($"{candidate.DisplayName} {candidate.PrimaryType} {candidate.PrimaryTypeDisplayName} {string.Join(' ', candidate.Types)} {string.Join(' ', candidate.LightweightAttributes.Values)}");
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0.50d;
        }

        if (query.Contains("fine dining", StringComparison.Ordinal)
            || intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
        {
            var upscale = haystack.Contains("fine dining", StringComparison.Ordinal)
                          || haystack.Contains("upscale", StringComparison.Ordinal)
                          || haystack.Contains("restaurant", StringComparison.Ordinal)
                          && (candidate.PriceLevel?.Contains("EXPENSIVE", StringComparison.OrdinalIgnoreCase) == true
                              || candidate.LightweightAttributes.TryGetValue("reservable", out var reservable)
                              && bool.TryParse(reservable, out var parsed)
                              && parsed);
            return upscale ? 1d : 0.20d;
        }

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 4 && haystack.Contains(token, StringComparison.Ordinal))
            {
                return 0.92d;
            }
        }

        return 0.35d;
    }

    private static double ScoreParking(CompanionPlacePoolCandidate candidate)
    {
        var haystack = Normalize($"{candidate.PrimaryType} {candidate.PrimaryTypeDisplayName} {string.Join(' ', candidate.Types)} {string.Join(' ', candidate.LightweightAttributes.Values)}");
        return haystack.Contains("parking", StringComparison.Ordinal) || haystack.Contains("car park", StringComparison.Ordinal)
            ? 1d
            : 0.25d;
    }

    private static double ScoreSoftPreferences(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var score = 0.40d;
        if (intent.SoftPreferences.Contains("not_too_expensive", StringComparer.OrdinalIgnoreCase))
        {
            score = Math.Max(score, candidate.PriceLevel is null
                ? 0.50d
                : candidate.PriceLevel.Contains("INEXPENSIVE", StringComparison.OrdinalIgnoreCase)
                  || candidate.PriceLevel.Contains("MODERATE", StringComparison.OrdinalIgnoreCase)
                    ? 1d
                    : 0.15d);
        }

        if (intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
        {
            score = Math.Max(score, candidate.PriceLevel?.Contains("EXPENSIVE", StringComparison.OrdinalIgnoreCase) == true ? 0.80d : 0.50d);
        }

        return score;
    }

    private static double ScoreDistance(double? distanceMeters)
    {
        if (!distanceMeters.HasValue)
        {
            return 0.15d;
        }

        var distance = Math.Max(0d, distanceMeters.Value);
        return distance switch
        {
            <= 500d => 1.00d,
            <= 1_500d => 0.88d,
            <= 3_000d => 0.70d,
            <= 6_000d => 0.48d,
            <= 10_000d => 0.26d,
            _ => 0.08d
        };
    }

    private static double ScoreRating(CompanionPlacePoolCandidate candidate)
    {
        var rating = candidate.Rating.HasValue
            ? Math.Clamp((candidate.Rating.Value - 3.0d) / 2.0d, 0d, 1d)
            : 0.35d;
        var count = candidate.UserRatingCount.HasValue
            ? Math.Clamp(Math.Log10(candidate.UserRatingCount.Value + 1) / 3d, 0d, 1d)
            : 0.20d;
        return (rating * 0.75d) + (count * 0.25d);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
    }
}
