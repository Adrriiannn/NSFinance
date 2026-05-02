using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceSearchStrategySanitizer : ICompanionPlaceSearchStrategySanitizer
{
    public CompanionPlaceSearchStrategy Sanitize(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy strategy)
    {
        var role = strategy.Role.CategoryStrictness is "strict" or "compatible" or "loose"
            ? strategy.Role
            : strategy.Role with { CategoryStrictness = "loose" };
        var entity = strategy.Entity;
        if (entity is not null && IsGenericEntity(entity, role))
        {
            entity = null;
        }

        var variants = strategy.SearchVariants
            .Where(static item => !string.IsNullOrWhiteSpace(item.Query))
            .Select(static item => item with { Query = Regex.Replace(item.Query.Trim(), @"\s+", " ") })
            .DistinctBy(static item => Normalize(item.Query), StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        if (variants.Length == 0 && !string.IsNullOrWhiteSpace(strategy.CanonicalQuery))
        {
            variants = [new CompanionPlaceSearchVariant(strategy.CanonicalQuery!, "primary", entity is not null, role.CategoryStrictness != "loose", strategy.Confidence)];
        }

        return strategy with
        {
            OriginalUserMessage = request.UserMessage,
            CanonicalQuery = string.IsNullOrWhiteSpace(strategy.CanonicalQuery) ? intent.PlaceQuery : strategy.CanonicalQuery.Trim(),
            Entity = entity,
            Role = role,
            SearchVariants = variants,
            HardRequirements = Clean(strategy.HardRequirements.Count > 0 ? strategy.HardRequirements : intent.HardFilters),
            NegativeRequirements = Clean(strategy.NegativeRequirements.Count > 0 ? strategy.NegativeRequirements : intent.NegativeFilters),
            SoftPreferences = Clean(strategy.SoftPreferences.Count > 0 ? strategy.SoftPreferences : intent.SoftPreferences),
            NonSearchablePreferences = Clean(strategy.NonSearchablePreferences.Count > 0 ? strategy.NonSearchablePreferences : intent.NonSearchablePreferences),
            Location = intent.Location,
            RankingGoal = string.IsNullOrWhiteSpace(strategy.RankingGoal) ? intent.RankingGoal : strategy.RankingGoal,
            MaxCandidatePoolSize = Math.Clamp(strategy.MaxCandidatePoolSize, 1, 50),
            MaxVisibleCards = Math.Clamp(strategy.MaxVisibleCards, 1, 10),
            Confidence = Math.Clamp(strategy.Confidence, 0d, 1d),
            Warnings = Clean(strategy.Warnings)
        };
    }

    private static bool IsGenericEntity(CompanionPlaceEntityIntent entity, CompanionPlaceRoleIntent role)
    {
        var raw = Normalize($"{entity.RawEntityText} {entity.CanonicalName}");
        return string.IsNullOrWhiteSpace(raw)
               || role.RequiredCoreRoles.Concat(role.AcceptableSubRoles).Concat(role.Modifiers)
                   .Select(Normalize)
                   .Any(item => raw == item || raw.Contains(item, StringComparison.Ordinal))
               || raw is "fine dining" or "car parks" or "coffee shops" or "restaurants" or "parking" or "cafe";
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string> values)
    {
        return values
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
    }
}
