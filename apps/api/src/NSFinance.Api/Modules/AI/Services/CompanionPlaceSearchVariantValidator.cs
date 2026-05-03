using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceSearchVariantValidator(IChatTelemetry telemetry) : ICompanionPlaceSearchVariantValidator
{
    private const int MaxVariantLength = 80;

    public IReadOnlyList<CompanionPlaceSearchVariant> Validate(CompanionPlaceSearchStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var accepted = new List<CompanionPlaceSearchVariant>();
        var rejected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in strategy.SearchVariants)
        {
            var rejection = GetRejectionReason(strategy, variant, seen);
            if (rejection is not null)
            {
                rejected.Add($"{variant.Query}:{rejection}");
                continue;
            }

            seen.Add(Normalize(variant.Query));
            accepted.Add(variant with { Query = Regex.Replace(variant.Query.Trim(), @"\s+", " ") });
        }

        _ = telemetry.TrackAsync(
            "places.search_variants.validated",
            new Dictionary<string, object?>
            {
                ["canonicalQuery"] = strategy.CanonicalQuery,
                ["validatedVariants"] = accepted.Select(static item => item.Query).ToArray(),
                ["validatedVariantCount"] = accepted.Count
            },
            CancellationToken.None);
        if (rejected.Count > 0)
        {
            _ = telemetry.TrackAsync(
                "places.search_variants.rejected",
                new Dictionary<string, object?>
                {
                    ["canonicalQuery"] = strategy.CanonicalQuery,
                    ["rejectedVariants"] = rejected.ToArray()
                },
                CancellationToken.None);
        }

        return accepted.Count > 0
            ? accepted
            : BuildFallbackVariant(strategy);
    }

    private static string? GetRejectionReason(
        CompanionPlaceSearchStrategy strategy,
        CompanionPlaceSearchVariant variant,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(variant.Query))
        {
            return "empty";
        }

        var normalized = Normalize(variant.Query);
        if (seen.Contains(normalized))
        {
            return "duplicate";
        }

        if (variant.Query.Length > MaxVariantLength || LooksConversational(variant.Query))
        {
            return "not_search_query";
        }

        if (AddsCoffeeTermsForNonCoffeeRole(strategy, normalized))
        {
            return "coffee_variant_wrong_role";
        }

        if (variant.RequiresEntityMatch && strategy.Entity is not null && !ContainsEntityOrAlias(strategy.Entity, normalized))
        {
            return "missing_entity";
        }

        if (ContradictsRole(strategy.Role, normalized))
        {
            return "contradicts_role";
        }

        if (ContradictsStrictAccommodationRole(strategy.Role, normalized))
        {
            return "contradicts_accommodation_role";
        }

        if (IsTooBroadGeneric(strategy, normalized))
        {
            return "too_broad";
        }

        return null;
    }

    private static IReadOnlyList<CompanionPlaceSearchVariant> BuildFallbackVariant(CompanionPlaceSearchStrategy strategy)
    {
        var query = strategy.CanonicalQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            query = strategy.Role.RequestedRole ?? "local places";
        }

        return [new CompanionPlaceSearchVariant(query, "primary", strategy.Entity is not null, strategy.Role.CategoryStrictness != "loose", 0.55d)];
    }

    private static bool AddsCoffeeTermsForNonCoffeeRole(CompanionPlaceSearchStrategy strategy, string normalized)
    {
        return strategy.Role.RequestedRole != "coffee_shop"
               && (HasWord(normalized, "coffee") || HasWord(normalized, "cafe") || HasWord(normalized, "cafes"));
    }

    private static bool ContainsEntityOrAlias(CompanionPlaceEntityIntent entity, string normalizedQuery)
    {
            return entity.Aliases
            .Append(entity.CanonicalName)
            .Append(entity.RawEntityText)
            .Concat(entity.RelationshipAliases.Select(static item => item.Name))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Any(alias => normalizedQuery.Contains(alias, StringComparison.Ordinal));
    }

    private static bool ContradictsRole(CompanionPlaceRoleIntent role, string normalized)
    {
        if (role.RequestedRole == "atm")
        {
            return HasWord(normalized, "bank") && !HasWord(normalized, "atm");
        }

        if (role.RequestedRole == "bank_branch")
        {
            return HasWord(normalized, "atm");
        }

        if (role.RequestedRole == "parking")
        {
            return HasWord(normalized, "park") && !normalized.Contains("car park", StringComparison.Ordinal) && !HasWord(normalized, "parking");
        }

        return false;
    }

    private static bool ContradictsStrictAccommodationRole(CompanionPlaceRoleIntent role, string normalized)
    {
        if (role.CategoryStrictness != "strict")
        {
            return false;
        }

        if (role.RequestedRole == "hotel")
        {
            return HasWord(normalized, "lodging")
                   || HasWord(normalized, "motel")
                   || HasWord(normalized, "guesthouse")
                   || normalized.Contains("guest house", StringComparison.Ordinal)
                   || HasWord(normalized, "hostel")
                   || normalized.Contains("places to stay", StringComparison.Ordinal)
                   || HasWord(normalized, "accommodation");
        }

        if (role.RequestedRole == "motel")
        {
            return HasWord(normalized, "hotel") && !HasWord(normalized, "motel");
        }

        return false;
    }

    private static bool IsTooBroadGeneric(CompanionPlaceSearchStrategy strategy, string normalized)
    {
        return strategy.Entity is null
               && normalized is "places" or "local places" or "near me";
    }

    private static bool LooksConversational(string query)
    {
        var normalized = Normalize(query);
        return normalized.Contains("can you", StringComparison.Ordinal)
               || normalized.Contains("please show", StringComparison.Ordinal)
               || normalized.Contains("i would like", StringComparison.Ordinal)
               || normalized.Contains("i'd like", StringComparison.Ordinal);
    }

    private static bool HasWord(string haystack, string word)
    {
        return Regex.IsMatch(haystack, $@"(^|\s){Regex.Escape(word)}($|\s)", RegexOptions.IgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }
}
