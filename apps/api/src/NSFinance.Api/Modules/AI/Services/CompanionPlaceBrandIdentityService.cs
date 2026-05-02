namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceBrandIdentityService(IChatTelemetry telemetry) : ICompanionPlaceBrandIdentityService
{
    public CompanionBrandIdentityResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(intent.BrandOrEntity))
        {
            return new CompanionBrandIdentityResult(candidates, [], []);
        }

        var accepted = new List<CompanionPlacePoolCandidate>();
        var rejected = new List<CompanionPlaceRejectedCandidate>();
        foreach (var candidate in candidates)
        {
            if (MatchesBrand(intent.BrandOrEntity!, candidate.DisplayName))
            {
                accepted.Add(candidate);
            }
            else
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "brand_mismatch"));
            }
        }

        _ = telemetry.TrackAsync(
            "places.brand_identity.applied",
            new Dictionary<string, object?>
            {
                ["brand"] = intent.BrandOrEntity,
                ["candidateCount"] = candidates.Count,
                ["rejectedByBrandMismatch"] = rejected.Count
            },
            CancellationToken.None);

        return new CompanionBrandIdentityResult(
            accepted,
            rejected,
            rejected.Count > 0 ? ["places_brand_identity_applied"] : []);
    }

    private static bool MatchesBrand(string brand, string name)
    {
        var normalizedBrand = Normalize(brand);
        var normalizedName = Normalize(name);
        if (normalizedName.Contains(normalizedBrand, StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedBrand is "aib" or "allied irish bank")
        {
            return normalizedName.Contains("aib", StringComparison.Ordinal)
                   || normalizedName.Contains("allied irish bank", StringComparison.Ordinal);
        }

        return false;
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("&", "and");
    }
}
