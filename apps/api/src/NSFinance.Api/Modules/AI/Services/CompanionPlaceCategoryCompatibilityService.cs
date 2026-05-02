namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceCategoryCompatibilityService(IChatTelemetry telemetry) : ICompanionPlaceCategoryCompatibilityService
{
    public CompanionCategoryCompatibilityResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        if (intent.Role.CategoryStrictness == "loose"
            && intent.Role.ExcludedSiblingRoles.Count == 0
            && intent.Role.RequiredCoreRoles.Count == 0)
        {
            return new CompanionCategoryCompatibilityResult(candidates, [], []);
        }

        var accepted = new List<CompanionPlacePoolCandidate>();
        var rejected = new List<CompanionPlaceRejectedCandidate>();
        foreach (var candidate in candidates)
        {
            var haystack = Haystack(candidate);
            var excluded = intent.Role.ExcludedSiblingRoles.FirstOrDefault(role => ContainsRole(haystack, role));
            if (!string.IsNullOrWhiteSpace(excluded))
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, $"excluded_sibling_role:{excluded}"));
                continue;
            }

            if (intent.Role.CategoryStrictness == "strict"
                && !intent.Role.RequiredCoreRoles.Any(role => ContainsRole(haystack, role))
                && !intent.Role.AcceptableSubRoles.Any(role => ContainsRole(haystack, role)))
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "category_role_mismatch"));
                continue;
            }

            if (intent.Role.CategoryStrictness == "compatible"
                && intent.Role.RequiredCoreRoles.Count > 0
                && !intent.Role.RequiredCoreRoles.Any(role => ContainsRole(haystack, role))
                && !intent.Role.AcceptableSubRoles.Any(role => ContainsRole(haystack, role)))
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "category_role_mismatch"));
                continue;
            }

            accepted.Add(candidate);
        }

        _ = telemetry.TrackAsync(
            "places.category_compatibility.applied",
            new Dictionary<string, object?>
            {
                ["requestedRole"] = intent.Role.RequestedRole,
                ["strictness"] = intent.Role.CategoryStrictness,
                ["candidateCount"] = candidates.Count,
                ["rejectedCount"] = rejected.Count
            },
            CancellationToken.None);

        return new CompanionCategoryCompatibilityResult(
            accepted,
            rejected,
            rejected.Count > 0 ? ["places_category_compatibility_applied"] : []);
    }

    private static bool ContainsRole(string haystack, string role)
    {
        var normalized = Normalize(role);
        if (normalized == "financial institution")
        {
            return haystack.Contains("bank", StringComparison.Ordinal)
                   || haystack.Contains("financial institution", StringComparison.Ordinal);
        }

        if (normalized == "parking")
        {
            return haystack.Contains("parking", StringComparison.Ordinal)
                   || haystack.Contains("car park", StringComparison.Ordinal);
        }

        return haystack.Contains(normalized, StringComparison.Ordinal);
    }

    private static string Haystack(CompanionPlacePoolCandidate candidate)
    {
        return Normalize(string.Join(' ', candidate.DisplayName, candidate.PrimaryType, candidate.PrimaryTypeDisplayName, string.Join(' ', candidate.Types)));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
    }
}
