using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceSearchStrategyJsonParser(
    ICompanionPlaceSearchStrategySanitizer sanitizer) : ICompanionPlaceSearchStrategyJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool TryParse(
        AIResponse response,
        UserChatRequest request,
        CompanionSemanticIntent intent,
        out CompanionPlaceSearchStrategy? strategy,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason)
    {
        strategy = null;
        failureReason = null;

        if (!response.Succeeded)
        {
            failureReason = response.FailureReason ?? "places_search_strategy_ai_failed";
            reasonCodes = [failureReason];
            return false;
        }

        var raw = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            failureReason = "places_search_strategy_empty_payload";
            reasonCodes = [failureReason];
            return false;
        }

        StrategyPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StrategyPayload>(ExtractJson(raw), JsonOptions);
        }
        catch (JsonException)
        {
            failureReason = "places_search_strategy_invalid_json";
            reasonCodes = [failureReason];
            return false;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.CanonicalQuery)
            || payload.Role is null
            || payload.SearchVariants is null
            || payload.SearchVariants.Count == 0
            || payload.Confidence is < 0d or > 1d)
        {
            failureReason = "places_search_strategy_missing_required_fields";
            reasonCodes = [failureReason];
            return false;
        }

        var role = new CompanionPlaceRoleIntent(
            payload.Role.RequestedRole,
            CleanList(payload.Role.RequiredCoreRoles),
            CleanList(payload.Role.AcceptableSubRoles),
            CleanList(payload.Role.ExcludedSiblingRoles),
            CleanList(payload.Role.Modifiers),
            NormalizeStrictness(payload.Role.CategoryStrictness));
        var entity = payload.Entity is null || payload.Entity.IsBrandOrNamedEntity == false
            ? null
            : new CompanionPlaceEntityIntent(
                payload.Entity.RawEntityText,
                payload.Entity.CanonicalName,
                CleanList(payload.Entity.Aliases),
                payload.Entity.IsBrandOrNamedEntity,
                payload.Entity.RequiresEntityLock,
                payload.Entity.VerificationRequired,
                "pending",
                Clamp(payload.Entity.Confidence));
        var variants = payload.SearchVariants
            .Where(static item => !string.IsNullOrWhiteSpace(item.Query))
            .Take(4)
            .Select(static item => new CompanionPlaceSearchVariant(
                item.Query!.Trim(),
                string.IsNullOrWhiteSpace(item.Purpose) ? "primary" : item.Purpose!.Trim(),
                item.RequiresEntityMatch,
                item.RequiresRoleMatch,
                Clamp(item.Confidence)))
            .ToArray();

        strategy = sanitizer.Sanitize(
            request,
            intent,
            new CompanionPlaceSearchStrategy(
                request.UserMessage,
                payload.CanonicalQuery.Trim(),
                entity,
                role,
                variants,
                CleanList(payload.HardRequirements),
                CleanList(payload.NegativeRequirements),
                CleanList(payload.SoftPreferences),
                CleanList(payload.NonSearchablePreferences),
                intent.Location,
                string.IsNullOrWhiteSpace(payload.RankingGoal) ? intent.RankingGoal : payload.RankingGoal.Trim(),
                Math.Clamp(payload.MaxCandidatePoolSize ?? 50, 1, 50),
                Math.Clamp(payload.MaxVisibleCards ?? intent.RequestedMaxResults ?? 10, 1, 10),
                Clamp(payload.Confidence),
                CleanList(payload.Warnings)));
        reasonCodes = ["places_search_strategy_ai_parsed"];
        return true;
    }

    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return trimmed[firstBrace..(lastBrace + 1)];
            }
        }

        return trimmed;
    }

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static string NormalizeStrictness(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "strict" or "compatible" or "loose" ? value.Trim().ToLowerInvariant() : "loose";
    }

    private static double Clamp(double? value) => Math.Clamp(value ?? 0.5d, 0d, 1d);

    private sealed record StrategyPayload(
        string? CanonicalQuery,
        EntityPayload? Entity,
        RolePayload? Role,
        IReadOnlyList<VariantPayload>? SearchVariants,
        IReadOnlyList<string>? HardRequirements,
        IReadOnlyList<string>? NegativeRequirements,
        IReadOnlyList<string>? SoftPreferences,
        IReadOnlyList<string>? NonSearchablePreferences,
        string? RankingGoal,
        int? MaxCandidatePoolSize,
        int? MaxVisibleCards,
        double? Confidence,
        IReadOnlyList<string>? Warnings);

    private sealed record EntityPayload(
        string? RawEntityText,
        string? CanonicalName,
        IReadOnlyList<string>? Aliases,
        bool IsBrandOrNamedEntity,
        bool RequiresEntityLock,
        bool VerificationRequired,
        double? Confidence);

    private sealed record RolePayload(
        string? RequestedRole,
        IReadOnlyList<string>? RequiredCoreRoles,
        IReadOnlyList<string>? AcceptableSubRoles,
        IReadOnlyList<string>? ExcludedSiblingRoles,
        IReadOnlyList<string>? Modifiers,
        string? CategoryStrictness);

    private sealed record VariantPayload(
        string? Query,
        string? Purpose,
        bool RequiresEntityMatch,
        bool RequiresRoleMatch,
        double? Confidence);
}
