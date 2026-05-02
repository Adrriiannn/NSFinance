namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceCategoryCompatibilityService(
    ICompanionPlaceTypeFamilyClassifier typeFamilyClassifier,
    IChatTelemetry telemetry) : ICompanionPlaceCategoryCompatibilityService
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
            var families = typeFamilyClassifier.ClassifyFamilies(candidate);
            var excluded = intent.Role.ExcludedSiblingRoles.FirstOrDefault(role => ContainsRole(families, role));
            if (!string.IsNullOrWhiteSpace(excluded))
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, $"excluded_sibling_role:{excluded}"));
                continue;
            }

            if (intent.Role.CategoryStrictness == "strict"
                && !intent.Role.RequiredCoreRoles.Any(role => ContainsRole(families, role))
                && !intent.Role.AcceptableSubRoles.Any(role => ContainsRole(families, role)))
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "category_role_mismatch"));
                continue;
            }

            if (intent.Role.CategoryStrictness == "compatible"
                && intent.Role.RequiredCoreRoles.Count > 0
                && !intent.Role.RequiredCoreRoles.Any(role => ContainsRole(families, role))
                && !intent.Role.AcceptableSubRoles.Any(role => ContainsRole(families, role)))
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

    private static bool ContainsRole(IReadOnlySet<string> families, string role)
    {
        var normalized = Normalize(role);
        if (normalized == "financial institution")
        {
            return families.Contains("bank")
                   || families.Contains("financial_institution");
        }

        if (normalized is "parking" or "parking lot" or "parking garage")
        {
            return families.Contains("parking");
        }

        if (normalized is "cafe" or "coffee shop")
        {
            return families.Contains("cafe") || families.Contains("coffee_shop");
        }

        return families.Contains(normalized.Replace(' ', '_')) || families.Contains(normalized);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
    }
}
