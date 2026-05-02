using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionSemanticIntentService(IChatTelemetry? telemetry = null) : ICompanionSemanticIntentService
{
    public CompanionSemanticIntent Build(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ConversationIntelligenceResult? intelligence,
        CompanionResolvedAction? resolvedAction)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actionKind = resolvedAction?.Kind switch
        {
            CompanionActionKind.NewPlaceSearch => "new_place_search",
            CompanionActionKind.FilterPreviousResults => "filter_previous_results",
            CompanionActionKind.SortPreviousResults => "sort_previous_results",
            CompanionActionKind.EnrichPreviousResults => "filter_previous_results",
            CompanionActionKind.CloseConversation => "close_conversation",
            _ => MapNextAction(intelligence?.NextAction.Type)
        };
        var fallbackClosing = false;
        if (actionKind != "close_conversation" && IsClosingFallback(request.UserMessage))
        {
            fallbackClosing = true;
            actionKind = "close_conversation";
        }

        var placeQuery = FirstNonEmpty(
            resolvedAction?.PlaceQuery,
            retrievalPlan?.BrandTerm,
            interpretation?.PlacePlan.BrandOrEntityTerms.FirstOrDefault(),
            retrievalPlan?.CanonicalConcept,
            interpretation?.PlacePlan.CanonicalConcept);
        var brand = FirstNonEmpty(
            retrievalPlan?.BrandTerm,
            interpretation?.PlacePlan.BrandOrEntityTerms.FirstOrDefault(),
            IsSingleTokenEntity(resolvedAction?.PlaceQuery) ? resolvedAction?.PlaceQuery : null,
            resolvedAction?.IncludeConcepts.FirstOrDefault(IsLikelyBrand),
            ExtractBrandFallback(request.UserMessage, placeQuery));
        var location = BuildLocationIntent(request, state, interpretation, retrievalPlan, resolvedAction);
        var hardFilters = MergeDistinct(
            resolvedAction?.Requirement is null ? [] : [resolvedAction.Requirement],
            resolvedAction?.TimeFilters,
            interpretation?.PlacePlan.TimeFilters,
            retrievalPlan?.TimeFilters);
        var ratingFilter = ExtractRatingThresholdFilter(request.UserMessage);
        if (ratingFilter is not null)
        {
            hardFilters = MergeDistinct(hardFilters, [ratingFilter]);
            if (telemetry is not null)
            {
                _ = telemetry.TrackAsync(
                    "places.semantic_intent.rating_filter_extracted",
                    new Dictionary<string, object?>
                    {
                        ["correlationId"] = request.CorrelationId,
                        ["filter"] = ratingFilter
                    },
                    CancellationToken.None);
            }
        }

        var fallbackHardFilters = hardFilters.Count == 0;
        if (fallbackHardFilters)
        {
            hardFilters = ExtractHardFiltersFallback(request.UserMessage);
        }

        var negativeFilters = MergeDistinct(
            resolvedAction?.ExcludeConcepts,
            interpretation?.PlacePlan.ExcludeTypes,
            retrievalPlan?.ExcludedTypes);
        var fallbackNegativeFilters = negativeFilters.Count == 0;
        if (fallbackNegativeFilters)
        {
            negativeFilters = ExtractNegativeFiltersFallback(request.UserMessage, placeQuery);
        }

        var softPreferences = MergeDistinct(
            resolvedAction?.Preferences,
            interpretation?.PlacePlan.Preferences,
            retrievalPlan?.Preferences);
        var fallbackSoftPreferences = softPreferences.Count == 0;
        if (fallbackSoftPreferences)
        {
            softPreferences = ExtractSoftPreferencesFallback(request.UserMessage);
        }

        var nonSearchable = ExtractNonSearchablePreferencesFallback(request.UserMessage);
        var detailFields = ExtractRequestedDetailFieldsFallback(request.UserMessage, resolvedAction?.Requirement);
        var rankingGoal = ResolveRankingGoal(actionKind, request.UserMessage, brand, placeQuery, resolvedAction?.SortGoal);
        var requestedMax = ExtractRequestedMaxResults(request.UserMessage, resolvedAction);
        var normalizedPlaceQuery = NormalizePlaceQuery(placeQuery, request.UserMessage, resultContext);
        var fallbackPlaceQuery = string.IsNullOrWhiteSpace(placeQuery) && !string.IsNullOrWhiteSpace(normalizedPlaceQuery);

        var intent = new CompanionSemanticIntent(
            IntentFamily: "places",
            ActionKind: actionKind,
            PlaceQuery: normalizedPlaceQuery,
            BrandOrEntity: brand,
            Location: location,
            Role: ResolveRole(normalizedPlaceQuery, brand, hardFilters, negativeFilters, softPreferences),
            HardFilters: hardFilters,
            NegativeFilters: negativeFilters,
            SoftPreferences: softPreferences,
            NonSearchablePreferences: nonSearchable,
            RequestedDetailFields: detailFields,
            RankingGoal: rankingGoal,
            RequestedMaxResults: requestedMax,
            Confidence: conversationConfidence(intelligence, interpretation, resolvedAction),
            Ambiguities: interpretation?.Ambiguities ?? []);

        if (telemetry is not null
            && (fallbackHardFilters || fallbackNegativeFilters || fallbackSoftPreferences || fallbackPlaceQuery || fallbackClosing))
        {
            _ = telemetry.TrackAsync(
                "places.semantic_intent.fallback_used",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = request.CorrelationId,
                    ["fallbackHardFilters"] = fallbackHardFilters,
                    ["fallbackNegativeFilters"] = fallbackNegativeFilters,
                    ["fallbackSoftPreferences"] = fallbackSoftPreferences,
                    ["fallbackPlaceQuery"] = fallbackPlaceQuery,
                    ["fallbackClosing"] = fallbackClosing
                },
                CancellationToken.None);
        }

        return intent;

        static double conversationConfidence(
            ConversationIntelligenceResult? intelligence,
            TurnInterpretationV2? interpretation,
            CompanionResolvedAction? resolvedAction)
        {
            if (resolvedAction is not null)
            {
                return Math.Max(0.80d, intelligence?.UserIntentConfidence ?? interpretation?.Confidence ?? 0.80d);
            }

            return intelligence?.UserIntentConfidence ?? interpretation?.Confidence ?? 0.55d;
        }
    }

    private static string MapNextAction(string? nextAction)
    {
        return nextAction switch
        {
            "execute_search" => "new_place_search",
            "filter_previous_results" => "filter_previous_results",
            "sort_previous_results" => "sort_previous_results",
            "enrich_details" => "filter_previous_results",
            "answer_directly" => "answer_directly",
            _ => "answer_directly"
        };
    }

    private static CompanionLocationIntent BuildLocationIntent(
        UserChatRequest request,
        ConversationStateSnapshot state,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        CompanionResolvedAction? resolvedAction)
    {
        var metadata = request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var latitude = TryReadDouble(metadata, CompanionLocationMetadataKeys.Latitude);
        var longitude = TryReadDouble(metadata, CompanionLocationMetadataKeys.Longitude);
        if (latitude.HasValue && longitude.HasValue)
        {
            return new CompanionLocationIntent("near_me", null, latitude, longitude, RequiresLocation: false);
        }

        var area = FirstNonEmpty(
            resolvedAction?.LocationQuery is "near me" ? null : resolvedAction?.LocationQuery,
            retrievalPlan?.ResolvedAreaHint,
            interpretation?.LocationPlan.ResolvedAreaHint,
            interpretation?.LocationPlan.ExplicitAreaText,
            state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var previousArea) ? previousArea : null);
        if (!string.IsNullOrWhiteSpace(area) && !string.Equals(area, "near_me", StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionLocationIntent("typed_area", area.Trim(), null, null, RequiresLocation: false);
        }

        var nearMe = string.Equals(resolvedAction?.LocationQuery, "near me", StringComparison.OrdinalIgnoreCase)
                     || retrievalPlan?.NearMeSemantic == true
                     || interpretation?.LocationPlan.NearMeSemantic == true
                     || request.UserMessage.Contains("near me", StringComparison.OrdinalIgnoreCase)
                     || request.UserMessage.Contains("around me", StringComparison.OrdinalIgnoreCase);
        return new CompanionLocationIntent(nearMe ? "near_me" : "none", null, null, null, RequiresLocation: nearMe);
    }

    private static string? NormalizePlaceQuery(string? placeQuery, string userMessage, ResultContextSnapshot? resultContext)
    {
        if (!string.IsNullOrWhiteSpace(placeQuery))
        {
            return placeQuery.Trim();
        }

        if (resultContext?.NormalizedConstraints.TryGetValue("semantic_place_query", out var previousQuery) == true
            && !string.IsNullOrWhiteSpace(previousQuery))
        {
            return previousQuery;
        }

        var stripped = Regex.Replace(userMessage, @"\b(near me|around me|please|show me|can you|could you|i'd like|i would like)\b", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim(' ', '.', ',', '?', '!');
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped;
    }

    private static IReadOnlyList<string> ExtractHardFiltersFallback(string message)
    {
        var normalized = Normalize(message);
        var filters = new List<string>();
        var ratingFilter = ExtractRatingThresholdFilter(message);
        if (ratingFilter is not null)
        {
            filters.Add(ratingFilter);
        }

        if (normalized.Contains("open now", StringComparison.Ordinal))
        {
            filters.Add("open_now");
        }

        if (normalized.Contains("parking", StringComparison.Ordinal))
        {
            filters.Add("parking_available_or_nearby");
        }

        return filters;
    }

    private static string? ExtractRatingThresholdFilter(string message)
    {
        var normalized = Normalize(message);
        var patterns = new[]
        {
            @"(?:rated\s*)?([0-5](?:\.\d)?)\s*(?:\+|and up|or higher)",
            @"(?:minimum|at least)\s*([0-5](?:\.\d)?)\s*(?:stars?|rating)?",
            @"(?:above|over)\s*([0-5](?:\.\d)?)",
            @"only\s*([0-5](?:\.\d)?)\s*(?:rating\s*)?(?:and up|or higher)?"
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
            if (match.Success
                && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var threshold)
                && threshold is >= 0d and <= 5d)
            {
                return $"rating>={threshold.ToString("0.0", CultureInfo.InvariantCulture)}";
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractNegativeFiltersFallback(string message, string? placeQuery)
    {
        var normalized = Normalize($"{message} {placeQuery}");
        var filters = new List<string>();
        if (normalized.Contains("not fast food", StringComparison.Ordinal)
            || normalized.Contains("no fast food", StringComparison.Ordinal)
            || normalized.Contains("fine dining", StringComparison.Ordinal)
            || normalized.Contains("fancy restaurant", StringComparison.Ordinal))
        {
            filters.AddRange(["fast_food", "fast_food_restaurant", "takeaway", "meal_takeaway"]);
        }

        if (normalized.Contains("not takeaway", StringComparison.Ordinal)
            || normalized.Contains("no takeaway", StringComparison.Ordinal))
        {
            filters.AddRange(["takeaway", "meal_takeaway"]);
        }

        if (normalized.Contains("not mcdonald", StringComparison.Ordinal))
        {
            filters.Add("mcdonalds");
        }

        return filters.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ExtractSoftPreferencesFallback(string message)
    {
        var normalized = Normalize(message);
        var values = new List<string>();
        if (normalized.Contains("fancy", StringComparison.Ordinal)
            || normalized.Contains("high class", StringComparison.Ordinal)
            || normalized.Contains("fine dining", StringComparison.Ordinal))
        {
            values.AddRange(["upscale", "high_quality"]);
        }

        if (normalized.Contains("quiet", StringComparison.Ordinal))
        {
            values.Add("quiet");
        }

        if (normalized.Contains("not too expensive", StringComparison.Ordinal)
            || normalized.Contains("cheaper", StringComparison.Ordinal))
        {
            values.Add("not_too_expensive");
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ExtractNonSearchablePreferencesFallback(string message)
    {
        var normalized = Normalize(message);
        var values = new List<string>();
        if (normalized.Contains("immaculate interior", StringComparison.Ordinal)
            || normalized.Contains("interior design", StringComparison.Ordinal))
        {
            values.Add("immaculate_interior_design");
        }

        if (normalized.Contains("vibey", StringComparison.Ordinal))
        {
            values.Add("vibey");
        }

        return values;
    }

    private static IReadOnlyList<string> ExtractRequestedDetailFieldsFallback(string message, string? requirement)
    {
        var normalized = Normalize($"{message} {requirement}");
        var values = new List<string>();
        if (normalized.Contains("parking", StringComparison.Ordinal))
        {
            values.Add("parking");
        }

        if (normalized.Contains("call", StringComparison.Ordinal) || normalized.Contains("phone", StringComparison.Ordinal))
        {
            values.Add("phone");
        }

        if (normalized.Contains("open", StringComparison.Ordinal))
        {
            values.Add("opening_hours");
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveRankingGoal(string actionKind, string message, string? brand, string? placeQuery, string? sortGoal)
    {
        var normalized = Normalize($"{message} {placeQuery}");
        if (actionKind == "sort_previous_results" || string.Equals(sortGoal, "distance", StringComparison.OrdinalIgnoreCase))
        {
            return "distance";
        }

        if (!string.IsNullOrWhiteSpace(brand))
        {
            return "brand_match_then_distance";
        }

        if (normalized.Contains("fine dining", StringComparison.Ordinal)
            || normalized.Contains("fancy restaurant", StringComparison.Ordinal))
        {
            return "concept_fit_then_distance";
        }

        if (normalized.Contains("museum", StringComparison.Ordinal) && !normalized.Contains("near me", StringComparison.Ordinal))
        {
            return "relevance_rating_then_distance";
        }

        if (normalized.Contains("car park", StringComparison.Ordinal) || normalized.Contains("parking", StringComparison.Ordinal))
        {
            return "parking_match_then_distance";
        }

        return "intent_fit_then_distance";
    }

    private static int? ExtractRequestedMaxResults(string message, CompanionResolvedAction? action)
    {
        if (action?.Preferences.Contains("single_best", StringComparer.OrdinalIgnoreCase) == true)
        {
            return 1;
        }

        var normalized = Normalize(message);
        if (normalized.Contains("just the closest", StringComparison.Ordinal)
            || normalized.Contains("closest only", StringComparison.Ordinal))
        {
            return 1;
        }

        return null;
    }

    private static bool IsClosingFallback(string message)
    {
        var normalized = Normalize(message).Trim('.', '!', '?');
        return normalized is "thanks" or "thank you" or "cheers" or "stop" or "never mind" or "nevermind";
    }

    private static bool IsLikelyBrand(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 40)
        {
            return false;
        }

        var normalized = Normalize(value);
        if (normalized.Contains("restaurant", StringComparison.Ordinal)
            || normalized.Contains("coffee", StringComparison.Ordinal)
            || normalized.Contains("cafe", StringComparison.Ordinal)
            || normalized.Contains("shop", StringComparison.Ordinal)
            || normalized.Contains("museum", StringComparison.Ordinal)
            || normalized.Contains("gym", StringComparison.Ordinal)
            || normalized.Contains("parking", StringComparison.Ordinal)
            || normalized.Contains("park", StringComparison.Ordinal))
        {
            return false;
        }

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;
    }

    private static string? ExtractBrandFallback(string message, string? placeQuery)
    {
        var normalized = Normalize($"{message} {placeQuery}");
        if (normalized.Contains("starbucks", StringComparison.Ordinal))
        {
            return "Starbucks";
        }

        if (Regex.IsMatch(normalized, @"\baib\b", RegexOptions.IgnoreCase)
            || normalized.Contains("allied irish bank", StringComparison.Ordinal))
        {
            return "AIB";
        }

        return null;
    }

    private static CompanionPlaceRoleIntent ResolveRole(
        string? placeQuery,
        string? brand,
        IReadOnlyList<string> hardFilters,
        IReadOnlyList<string> negativeFilters,
        IReadOnlyList<string> softPreferences)
    {
        var normalized = Normalize($"{placeQuery} {brand}");
        if (normalized.Contains("atm", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("atm", ["atm"], ["atm"], [], [], "strict");
        }

        if (normalized.Contains("bank", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("bank_branch", ["bank", "financial institution"], ["bank"], ["atm"], [], "strict");
        }

        if (normalized.Contains("car park", StringComparison.Ordinal) || normalized.Contains("parking", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("parking", ["parking"], ["parking_lot", "parking_garage", "parking"], ["park", "tourist_attraction"], [], "strict");
        }

        if (normalized.Contains("fine dining", StringComparison.Ordinal)
            || softPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
        {
            return new CompanionPlaceRoleIntent(
                "restaurant",
                ["restaurant"],
                ["fine_dining_restaurant", "irish_restaurant", "french_restaurant", "asian_restaurant", "european_restaurant", "italian_restaurant", "seafood_restaurant", "restaurant"],
                ["fast_food_restaurant", "meal_takeaway", "cafe"],
                ["fine_dining", "upscale"],
                "compatible");
        }

        if (normalized.Contains("pharmacy", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("pharmacy", ["pharmacy"], ["pharmacy"], ["hospital"], [], "strict");
        }

        if (normalized.Contains("petrol", StringComparison.Ordinal) || normalized.Contains("gas station", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("petrol_station", ["gas_station"], ["gas_station"], ["car_wash"], [], "strict");
        }

        if (normalized.Contains("post office", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("post_office", ["post_office"], ["post_office"], ["mailbox"], [], "strict");
        }

        if (normalized.Contains("hotel", StringComparison.Ordinal))
        {
            return new CompanionPlaceRoleIntent("hotel", ["lodging"], ["hotel", "lodging"], ["restaurant", "bar"], [], "strict");
        }

        return new CompanionPlaceRoleIntent(null, [], [], [], [], "loose");
    }

    private static bool IsSingleTokenEntity(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Trim().Length <= 40
               && !value.Trim().Contains(' ', StringComparison.Ordinal)
               && IsLikelyBrand(value);
    }

    private static double? TryReadDouble(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var raw)
               && double.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static IReadOnlyList<string> MergeDistinct(params IReadOnlyList<string>?[] values)
    {
        return values
            .Where(static value => value is not null)
            .SelectMany(static value => value!)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    }
}
