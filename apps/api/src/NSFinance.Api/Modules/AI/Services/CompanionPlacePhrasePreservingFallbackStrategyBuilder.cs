using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlacePhrasePreservingFallbackStrategyBuilder : ICompanionPlacePhrasePreservingFallbackStrategyBuilder
{
    private const int DefaultPoolSize = 50;
    private const int DefaultVisibleCards = 10;

    public CompanionPlaceSearchStrategy Build(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(intent);

        var canonicalQuery = ResolveCanonicalQuery(request, intent);
        var entity = ResolveEntity(intent, canonicalQuery);
        var variants = BuildVariants(canonicalQuery, intent, entity);
        return new CompanionPlaceSearchStrategy(
            OriginalUserMessage: request.UserMessage,
            CanonicalQuery: canonicalQuery,
            Entity: entity,
            Role: intent.Role,
            SearchVariants: variants,
            HardRequirements: intent.HardFilters,
            NegativeRequirements: intent.NegativeFilters,
            SoftPreferences: intent.SoftPreferences,
            NonSearchablePreferences: intent.NonSearchablePreferences,
            Location: intent.Location,
            RankingGoal: string.IsNullOrWhiteSpace(intent.RankingGoal) ? "intent_fit_then_distance" : intent.RankingGoal,
            MaxCandidatePoolSize: DefaultPoolSize,
            MaxVisibleCards: Math.Clamp(intent.RequestedMaxResults ?? DefaultVisibleCards, 1, DefaultVisibleCards),
            Confidence: Math.Clamp(Math.Min(intent.Confidence, 0.62d), 0.35d, 0.62d),
            Warnings: [fallbackReason, "phrase_preserving_fallback_used"]);
    }

    private static string ResolveCanonicalQuery(UserChatRequest request, CompanionSemanticIntent intent)
    {
        var query = FirstNonEmpty(CleanUserMessage(intent.PlaceQuery ?? string.Empty, intent), CleanUserMessage(request.UserMessage, intent));
        query = Regex.Replace(query ?? string.Empty, @"\s+", " ").Trim(' ', '.', ',', '?', '!');
        return string.IsNullOrWhiteSpace(query) ? "local places" : query;
    }

    private static string CleanUserMessage(string message, CompanionSemanticIntent intent)
    {
        var cleaned = message;
        cleaned = Regex.Replace(cleaned, @"\b(can you|could you|please|show me|find me|i'd like|i would like|looking for)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(near me|nearby|around me|close to me)\b", " ", RegexOptions.IgnoreCase);

        if (intent.Location.Mode == "typed_area" && !string.IsNullOrWhiteSpace(intent.Location.AreaText))
        {
            var area = Regex.Escape(intent.Location.AreaText.Trim());
            cleaned = Regex.Replace(cleaned, $@"\b(in|around|near)\s+{area}\b\s*$", " ", RegexOptions.IgnoreCase);
        }

        cleaned = Regex.Replace(cleaned, @"\bwithin\s+\d+(\.\d+)?\s*(km|kilometres?|miles?|mi)\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.', ',', '?', '!');
    }

    private static CompanionPlaceEntityIntent? ResolveEntity(CompanionSemanticIntent intent, string canonicalQuery)
    {
        var brandSource = intent.BrandOrEntity;
        if (string.IsNullOrWhiteSpace(brandSource) && IsDistinctiveProperEntity(canonicalQuery))
        {
            brandSource = canonicalQuery;
        }

        if (string.IsNullOrWhiteSpace(brandSource))
        {
            return null;
        }

        var brand = CleanEntityText(brandSource, canonicalQuery);
        if (string.IsNullOrWhiteSpace(brand))
        {
            return null;
        }

        if (IsLikelyGenericEntity(brand, canonicalQuery))
        {
            return null;
        }

        return new CompanionPlaceEntityIntent(
            RawEntityText: brand,
            CanonicalName: brand,
            Aliases: [brand],
            RelationshipAliases: [],
            IsBrandOrNamedEntity: true,
            RequiresEntityLock: true,
            VerificationRequired: true,
            VerificationStatus: "pending",
            Confidence: Math.Clamp(Math.Max(intent.Confidence, 0.82d), 0.82d, 0.9d));
    }

    private static string? CleanEntityText(string? brandOrEntity, string canonicalQuery)
    {
        if (string.IsNullOrWhiteSpace(brandOrEntity))
        {
            return null;
        }

        var cleaned = brandOrEntity.Trim();
        var query = Normalize(canonicalQuery);
        if (query.Contains("bank", StringComparison.Ordinal))
        {
            cleaned = Regex.Replace(cleaned, @"\b(banks?|bank branches?|branches?)\b", " ", RegexOptions.IgnoreCase);
        }
        else if (query.Contains("atm", StringComparison.Ordinal))
        {
            cleaned = Regex.Replace(cleaned, @"\b(atms?|cash machines?)\b", " ", RegexOptions.IgnoreCase);
        }
        else if (query.Contains("office", StringComparison.Ordinal))
        {
            cleaned = Regex.Replace(cleaned, @"\b(offices?|corporate offices?|headquarters|hq)\b", " ", RegexOptions.IgnoreCase);
        }
        else if (query.Contains("petrol station", StringComparison.Ordinal) || query.Contains("gas station", StringComparison.Ordinal))
        {
            cleaned = Regex.Replace(cleaned, @"\b(petrol stations?|gas stations?|fuel stations?|service stations?)\b", " ", RegexOptions.IgnoreCase);
        }
        else if (query.Contains("post office", StringComparison.Ordinal))
        {
            cleaned = Regex.Replace(cleaned, @"\b(post offices?)\b", " ", RegexOptions.IgnoreCase);
        }

        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.', ',', '?', '!');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static IReadOnlyList<CompanionPlaceSearchVariant> BuildVariants(
        string canonicalQuery,
        CompanionSemanticIntent intent,
        CompanionPlaceEntityIntent? entity)
    {
        var queries = new List<string> { canonicalQuery };
        var semanticQuery = CleanUserMessage(intent.PlaceQuery ?? string.Empty, intent);
        if (!string.IsNullOrWhiteSpace(semanticQuery)
            && !string.Equals(semanticQuery, canonicalQuery, StringComparison.OrdinalIgnoreCase))
        {
            queries.Add(semanticQuery);
        }

        return queries
            .Where(static query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select((query, index) => new CompanionPlaceSearchVariant(
                query,
                index == 0 ? "primary" : "semantic_query",
                entity is not null,
                false,
                index == 0 ? 0.62d : 0.55d))
            .ToArray();
    }

    private static bool IsLikelyGenericEntity(string brand, string canonicalQuery)
    {
        var normalizedBrand = Normalize(brand);
        var normalizedQuery = Normalize(canonicalQuery);
        if (string.IsNullOrWhiteSpace(normalizedBrand))
        {
            return true;
        }

        if (normalizedQuery.Contains(normalizedBrand, StringComparison.Ordinal)
            && Regex.IsMatch(normalizedQuery, @"\b(shops?|stores?|repair|restaurants?|cafes?|parks?|parking|banks?|atms?|offices?|hotels?|pharmacies|centres?|centers?)\b", RegexOptions.IgnoreCase)
            && !IsDistinctiveProperEntity(brand))
        {
            return true;
        }

        return normalizedBrand is "bike" or "bicycle" or "cycle" or "shoe" or "toy" or "phone" or "garden"
            or "aquarium" or "tailor" or "currency" or "exchange" or "restaurant" or "coffee" or "parking";
    }

    private static bool IsDistinctiveProperEntity(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 5 && trimmed.Any(char.IsLetter) && trimmed.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))
               || trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.Length > 1 && char.IsUpper(word[0]) && word.Skip(1).Any(char.IsLower));
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
