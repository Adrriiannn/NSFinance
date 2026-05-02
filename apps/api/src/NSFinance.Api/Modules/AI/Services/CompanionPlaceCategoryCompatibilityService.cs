namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceCategoryCompatibilityService(
    ICompanionPlaceTypeFamilyClassifier typeFamilyClassifier,
    IChatTelemetry telemetry) : ICompanionPlaceCategoryCompatibilityService
{
    public CompanionCategoryCompatibilityResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CompanionPlaceSearchStrategy? strategy = null)
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

            if (IsOfficeRole(intent.Role))
            {
                var entityMatch = MatchesEntityLock(strategy?.Entity, intent.BrandOrEntity, candidate.DisplayName, out var matchedAlias, out var relationshipType);
                if (RequiresEntityLock(strategy?.Entity, intent.BrandOrEntity) && !entityMatch)
                {
                    rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "office_entity_mismatch"));
                    continue;
                }

                if (entityMatch || HasOfficeCompatibleFamily(families))
                {
                    accepted.Add(candidate);
                    _ = telemetry.TrackAsync(
                        "places.office_role.identity_led_category_match",
                        new Dictionary<string, object?>
                        {
                            ["requestedRole"] = intent.Role.RequestedRole,
                            ["entity"] = strategy?.Entity?.CanonicalName ?? intent.BrandOrEntity,
                            ["matchedAlias"] = matchedAlias,
                            ["aliasRelationshipType"] = relationshipType,
                            ["candidateName"] = candidate.DisplayName,
                            ["categoryFamilyWeak"] = !families.Contains("office") && !families.Contains("corporate_office")
                        },
                        CancellationToken.None);
                    continue;
                }

                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "category_role_mismatch"));
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

    private static bool IsOfficeRole(CompanionPlaceRoleIntent role)
    {
        return role.RequestedRole is "office" or "corporate_office" or "headquarters" or "hq"
               || role.AcceptableSubRoles.Any(static item => item is "office" or "corporate_office" or "headquarters" or "hq");
    }

    private static bool HasOfficeCompatibleFamily(IReadOnlySet<string> families)
    {
        return families.Contains("office")
               || families.Contains("corporate_office")
               || families.Contains("business_center")
               || families.Contains("headquarters")
               || families.Contains("establishment")
               || families.Contains("point_of_interest");
    }

    private static bool RequiresEntityLock(CompanionPlaceEntityIntent? entity, string? brandOrEntity)
    {
        return entity?.RequiresEntityLock == true || !string.IsNullOrWhiteSpace(brandOrEntity);
    }

    private static bool MatchesEntityLock(
        CompanionPlaceEntityIntent? entity,
        string? brandOrEntity,
        string candidateName,
        out string? matchedAlias,
        out string? relationshipType)
    {
        matchedAlias = null;
        relationshipType = null;
        var normalizedName = Normalize(candidateName);
        var compactName = Compact(candidateName);

        if (entity is not null)
        {
            foreach (var term in entity.Aliases
                         .Append(entity.CanonicalName)
                         .Append(entity.RawEntityText)
                         .Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                if (ContainsTerm(normalizedName, compactName, term!))
                {
                    matchedAlias = term;
                    return true;
                }
            }

            foreach (var alias in entity.RelationshipAliases)
            {
                if (ContainsTerm(normalizedName, compactName, alias.Name))
                {
                    matchedAlias = alias.Name;
                    relationshipType = alias.RelationshipType;
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(brandOrEntity) && ContainsTerm(normalizedName, compactName, brandOrEntity))
        {
            matchedAlias = brandOrEntity;
            return true;
        }

        return false;
    }

    private static bool ContainsTerm(string normalizedName, string compactName, string term)
    {
        return normalizedName.Contains(Normalize(term), StringComparison.Ordinal)
               || compactName.Contains(Compact(term), StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
    }

    private static string Compact(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
