namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceBrandIdentityService(IChatTelemetry telemetry) : ICompanionPlaceBrandIdentityService
{
    public CompanionBrandIdentityResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CompanionPlaceSearchStrategy? strategy = null)
    {
        var entity = strategy?.Entity;
        var lockTerms = ResolveLockTerms(intent, entity);
        if (lockTerms.Count == 0)
        {
            return new CompanionBrandIdentityResult(candidates, [], []);
        }

        var accepted = new List<CompanionPlacePoolCandidate>();
        var rejected = new List<CompanionPlaceRejectedCandidate>();
        foreach (var candidate in candidates)
        {
            if (MatchesBrand(lockTerms, candidate.DisplayName))
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
                ["brand"] = entity?.CanonicalName ?? intent.BrandOrEntity,
                ["verificationStatus"] = entity?.VerificationStatus,
                ["candidateCount"] = candidates.Count,
                ["rejectedByBrandMismatch"] = rejected.Count
            },
            CancellationToken.None);

        return new CompanionBrandIdentityResult(
            accepted,
            rejected,
            rejected.Count > 0 ? ["places_brand_identity_applied"] : []);
    }

    private static IReadOnlyList<string> ResolveLockTerms(CompanionSemanticIntent intent, CompanionPlaceEntityIntent? entity)
    {
        if (entity is not null)
        {
            if (entity.VerificationStatus is "rejected" or "ambiguous")
            {
                return [];
            }

            return entity.Aliases
                .Append(entity.CanonicalName)
                .Append(entity.RawEntityText)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(intent.BrandOrEntity) ? [] : [intent.BrandOrEntity!];
    }

    private static bool MatchesBrand(IReadOnlyList<string> lockTerms, string name)
    {
        var normalizedName = Normalize(name);
        var compactName = Compact(name);
        foreach (var term in lockTerms)
        {
            var normalizedTerm = Normalize(term);
            if (normalizedName.Contains(normalizedTerm, StringComparison.Ordinal)
                || compactName.Contains(Compact(term), StringComparison.Ordinal)
                || Acronym(name).Equals(Compact(term), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("&", "and");
    }

    private static string Compact(string value)
    {
        return new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private static string Acronym(string value)
    {
        return new string(value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static word => word.Length > 0)
            .Select(static word => char.ToLowerInvariant(word[0]))
            .ToArray());
    }
}
