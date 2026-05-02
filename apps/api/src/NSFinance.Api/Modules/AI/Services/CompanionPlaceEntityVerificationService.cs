using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceEntityVerificationService(
    ICompanionPlaceDiscoveryService discoveryService,
    ICompanionPlaceTypeFamilyClassifier typeFamilyClassifier,
    IChatTelemetry telemetry) : ICompanionPlaceEntityVerificationService
{
    public async Task<CompanionPlaceEntityVerificationResult> VerifyAsync(
        CompanionPlaceSearchStrategy strategy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        if (strategy.Entity is null || !strategy.Entity.VerificationRequired)
        {
            return new CompanionPlaceEntityVerificationResult(
                strategy.Entity,
                strategy.SearchVariants,
                "not_required",
                [],
                []);
        }

        await telemetry.TrackAsync(
            "places.entity_verification.started",
            new Dictionary<string, object?>
            {
                ["canonicalQuery"] = strategy.CanonicalQuery,
                ["rawEntityText"] = strategy.Entity.RawEntityText,
                ["canonicalEntity"] = strategy.Entity.CanonicalName,
                ["requestedRole"] = strategy.Role.RequestedRole
            },
            cancellationToken);

        var evidence = new List<string>();
        var warnings = new List<string>();
        var variantsToProbe = strategy.SearchVariants.Take(2).ToArray();
        var matchedEntity = new List<CompanionPlaceCandidate>();
        var matchedRole = new List<CompanionPlaceCandidate>();
        foreach (var variant in variantsToProbe)
        {
            var result = await discoveryService.DiscoverAsync(
                new CompanionPlaceDiscoveryRequest(
                    Query: variant.Query,
                    CountryCode: null,
                    Latitude: strategy.Location.Latitude,
                    Longitude: strategy.Location.Longitude,
                    RadiusMeters: strategy.Location.Latitude.HasValue ? 15_000 : null,
                    MaxCandidates: 5),
                cancellationToken);
            foreach (var candidate in result.Candidates)
            {
                if (!MatchesEntity(strategy.Entity, candidate.DisplayName))
                {
                    continue;
                }

                matchedEntity.Add(candidate);
                if (MatchesRole(strategy.Role, candidate))
                {
                    matchedRole.Add(candidate);
                }
            }
        }

        var status = ResolveStatus(strategy, matchedEntity, matchedRole);
        if (matchedEntity.Count > 0)
        {
            evidence.Add($"entity_name_match:{matchedEntity.Count}");
        }

        if (matchedRole.Count > 0)
        {
            evidence.Add($"entity_role_match:{matchedRole.Count}");
        }

        CompanionPlaceEntityIntent? entity = strategy.Entity with
        {
            VerificationStatus = status
        };
        if (status == "verified")
        {
            var inferred = InferEntityName(strategy.Entity, matchedRole.FirstOrDefault() ?? matchedEntity.First());
            var aliases = strategy.Entity.Aliases
                .Append(strategy.Entity.CanonicalName)
                .Append(inferred)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            entity = strategy.Entity with
            {
                CanonicalName = inferred ?? strategy.Entity.CanonicalName,
                Aliases = aliases,
                VerificationStatus = "verified"
            };
        }

        if (status is "pending" or "ambiguous")
        {
            warnings.Add($"entity_verification_{status}");
        }

        await telemetry.TrackAsync(
            "places.entity_verification.completed",
            new Dictionary<string, object?>
            {
                ["canonicalQuery"] = strategy.CanonicalQuery,
                ["canonicalEntity"] = entity?.CanonicalName,
                ["requestedRole"] = strategy.Role.RequestedRole,
                ["verificationStatus"] = status,
                ["entityEvidenceCount"] = matchedEntity.Count,
                ["roleEvidenceCount"] = matchedRole.Count
            },
            cancellationToken);

        return new CompanionPlaceEntityVerificationResult(
            entity,
            entity is null ? strategy.SearchVariants : RewriteVariants(strategy, entity),
            status,
            evidence,
            warnings);
    }

    private static string ResolveStatus(
        CompanionPlaceSearchStrategy strategy,
        IReadOnlyList<CompanionPlaceCandidate> matchedEntity,
        IReadOnlyList<CompanionPlaceCandidate> matchedRole)
    {
        if (matchedRole.Count > 0)
        {
            return "verified";
        }

        if (matchedEntity.Count > 0)
        {
            return strategy.Role.CategoryStrictness == "loose" ? "verified" : "ambiguous";
        }

        return strategy.Entity?.Confidence >= 0.80d ? "pending" : "rejected";
    }

    private static IReadOnlyList<CompanionPlaceSearchVariant> RewriteVariants(
        CompanionPlaceSearchStrategy strategy,
        CompanionPlaceEntityIntent entity)
    {
        if (string.IsNullOrWhiteSpace(entity.CanonicalName))
        {
            return strategy.SearchVariants;
        }

        var roleQuery = strategy.Role.RequestedRole switch
        {
            "bank_branch" => "bank",
            "atm" => "ATM",
            "parking" => "parking",
            "gas_station" => "petrol station",
            "post_office" => "post office",
            "coffee_shop" => "coffee shop",
            "restaurant" => "restaurant",
            _ => strategy.Role.RequestedRole
        };
        return entity.Aliases
            .Append(entity.CanonicalName)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((alias, index) => new CompanionPlaceSearchVariant(
                string.IsNullOrWhiteSpace(roleQuery) ? alias! : $"{alias} {roleQuery}",
                index == 0 ? "primary" : "alias",
                true,
                strategy.Role.CategoryStrictness != "loose",
                0.9d))
            .ToArray();
    }

    private bool MatchesRole(CompanionPlaceRoleIntent role, CompanionPlaceCandidate candidate)
    {
        if (role.CategoryStrictness == "loose" && role.RequiredCoreRoles.Count == 0 && role.AcceptableSubRoles.Count == 0)
        {
            return true;
        }

        var families = typeFamilyClassifier.ClassifyFamilies(new CompanionPlacePoolCandidate(
            candidate.PlaceId,
            candidate.DisplayName,
            candidate.PrimaryType,
            candidate.PrimaryTypeDisplayName,
            candidate.Types,
            candidate.Location?.Latitude,
            candidate.Location?.Longitude,
            DistanceMeters: null,
            candidate.ShortFormattedAddress,
            candidate.Rating,
            candidate.UserRatingCount,
            candidate.PriceLevel,
            candidate.OpeningHours.OpenNow,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        if (role.ExcludedSiblingRoles.Any(item => families.Contains(NormalizeRole(item))))
        {
            return false;
        }

        return role.RequiredCoreRoles.Concat(role.AcceptableSubRoles)
            .Select(NormalizeRole)
            .Any(families.Contains);
    }

    private static bool MatchesEntity(CompanionPlaceEntityIntent entity, string candidateName)
    {
        var candidate = Normalize(candidateName);
        var compactCandidate = Compact(candidateName);
        return entity.Aliases
            .Append(entity.CanonicalName)
            .Append(entity.RawEntityText)
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Any(alias =>
            {
                var normalizedAlias = Normalize(alias);
                return candidate.Contains(normalizedAlias, StringComparison.Ordinal)
                       || compactCandidate.Contains(Compact(alias), StringComparison.Ordinal);
            });
    }

    private static string? InferEntityName(CompanionPlaceEntityIntent entity, CompanionPlaceCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.DisplayName))
        {
            return entity.CanonicalName;
        }

        var rawCompact = Compact(entity.RawEntityText ?? entity.CanonicalName);
        var words = candidate.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var length = Math.Min(3, words.Length); length >= 1; length--)
        {
            var prefix = string.Join(' ', words.Take(length)).Trim();
            if (Compact(prefix).Contains(rawCompact, StringComparison.Ordinal)
                || rawCompact.Contains(Compact(prefix), StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        return entity.CanonicalName;
    }

    private static string NormalizeRole(string value) => Normalize(value).Replace("financial institution", "financial_institution").Replace(' ', '_');

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }

    private static string Compact(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
    }
}
