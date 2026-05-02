using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class DeterministicCompanionPlaceSearchStrategyFallback(IChatTelemetry telemetry) : IDeterministicCompanionPlaceSearchStrategyFallback
{
    private const int DefaultPoolSize = 50;
    private const int DefaultVisibleCards = 10;

    public CompanionPlaceSearchStrategy Plan(UserChatRequest request, CompanionSemanticIntent intent, string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(intent);

        var role = ResolveRole(request.UserMessage, intent);
        var entity = ResolveEntity(request.UserMessage, intent, role);
        var canonicalQuery = BuildCanonicalQuery(intent, role, entity);
        var variants = BuildVariants(canonicalQuery, role, entity);
        var strategy = new CompanionPlaceSearchStrategy(
            OriginalUserMessage: request.UserMessage,
            CanonicalQuery: canonicalQuery,
            Entity: entity,
            Role: role,
            SearchVariants: variants,
            HardRequirements: intent.HardFilters,
            NegativeRequirements: intent.NegativeFilters,
            SoftPreferences: intent.SoftPreferences,
            NonSearchablePreferences: intent.NonSearchablePreferences,
            Location: intent.Location,
            RankingGoal: intent.RankingGoal,
            MaxCandidatePoolSize: DefaultPoolSize,
            MaxVisibleCards: Math.Clamp(intent.RequestedMaxResults ?? DefaultVisibleCards, 1, DefaultVisibleCards),
            Confidence: intent.Confidence,
            Warnings: [fallbackReason]);

        _ = telemetry.TrackAsync(
            "places.search_strategy.fallback_used",
            new Dictionary<string, object?>
            {
                ["originalMessage"] = request.UserMessage,
                ["fallbackReason"] = fallbackReason,
                ["canonicalQuery"] = strategy.CanonicalQuery,
                ["rawEntityText"] = strategy.Entity?.RawEntityText,
                ["canonicalEntity"] = strategy.Entity?.CanonicalName,
                ["requestedRole"] = strategy.Role.RequestedRole,
                ["categoryStrictness"] = strategy.Role.CategoryStrictness,
                ["variantCount"] = strategy.SearchVariants.Count
            },
            CancellationToken.None);

        return strategy;
    }

    private static CompanionPlaceRoleIntent ResolveRole(string message, CompanionSemanticIntent intent)
    {
        var normalized = Normalize($"{message} {intent.PlaceQuery} {intent.BrandOrEntity}");
        if (HasAny(normalized, "atm", "atms", "cash machine"))
        {
            return new CompanionPlaceRoleIntent("atm", ["atm"], ["atm"], ["bank"], [], "strict");
        }

        if (HasAny(normalized, "bank", "banks", "bank branch", "bank branches"))
        {
            return new CompanionPlaceRoleIntent("bank_branch", ["bank", "financial_institution"], ["bank"], ["atm"], [], "strict");
        }

        if (HasAny(normalized, "car park", "car parks", "parking", "parking lot", "parking garage"))
        {
            return new CompanionPlaceRoleIntent("parking", ["parking"], ["parking", "parking_lot", "parking_garage"], ["park", "tourist_attraction"], [], "strict");
        }

        if (HasAny(normalized, "fine dining", "fancy restaurant", "upscale restaurant", "upscale restaurants"))
        {
            return new CompanionPlaceRoleIntent(
                "restaurant",
                ["restaurant"],
                ["restaurant", "fine_dining_restaurant", "irish_restaurant", "french_restaurant", "asian_restaurant", "european_restaurant", "italian_restaurant", "seafood_restaurant"],
                ["fast_food_restaurant", "meal_takeaway", "cafe"],
                ["fine_dining", "upscale"],
                "compatible");
        }

        if (HasAny(normalized, "coffee shop", "coffee shops", "cafe", "cafes", "café", "cafés"))
        {
            return new CompanionPlaceRoleIntent("coffee_shop", ["coffee_shop", "cafe"], ["coffee_shop", "cafe"], [], [], "compatible");
        }

        if (HasAny(normalized, "petrol station", "petrol stations", "gas station", "fuel station", "service station"))
        {
            return new CompanionPlaceRoleIntent("gas_station", ["gas_station"], ["gas_station"], ["car_wash"], [], "strict");
        }

        if (HasAny(normalized, "post office", "post offices"))
        {
            return new CompanionPlaceRoleIntent("post_office", ["post_office"], ["post_office"], ["mailbox"], [], "strict");
        }

        return intent.Role.CategoryStrictness == "loose" ? intent.Role : intent.Role;
    }

    private static CompanionPlaceEntityIntent? ResolveEntity(
        string message,
        CompanionSemanticIntent intent,
        CompanionPlaceRoleIntent role)
    {
        var candidate = CleanEntityText(FirstNonEmpty(intent.BrandOrEntity, ExtractEntityFromMessage(message, role)), role);
        if (string.IsNullOrWhiteSpace(candidate) || IsGenericOrModifier(candidate, role))
        {
            return null;
        }

        var aliases = new[] { candidate }
            .Concat(GenerateSpacedAlias(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CompanionPlaceEntityIntent(
            RawEntityText: candidate,
            CanonicalName: CanonicalizeEntityText(candidate),
            Aliases: aliases,
            IsBrandOrNamedEntity: true,
            RequiresEntityLock: true,
            VerificationRequired: true,
            VerificationStatus: "pending",
            Confidence: EstimateEntityConfidence(candidate));
    }

    private static string? ExtractEntityFromMessage(string message, CompanionPlaceRoleIntent role)
    {
        var stripped = Regex.Replace(message, @"\b(near me|around me|please|show me|can you|could you|i'd like|i would like|in\s+[a-z0-9\s]+)$", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim(' ', '.', ',', '?', '!');
        stripped = RemoveRoleWords(stripped, role).Trim();
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped;
    }

    private static string? CleanEntityText(string? value, CompanionPlaceRoleIntent role)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = RemoveRoleWords(value, role);
        cleaned = Regex.Replace(cleaned, @"\b(near me|around me|please|show me|can you|could you|i'd like|i would like)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.', ',', '?', '!');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string RemoveRoleWords(string value, CompanionPlaceRoleIntent role)
    {
        var result = value;
        var roleWords = role.RequestedRole switch
        {
            "bank_branch" => @"\b(banks?|bank branches?|branches?)\b",
            "atm" => @"\b(atms?|cash machines?)\b",
            "parking" => @"\b(car parks?|parking lots?|parking garages?|parking)\b",
            "restaurant" => @"\b(fine dining|fancy|upscale|restaurants?|dining)\b",
            "coffee_shop" => @"\b(coffee shops?|cafes?|cafés?|coffee)\b",
            "gas_station" => @"\b(petrol stations?|gas stations?|fuel stations?|service stations?)\b",
            "post_office" => @"\b(post offices?)\b",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(roleWords)
            ? result
            : Regex.Replace(result, roleWords, " ", RegexOptions.IgnoreCase);
    }

    private static string BuildCanonicalQuery(
        CompanionSemanticIntent intent,
        CompanionPlaceRoleIntent role,
        CompanionPlaceEntityIntent? entity)
    {
        if (entity is not null)
        {
            var roleQuery = RoleQuery(role);
            return string.IsNullOrWhiteSpace(roleQuery)
                ? entity.CanonicalName ?? entity.RawEntityText ?? intent.PlaceQuery ?? string.Empty
                : $"{entity.CanonicalName ?? entity.RawEntityText} {roleQuery}".Trim();
        }

        if (!string.IsNullOrWhiteSpace(intent.PlaceQuery))
        {
            return intent.PlaceQuery!;
        }

        return RoleQuery(role) ?? "local places";
    }

    private static IReadOnlyList<CompanionPlaceSearchVariant> BuildVariants(
        string canonicalQuery,
        CompanionPlaceRoleIntent role,
        CompanionPlaceEntityIntent? entity)
    {
        var variants = new List<CompanionPlaceSearchVariant>();
        if (entity is not null)
        {
            foreach (var alias in entity.Aliases.Prepend(entity.CanonicalName).Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var query = string.IsNullOrWhiteSpace(RoleQuery(role)) ? alias! : $"{alias} {RoleQuery(role)}";
                variants.Add(new CompanionPlaceSearchVariant(query, variants.Count == 0 ? "primary" : "alias", true, role.CategoryStrictness != "loose", 0.9d));
            }

            return variants;
        }

        variants.Add(new CompanionPlaceSearchVariant(canonicalQuery, "primary", false, role.CategoryStrictness != "loose", 0.86d));
        if (role.RequestedRole == "coffee_shop")
        {
            variants.AddRange([
                new CompanionPlaceSearchVariant("cafe", "role_disambiguation", false, true, 0.72d),
                new CompanionPlaceSearchVariant("coffee", "role_disambiguation", false, true, 0.68d)
            ]);
        }
        else if (role.RequestedRole == "restaurant" && role.Modifiers.Contains("fine_dining", StringComparer.OrdinalIgnoreCase))
        {
            variants.Add(new CompanionPlaceSearchVariant("upscale restaurants", "role_disambiguation", false, true, 0.76d));
        }
        else if (role.RequestedRole == "parking")
        {
            variants.AddRange([
                new CompanionPlaceSearchVariant("parking", "role_disambiguation", false, true, 0.72d),
                new CompanionPlaceSearchVariant("parking garage", "role_disambiguation", false, true, 0.68d)
            ]);
        }

        return variants;
    }

    private static string? RoleQuery(CompanionPlaceRoleIntent role)
    {
        return role.RequestedRole switch
        {
            "bank_branch" => "bank",
            "atm" => "ATM",
            "parking" => "parking",
            "restaurant" when role.Modifiers.Contains("fine_dining", StringComparer.OrdinalIgnoreCase) => "restaurant",
            "coffee_shop" => "coffee shop",
            "gas_station" => "petrol station",
            "post_office" => "post office",
            _ => role.RequestedRole
        };
    }

    private static bool IsGenericOrModifier(string value, CompanionPlaceRoleIntent role)
    {
        var normalized = Normalize(value);
        if (normalized.Length <= 1)
        {
            return true;
        }

        var generic = role.RequiredCoreRoles
            .Concat(role.AcceptableSubRoles)
            .Concat(role.Modifiers)
            .Concat(["fine dining", "upscale", "fancy", "coffee", "cafe", "bank", "banks", "atm", "parking", "car parks", "restaurants"])
            .Select(Normalize)
            .Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        return generic;
    }

    private static string CanonicalizeEntityText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 4 && !trimmed.Contains(' ', StringComparison.Ordinal)
            ? trimmed.ToUpperInvariant()
            : string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(CapitalizeWord));
    }

    private static IEnumerable<string> GenerateSpacedAlias(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 5 && !trimmed.Contains(' ', StringComparison.Ordinal))
        {
            var split = Regex.Replace(trimmed, "([a-z])([A-Z])", "$1 $2");
            if (!string.Equals(split, trimmed, StringComparison.Ordinal))
            {
                yield return split;
            }
        }
    }

    private static double EstimateEntityConfidence(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= 5 && trimmed.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
        {
            return 0.86d;
        }

        return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3 ? 0.80d : 0.65d;
    }

    private static bool HasAny(string normalized, params string[] needles)
    {
        return needles.Any(needle => normalized.Contains(Normalize(needle), StringComparison.Ordinal));
    }

    private static string CapitalizeWord(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }
}
