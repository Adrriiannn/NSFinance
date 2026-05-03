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
        if (IsAccommodationRole(intent.Role))
        {
            _ = telemetry.TrackAsync(
                "places.accommodation_role.normalized",
                new Dictionary<string, object?>
                {
                    ["requestedRole"] = intent.Role.RequestedRole,
                    ["categoryStrictness"] = intent.Role.CategoryStrictness,
                    ["requiredCoreRoles"] = intent.Role.RequiredCoreRoles.ToArray(),
                    ["acceptableSubRoles"] = intent.Role.AcceptableSubRoles.ToArray(),
                    ["excludedSiblingRoles"] = intent.Role.ExcludedSiblingRoles.ToArray()
                },
                CancellationToken.None);
        }

        foreach (var candidate in candidates)
        {
            var families = typeFamilyClassifier.ClassifyFamilies(candidate);
            var detailsFamilies = candidate.HasProviderTypedRoleEvidence
                ? typeFamilyClassifier.ClassifyFamilies(candidate with
                {
                    RetrievalIncludedTypes = [],
                    RetrievalRoleFamilies = [],
                    HasProviderTypedRoleEvidence = false
                })
                : families;
            var hasRequiredByDetails = intent.Role.RequiredCoreRoles.Any(role => ContainsRole(detailsFamilies, role))
                                       || intent.Role.AcceptableSubRoles.Any(role => ContainsRole(detailsFamilies, role));
            var hasProviderTypedRoleEvidence = RoleSatisfiedByRetrievalEvidence(intent.Role, candidate);
            var excluded = intent.Role.ExcludedSiblingRoles.FirstOrDefault(role => ContainsRole(detailsFamilies, role));
            if (!string.IsNullOrWhiteSpace(excluded) && !hasRequiredByDetails)
            {
                if (IsAtmRole(intent.Role)
                    && families.Contains("bank")
                    && MatchesEntityLock(strategy?.Entity, intent.BrandOrEntity, candidate.DisplayName, out _, out _))
                {
                    accepted.Add(candidate);
                    _ = telemetry.TrackAsync(
                        "places.role_compatibility.mixed_role_applied",
                        new Dictionary<string, object?>
                        {
                            ["requestedRole"] = intent.Role.RequestedRole,
                            ["candidateName"] = candidate.DisplayName,
                            ["families"] = families.ToArray(),
                            ["status"] = "unknown",
                            ["needsDetails"] = true,
                            ["reason"] = "bank_family_candidate_kept_for_atm_evidence"
                        },
                        CancellationToken.None);
                    continue;
                }

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
                && !hasRequiredByDetails)
            {
                if (hasProviderTypedRoleEvidence && !HasConfirmedRoleConflict(intent.Role, detailsFamilies))
                {
                    accepted.Add(candidate);
                    TrackProviderTypedEvidenceAccepted(intent, candidate, families);
                    continue;
                }

                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "category_role_mismatch"));
                TrackAccommodationCandidate(intent.Role, candidate, families, accepted: false, "category_role_mismatch");
                continue;
            }

            if (intent.Role.CategoryStrictness == "compatible"
                && intent.Role.RequiredCoreRoles.Count > 0
                && !hasRequiredByDetails)
            {
                if (hasProviderTypedRoleEvidence && !HasConfirmedRoleConflict(intent.Role, detailsFamilies))
                {
                    accepted.Add(candidate);
                    TrackProviderTypedEvidenceAccepted(intent, candidate, families);
                    continue;
                }

                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, "category_role_mismatch"));
                TrackAccommodationCandidate(intent.Role, candidate, families, accepted: false, "category_role_mismatch");
                continue;
            }

            accepted.Add(candidate);
            TrackAccommodationCandidate(intent.Role, candidate, families, accepted: true, null);
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
        var compact = normalized.Replace(' ', '_');
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

        if (normalized is "ev charging" or "ev charger" or "electric vehicle charging station")
        {
            return families.Contains("ev_charging") || families.Contains("electric_vehicle_charging_station");
        }

        return families.Contains(compact) || families.Contains(normalized);
    }

    private void TrackProviderTypedEvidenceAccepted(
        CompanionSemanticIntent intent,
        CompanionPlacePoolCandidate candidate,
        IReadOnlySet<string> families)
    {
        _ = telemetry.TrackAsync(
            "places.category_compatibility.provider_typed_evidence_accepted",
            new Dictionary<string, object?>
            {
                ["placeId"] = candidate.PlaceId,
                ["name"] = candidate.DisplayName,
                ["requestedRole"] = intent.Role.RequestedRole,
                ["requiredCoreRoles"] = intent.Role.RequiredCoreRoles.ToArray(),
                ["retrievalIncludedTypes"] = candidate.RetrievalIncludedTypes.ToArray(),
                ["retrievalRoleFamilies"] = candidate.RetrievalRoleFamilies.ToArray(),
                ["detailsFamilies"] = families.ToArray()
            },
            CancellationToken.None);
    }

    private void TrackAccommodationCandidate(
        CompanionPlaceRoleIntent role,
        CompanionPlacePoolCandidate candidate,
        IReadOnlySet<string> families,
        bool accepted,
        string? rejectionReason)
    {
        if (!IsAccommodationRole(role))
        {
            return;
        }

        _ = telemetry.TrackAsync(
            "places.accommodation_candidate.classified",
            new Dictionary<string, object?>
            {
                ["placeId"] = candidate.PlaceId,
                ["name"] = candidate.DisplayName,
                ["primaryType"] = candidate.PrimaryType,
                ["primaryTypeDisplayName"] = candidate.PrimaryTypeDisplayName,
                ["types"] = candidate.Types.ToArray(),
                ["families"] = families.ToArray(),
                ["accepted"] = accepted,
                ["rejectionReason"] = rejectionReason
            },
            CancellationToken.None);
    }

    private static bool RoleSatisfiedByRetrievalEvidence(CompanionPlaceRoleIntent role, CompanionPlacePoolCandidate candidate)
    {
        if (!candidate.HasProviderTypedRoleEvidence)
        {
            return false;
        }

        var retrievalFamilies = candidate.RetrievalRoleFamilies.Concat(candidate.RetrievalIncludedTypes.Select(Normalize)).ToArray();
        bool Has(string value) => retrievalFamilies.Any(item => string.Equals(Normalize(item).Replace(' ', '_'), Normalize(value).Replace(' ', '_'), StringComparison.OrdinalIgnoreCase));

        if (IsStrictHotelRole(role))
        {
            return false;
        }

        return role.RequiredCoreRoles.Concat(role.AcceptableSubRoles).Any(roleValue =>
                   Has(roleValue)
                   || (Normalize(roleValue) is "ev charging" or "electric vehicle charging station" && Has("ev_charging")))
               || (role.RequestedRole is not null && Has(role.RequestedRole));
    }

    private static bool HasConfirmedRoleConflict(CompanionPlaceRoleIntent role, IReadOnlySet<string> families)
    {
        if (role.RequestedRole is "ev_charging" or "ev_charging_station" or "electric_vehicle_charging_station")
        {
            return families.Contains("restaurant")
                   || families.Contains("cafe")
                   || families.Contains("hotel")
                   || families.Contains("motel");
        }

        return role.ExcludedSiblingRoles.Any(excluded => ContainsRole(families, excluded));
    }

    private static bool IsAccommodationRole(CompanionPlaceRoleIntent role)
    {
        return role.RequestedRole is "hotel" or "motel" or "lodging" or "accommodation" or "guesthouse" or "aparthotel"
               || role.RequiredCoreRoles.Concat(role.AcceptableSubRoles).Concat(role.ExcludedSiblingRoles)
                   .Any(static item => item is "hotel" or "motel" or "lodging" or "accommodation" or "guesthouse" or "aparthotel" or "private_accommodation" or "student_accommodation");
    }

    private static bool IsStrictHotelRole(CompanionPlaceRoleIntent role)
    {
        return role.CategoryStrictness == "strict" && role.RequestedRole == "hotel";
    }

    private static bool IsOfficeRole(CompanionPlaceRoleIntent role)
    {
        return role.RequestedRole is "office" or "corporate_office" or "headquarters" or "hq"
               || role.AcceptableSubRoles.Any(static item => item is "office" or "corporate_office" or "headquarters" or "hq");
    }

    private static bool IsAtmRole(CompanionPlaceRoleIntent role)
    {
        return role.RequestedRole == "atm"
               || role.RequiredCoreRoles.Contains("atm", StringComparer.OrdinalIgnoreCase)
               || role.AcceptableSubRoles.Contains("atm", StringComparer.OrdinalIgnoreCase);
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
