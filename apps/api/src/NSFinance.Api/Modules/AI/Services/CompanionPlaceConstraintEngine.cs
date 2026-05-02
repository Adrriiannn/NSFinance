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

public sealed class CompanionPlaceConstraintEngine(IChatTelemetry telemetry) : ICompanionPlaceConstraintEngine
{
    public CompanionPlaceConstraintResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        var accepted = new List<CompanionPlacePoolCandidate>();
        var rejected = new List<CompanionPlaceRejectedCandidate>();
        foreach (var candidate in candidates)
        {
            var rejection = EvaluateHardRejection(intent, candidate);
            if (rejection is null)
            {
                accepted.Add(candidate);
            }
            else
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, rejection));
            }
        }

        _ = telemetry.TrackAsync(
            "places.constraint.applied",
            new Dictionary<string, object?>
            {
                ["candidatePoolCount"] = candidates.Count,
                ["hardFilterCount"] = intent.HardFilters.Count,
                ["softPreferenceCount"] = intent.SoftPreferences.Count,
                ["negativeFilterCount"] = intent.NegativeFilters.Count,
                ["rejectedByHardFilterCount"] = rejected.Count
            },
            CancellationToken.None);
        if (accepted.Count == 0 && candidates.Count > 0)
        {
            _ = telemetry.TrackAsync(
                "places.constraint.no_matches",
                new Dictionary<string, object?>
                {
                    ["candidatePoolCount"] = candidates.Count,
                    ["hardFilterCount"] = intent.HardFilters.Count,
                    ["negativeFilterCount"] = intent.NegativeFilters.Count,
                    ["rejectedByHardFilterCount"] = rejected.Count
                },
                CancellationToken.None);
        }

        return new CompanionPlaceConstraintResult(
            Candidates: accepted,
            Rejected: rejected,
            AppliedHardFilters: intent.HardFilters,
            AppliedSoftPreferences: intent.SoftPreferences,
            NonSearchablePreferences: intent.NonSearchablePreferences,
            Diagnostics: accepted.Count == 0 && candidates.Count > 0
                ? ["places_constraint_hard_filters_applied", "no_hard_filter_matches"]
                : rejected.Count > 0 ? ["places_constraint_hard_filters_applied"] : []);
    }

    private static string? EvaluateHardRejection(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var haystack = BuildHaystack(candidate);
        foreach (var negative in intent.NegativeFilters.Select(Normalize))
        {
            if (negative.Length == 0)
            {
                continue;
            }

            if (negative is "mcdonalds" && haystack.Contains("mcdonald", StringComparison.Ordinal))
            {
                return "negative_filter:mcdonalds";
            }

            if ((negative.Contains("fast_food", StringComparison.Ordinal) || negative.Contains("fast food", StringComparison.Ordinal))
                && (haystack.Contains("fast food", StringComparison.Ordinal)
                    || haystack.Contains("fast_food", StringComparison.Ordinal)
                    || haystack.Contains("fast_food_restaurant", StringComparison.Ordinal)))
            {
                return "negative_filter:fast_food";
            }

            if (negative.Contains("takeaway", StringComparison.Ordinal)
                && (haystack.Contains("takeaway", StringComparison.Ordinal)
                    || haystack.Contains("meal_takeaway", StringComparison.Ordinal)))
            {
                return "negative_filter:takeaway";
            }
        }

        foreach (var filter in intent.HardFilters)
        {
            var normalized = Normalize(filter);
            if (normalized == "open now" || normalized == "open_now")
            {
                if (candidate.OpenNow != true)
                {
                    return "hard_filter:open_now";
                }
            }
            else if (normalized.StartsWith("rating>=", StringComparison.Ordinal)
                     && double.TryParse(normalized["rating>=".Length..], CultureInfo.InvariantCulture, out var minRating)
                     && (candidate.Rating ?? 0d) < minRating)
            {
                return "hard_filter:rating";
            }
            else if (normalized.Contains("parking", StringComparison.Ordinal)
                     && HasParkingEvidence(candidate) == false)
            {
                return "hard_filter:parking";
            }
        }

        return null;
    }

    private static bool HasParkingEvidence(CompanionPlacePoolCandidate candidate)
    {
        return BuildHaystack(candidate).Contains("parking", StringComparison.Ordinal);
    }

    private static string BuildHaystack(CompanionPlacePoolCandidate candidate)
    {
        return Normalize(string.Join(
            ' ',
            candidate.DisplayName,
            candidate.PrimaryType,
            candidate.PrimaryTypeDisplayName,
            string.Join(' ', candidate.Types),
            candidate.ShortFormattedAddress,
            string.Join(' ', candidate.LightweightAttributes.Values)));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
    }
}
