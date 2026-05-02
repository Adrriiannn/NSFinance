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

public sealed record CompanionSemanticIntent(
    string IntentFamily,
    string ActionKind,
    string? PlaceQuery,
    string? BrandOrEntity,
    CompanionLocationIntent Location,
    IReadOnlyList<string> HardFilters,
    IReadOnlyList<string> NegativeFilters,
    IReadOnlyList<string> SoftPreferences,
    IReadOnlyList<string> NonSearchablePreferences,
    IReadOnlyList<string> RequestedDetailFields,
    string RankingGoal,
    int? RequestedMaxResults,
    double Confidence,
    IReadOnlyList<string> Ambiguities);

public sealed record CompanionLocationIntent(
    string Mode,
    string? AreaText,
    double? Latitude,
    double? Longitude,
    bool RequiresLocation);

public sealed record CompanionPlacePoolCandidate(
    string PlaceId,
    string DisplayName,
    string? PrimaryType,
    string? PrimaryTypeDisplayName,
    IReadOnlyList<string> Types,
    double? Latitude,
    double? Longitude,
    double? DistanceMeters,
    string? ShortFormattedAddress,
    double? Rating,
    int? UserRatingCount,
    string? PriceLevel,
    bool? OpenNow,
    IReadOnlyDictionary<string, string> LightweightAttributes);

public sealed record CompanionPlaceCandidatePoolResult(
    IReadOnlyList<CompanionPlacePoolCandidate> Candidates,
    IReadOnlyList<string> QueryPasses,
    IReadOnlyList<string> Diagnostics,
    bool UsedCache,
    string? FailureReason);

public sealed record CompanionPlaceRejectedCandidate(
    string PlaceId,
    string DisplayName,
    string Reason);

public sealed record CompanionPlaceConstraintResult(
    IReadOnlyList<CompanionPlacePoolCandidate> Candidates,
    IReadOnlyList<CompanionPlaceRejectedCandidate> Rejected,
    IReadOnlyList<string> AppliedHardFilters,
    IReadOnlyList<string> AppliedSoftPreferences,
    IReadOnlyList<string> NonSearchablePreferences,
    IReadOnlyList<string> Diagnostics);

public sealed record CompanionPlaceIntelligenceRankingResult(
    IReadOnlyList<CompanionPlacePoolCandidate> RankedCandidates,
    IReadOnlyList<string> Diagnostics);

public sealed record CompanionPlaceFinalistResult(
    CompanionStructuredResults? StructuredResults,
    IReadOnlyList<CompanionPlacePoolCandidate> Finalists,
    IReadOnlyList<string> Diagnostics,
    int EnrichedCount);

public sealed record CompanionPlaceSearchContext(
    CompanionSemanticIntent Intent,
    IReadOnlyList<CompanionPlacePoolCandidate> CandidatePool,
    CompanionStructuredResults? VisibleCards,
    ResultContextSnapshot? ResultContext);

public interface ICompanionSemanticIntentService
{
    CompanionSemanticIntent Build(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ConversationIntelligenceResult? intelligence,
        CompanionResolvedAction? resolvedAction);
}

public interface ICompanionPlaceCandidatePoolService
{
    Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
        CompanionSemanticIntent intent,
        UserChatRequest request,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceConstraintEngine
{
    CompanionPlaceConstraintResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates);
}

public interface ICompanionPlaceIntelligenceRankingService
{
    CompanionPlaceIntelligenceRankingResult Rank(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates);
}

public interface ICompanionPlaceFinalistEnrichmentService
{
    Task<CompanionPlaceFinalistResult> EnrichAsync(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> rankedCandidates,
        int maxCards,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceSessionMemoryService
{
    Task SaveSearchContextAsync(
        UserChatRequest request,
        ConversationStateSnapshot state,
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidatePool,
        CompanionStructuredResults? visibleCards,
        CancellationToken cancellationToken);

    Task<CompanionPlaceSearchContext?> LoadActiveSearchContextAsync(
        UserChatRequest request,
        ResultContextSnapshot? activeResultContext,
        CancellationToken cancellationToken);
}

public interface IPlacesShortLivedCache
{
    Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct);
    Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct);
}

public interface IPlaceRegistryService
{
    Task RegisterSeenAsync(
        string provider,
        string providerPlaceId,
        IReadOnlyList<string> internalTags,
        CancellationToken cancellationToken);
}

public sealed class CompanionSemanticIntentService : ICompanionSemanticIntentService
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
        if (IsClosing(request.UserMessage))
        {
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
            resolvedAction?.IncludeConcepts.FirstOrDefault(IsLikelyBrand));
        var location = BuildLocationIntent(request, state, interpretation, retrievalPlan, resolvedAction);
        var hardFilters = MergeDistinct(
            resolvedAction?.Requirement is null ? [] : [resolvedAction.Requirement],
            interpretation?.PlacePlan.TimeFilters,
            ExtractHardFilters(request.UserMessage));
        var negativeFilters = MergeDistinct(
            resolvedAction?.ExcludeConcepts,
            interpretation?.PlacePlan.ExcludeTypes,
            retrievalPlan?.ExcludedTypes,
            ExtractNegativeFilters(request.UserMessage, placeQuery));
        var softPreferences = MergeDistinct(
            resolvedAction?.Preferences,
            interpretation?.PlacePlan.Preferences,
            ExtractSoftPreferences(request.UserMessage));
        var nonSearchable = ExtractNonSearchablePreferences(request.UserMessage);
        var detailFields = ExtractRequestedDetailFields(request.UserMessage, resolvedAction?.Requirement);
        var rankingGoal = ResolveRankingGoal(actionKind, request.UserMessage, brand, placeQuery, resolvedAction?.SortGoal);
        var requestedMax = ExtractRequestedMaxResults(request.UserMessage, resolvedAction);

        return new CompanionSemanticIntent(
            IntentFamily: "places",
            ActionKind: actionKind,
            PlaceQuery: NormalizePlaceQuery(placeQuery, request.UserMessage, resultContext),
            BrandOrEntity: brand,
            Location: location,
            HardFilters: hardFilters,
            NegativeFilters: negativeFilters,
            SoftPreferences: softPreferences,
            NonSearchablePreferences: nonSearchable,
            RequestedDetailFields: detailFields,
            RankingGoal: rankingGoal,
            RequestedMaxResults: requestedMax,
            Confidence: conversationConfidence(intelligence, interpretation, resolvedAction),
            Ambiguities: interpretation?.Ambiguities ?? []);

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

    private static IReadOnlyList<string> ExtractHardFilters(string message)
    {
        var normalized = Normalize(message);
        var filters = new List<string>();
        var rating = Regex.Match(normalized, @"(?:rated\s*)?([0-5](?:\.\d)?)\s*(?:\+|and up|or higher)");
        if (rating.Success)
        {
            filters.Add($"rating>={rating.Groups[1].Value}");
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

    private static IReadOnlyList<string> ExtractNegativeFilters(string message, string? placeQuery)
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

    private static IReadOnlyList<string> ExtractSoftPreferences(string message)
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

    private static IReadOnlyList<string> ExtractNonSearchablePreferences(string message)
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

    private static IReadOnlyList<string> ExtractRequestedDetailFields(string message, string? requirement)
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

    private static bool IsClosing(string message)
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

public sealed class CompanionPlaceCandidatePoolService(
    ICompanionPlaceDiscoveryService discoveryService,
    IPlaceRegistryService placeRegistryService,
    IOptions<GooglePlacesOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceCandidatePoolService
{
    private const int PoolTargetCount = 50;
    private const int ProviderPageSize = 16;
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
        CompanionSemanticIntent intent,
        UserChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var diagnostics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queryPasses = BuildQueryPasses(intent);
        var candidatesById = new Dictionary<string, CompanionPlacePoolCandidate>(StringComparer.OrdinalIgnoreCase);
        var country = ResolveCountryCode(request);
        var radiusMeters = ResolveRadiusMeters(intent);

        await telemetry.TrackAsync(
            "places.pool.build_started",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["queryPassCount"] = queryPasses.Count,
                ["radiusMeters"] = radiusMeters
            },
            cancellationToken);

        foreach (var pass in queryPasses)
        {
            if (candidatesById.Count >= PoolTargetCount)
            {
                break;
            }

            if (pass == "nearby:parking" && intent.Location.Latitude.HasValue && intent.Location.Longitude.HasValue)
            {
                var nearby = await discoveryService.DiscoverNearbyAsync(
                    new CompanionNearbyDiscoveryRequest(
                        Latitude: intent.Location.Latitude.Value,
                        Longitude: intent.Location.Longitude.Value,
                        RadiusMeters: radiusMeters,
                        IncludedTypes: ["parking"],
                        CountryCode: country,
                        MaxCandidates: ProviderPageSize),
                    cancellationToken);
                diagnostics.UnionWith(nearby.Warnings);
                foreach (var candidate in nearby.Candidates.Select(item => Map(item, intent)))
                {
                    candidatesById.TryAdd(candidate.PlaceId, candidate);
                    await placeRegistryService.RegisterSeenAsync("google_places", candidate.PlaceId, BuildRegistryTags(intent), cancellationToken);
                }

                continue;
            }

            var text = await discoveryService.DiscoverAsync(
                new CompanionPlaceDiscoveryRequest(
                    Query: pass,
                    CountryCode: country,
                    Latitude: intent.Location.Latitude,
                    Longitude: intent.Location.Longitude,
                    RadiusMeters: intent.Location.Latitude.HasValue ? radiusMeters : null,
                    MaxCandidates: ProviderPageSize),
                cancellationToken);
            diagnostics.UnionWith(text.Warnings);
            foreach (var candidate in text.Candidates.Select(item => Map(item, intent)))
            {
                candidatesById.TryAdd(candidate.PlaceId, candidate);
                await placeRegistryService.RegisterSeenAsync("google_places", candidate.PlaceId, BuildRegistryTags(intent), cancellationToken);
            }
        }

        var candidates = candidatesById.Values.Take(PoolTargetCount).ToArray();
        await telemetry.TrackAsync(
            "places.pool.provider_count",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["candidatePoolCount"] = candidates.Length,
                ["queryPasses"] = queryPasses.ToArray()
            },
            cancellationToken);
        await telemetry.TrackAsync(
            "places.pool.deduped_count",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["dedupedCount"] = candidates.Length
            },
            cancellationToken);

        return new CompanionPlaceCandidatePoolResult(
            Candidates: candidates,
            QueryPasses: queryPasses,
            Diagnostics: diagnostics.ToArray(),
            UsedCache: candidates.Any(static c => c.LightweightAttributes.TryGetValue("from_cache", out var value)
                                                  && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)),
            FailureReason: candidates.Length == 0 ? "places_pool_empty" : null);
    }

    private IReadOnlyList<string> BuildQueryPasses(CompanionSemanticIntent intent)
    {
        var passes = new List<string>();
        if (intent.PlaceQuery is not null && IsParkingIntent(intent))
        {
            if (intent.Location.Latitude.HasValue && intent.Location.Longitude.HasValue)
            {
                passes.Add("nearby:parking");
            }

            passes.AddRange(["car parks", "parking"]);
        }
        else if (!string.IsNullOrWhiteSpace(intent.BrandOrEntity))
        {
            passes.Add(intent.BrandOrEntity!);
            if (!string.IsNullOrWhiteSpace(intent.PlaceQuery)
                && !string.Equals(intent.PlaceQuery, intent.BrandOrEntity, StringComparison.OrdinalIgnoreCase))
            {
                passes.Add(intent.PlaceQuery!);
            }
        }
        else if (!string.IsNullOrWhiteSpace(intent.PlaceQuery))
        {
            passes.Add(intent.PlaceQuery!);
        }
        else
        {
            passes.Add("local places");
        }

        if (intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase)
            && !passes.Any(static pass => pass.Contains("fine dining", StringComparison.OrdinalIgnoreCase)))
        {
            passes.Insert(0, "fine dining restaurants");
        }

        return passes
            .Where(static pass => !string.IsNullOrWhiteSpace(pass))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
    }

    private int ResolveRadiusMeters(CompanionSemanticIntent intent)
    {
        if (intent.Location.Mode == "near_me")
        {
            return 15_000;
        }

        return Math.Clamp(placesOptions.DefaultSearchRadiusMeters <= 0 ? 15_000 : placesOptions.DefaultSearchRadiusMeters, 1_000, 25_000);
    }

    private static bool IsParkingIntent(CompanionSemanticIntent intent)
    {
        return Normalize(intent.PlaceQuery).Contains("car park", StringComparison.Ordinal)
               || Normalize(intent.PlaceQuery).Contains("parking", StringComparison.Ordinal)
               || intent.HardFilters.Contains("parking_available_or_nearby", StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveCountryCode(UserChatRequest request)
    {
        if (request.Metadata?.TryGetValue("country_code", out var value) == true
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToUpperInvariant();
        }

        return null;
    }

    private static CompanionPlacePoolCandidate Map(CompanionPlaceCandidate candidate, CompanionSemanticIntent intent)
    {
        var distance = TryComputeDistanceMeters(intent.Location.Latitude, intent.Location.Longitude, candidate.Location);
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add("resource_name", candidate.ResourceName);
        Add("formatted_address", candidate.FormattedAddress);
        Add("google_maps_uri", candidate.GoogleMapsUri);
        Add("website_url", candidate.WebsiteUri);
        Add("phone_number", candidate.NationalPhoneNumber);
        Add("business_status", candidate.BusinessStatus);
        Add("photo_names", candidate.Photos is { Count: > 0 } ? string.Join("|", candidate.Photos.Select(x => x.Name)) : null);
        Add("photos_json", candidate.Photos is { Count: > 0 } ? JsonSerializer.Serialize(candidate.Photos) : null);
        Add("takeout", candidate.Takeout?.ToString());
        Add("delivery", candidate.Delivery?.ToString());
        Add("dine_in", candidate.DineIn?.ToString());
        Add("reservable", candidate.Reservable?.ToString());
        Add("wheelchair_accessible_parking", candidate.AccessibilityOptions.WheelchairAccessibleParking?.ToString());
        Add("serves_wine", candidate.ServesWine?.ToString());
        Add("serves_cocktails", candidate.ServesCocktails?.ToString());
        Add("allows_dogs", candidate.AllowsDogs?.ToString());
        Add("editorial_summary", candidate.EditorialSummary.Text);

        return new CompanionPlacePoolCandidate(
            PlaceId: candidate.PlaceId,
            DisplayName: candidate.DisplayName ?? candidate.DisplayName ?? candidate.PlaceId,
            PrimaryType: candidate.PrimaryType,
            PrimaryTypeDisplayName: candidate.PrimaryTypeDisplayName,
            Types: candidate.Types,
            Latitude: candidate.Location?.Latitude,
            Longitude: candidate.Location?.Longitude,
            DistanceMeters: distance,
            ShortFormattedAddress: candidate.ShortFormattedAddress,
            Rating: candidate.Rating,
            UserRatingCount: candidate.UserRatingCount,
            PriceLevel: candidate.PriceLevel,
            OpenNow: candidate.OpeningHours.OpenNow,
            LightweightAttributes: attributes);

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[key] = value.Trim();
            }
        }
    }

    private static IReadOnlyList<string> BuildRegistryTags(CompanionSemanticIntent intent)
    {
        return new[]
            {
                intent.BrandOrEntity is null ? null : "brand",
                intent.PlaceQuery,
                intent.RankingGoal
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double? TryComputeDistanceMeters(double? sourceLatitude, double? sourceLongitude, PlaceLocationSummary? target)
    {
        if (!sourceLatitude.HasValue || !sourceLongitude.HasValue || target is null)
        {
            return null;
        }

        const double EarthRadiusMeters = 6_371_000d;
        var sourceLatRad = DegreesToRadians(sourceLatitude.Value);
        var targetLatRad = DegreesToRadians(target.Latitude);
        var deltaLat = DegreesToRadians(target.Latitude - sourceLatitude.Value);
        var deltaLon = DegreesToRadians(target.Longitude - sourceLongitude.Value);
        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)
                + (Math.Cos(sourceLatRad) * Math.Cos(targetLatRad)
                   * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d));
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double value) => value * (Math.PI / 180d);

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
    }
}

public sealed class CompanionPlaceConstraintEngine(IChatTelemetry telemetry) : ICompanionPlaceConstraintEngine
{
    public CompanionPlaceConstraintResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        var accepted = new List<CompanionPlacePoolCandidate>();
        var rejected = new List<CompanionPlaceRejectedCandidate>();
        foreach (var candidate in candidates)
        {
            var rejection = EvaluateHardRejection(intent, candidate);
            if (rejection is null)
            {
                accepted.Add(candidate);
            }
            else
            {
                rejected.Add(new CompanionPlaceRejectedCandidate(candidate.PlaceId, candidate.DisplayName, rejection));
            }
        }

        if (accepted.Count == 0 && candidates.Count > 0)
        {
            accepted.AddRange(candidates);
        }

        _ = telemetry.TrackAsync(
            "places.constraint.applied",
            new Dictionary<string, object?>
            {
                ["candidatePoolCount"] = candidates.Count,
                ["hardFilterCount"] = intent.HardFilters.Count,
                ["softPreferenceCount"] = intent.SoftPreferences.Count,
                ["negativeFilterCount"] = intent.NegativeFilters.Count,
                ["rejectedByHardFilterCount"] = rejected.Count
            },
            CancellationToken.None);

        return new CompanionPlaceConstraintResult(
            Candidates: accepted,
            Rejected: rejected,
            AppliedHardFilters: intent.HardFilters,
            AppliedSoftPreferences: intent.SoftPreferences,
            NonSearchablePreferences: intent.NonSearchablePreferences,
            Diagnostics: rejected.Count > 0 ? ["places_constraint_hard_filters_applied"] : []);
    }

    private static string? EvaluateHardRejection(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var haystack = BuildHaystack(candidate);
        foreach (var negative in intent.NegativeFilters.Select(Normalize))
        {
            if (negative.Length == 0)
            {
                continue;
            }

            if (negative is "mcdonalds" && haystack.Contains("mcdonald", StringComparison.Ordinal))
            {
                return "negative_filter:mcdonalds";
            }

            if ((negative.Contains("fast_food", StringComparison.Ordinal) || negative.Contains("fast food", StringComparison.Ordinal))
                && (haystack.Contains("fast food", StringComparison.Ordinal)
                    || haystack.Contains("fast_food", StringComparison.Ordinal)
                    || haystack.Contains("fast_food_restaurant", StringComparison.Ordinal)))
            {
                return "negative_filter:fast_food";
            }

            if (negative.Contains("takeaway", StringComparison.Ordinal)
                && (haystack.Contains("takeaway", StringComparison.Ordinal)
                    || haystack.Contains("meal_takeaway", StringComparison.Ordinal)))
            {
                return "negative_filter:takeaway";
            }
        }

        foreach (var filter in intent.HardFilters)
        {
            var normalized = Normalize(filter);
            if (normalized == "open now" || normalized == "open_now")
            {
                if (candidate.OpenNow != true)
                {
                    return "hard_filter:open_now";
                }
            }
            else if (normalized.StartsWith("rating>=", StringComparison.Ordinal)
                     && double.TryParse(normalized["rating>=".Length..], CultureInfo.InvariantCulture, out var minRating)
                     && (candidate.Rating ?? 0d) < minRating)
            {
                return "hard_filter:rating";
            }
            else if (normalized.Contains("parking", StringComparison.Ordinal)
                     && HasParkingEvidence(candidate) == false
                     && IsParkingSearch(intent))
            {
                return "hard_filter:parking";
            }
        }

        return null;
    }

    private static bool HasParkingEvidence(CompanionPlacePoolCandidate candidate)
    {
        return BuildHaystack(candidate).Contains("parking", StringComparison.Ordinal);
    }

    private static bool IsParkingSearch(CompanionSemanticIntent intent)
    {
        var query = Normalize(intent.PlaceQuery);
        return query.Contains("parking", StringComparison.Ordinal) || query.Contains("car park", StringComparison.Ordinal);
    }

    private static string BuildHaystack(CompanionPlacePoolCandidate candidate)
    {
        return Normalize(string.Join(
            ' ',
            candidate.DisplayName,
            candidate.PrimaryType,
            candidate.PrimaryTypeDisplayName,
            string.Join(' ', candidate.Types),
            candidate.ShortFormattedAddress,
            string.Join(' ', candidate.LightweightAttributes.Values)));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
    }
}

public sealed class CompanionPlaceIntelligenceRankingService : ICompanionPlaceIntelligenceRankingService
{
    public CompanionPlaceIntelligenceRankingResult Rank(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        var ranked = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = Score(intent, candidate)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => intent.RankingGoal.Contains("distance", StringComparison.OrdinalIgnoreCase)
                ? item.Candidate.DistanceMeters ?? double.MaxValue
                : double.MaxValue)
            .ThenByDescending(item => item.Candidate.Rating ?? 0d)
            .ThenBy(item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Candidate)
            .ToArray();

        return new CompanionPlaceIntelligenceRankingResult(
            RankedCandidates: ranked,
            Diagnostics: ["places_ranking_v2_completed"]);
    }

    private static double Score(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var concept = ScoreConcept(intent, candidate);
        var distance = ScoreDistance(candidate.DistanceMeters);
        var rating = ScoreRating(candidate);
        var open = candidate.OpenNow == true ? 0.08d : 0d;
        var soft = ScoreSoftPreferences(intent, candidate);

        if (intent.RankingGoal == "brand_match_then_distance")
        {
            return (ScoreBrand(intent, candidate) * 0.60d) + (distance * 0.25d) + (rating * 0.15d);
        }

        if (intent.RankingGoal == "concept_fit_then_distance")
        {
            return (concept * 0.52d) + (rating * 0.22d) + (distance * 0.16d) + (soft * 0.10d);
        }

        if (intent.RankingGoal == "relevance_rating_then_distance")
        {
            return (concept * 0.45d) + (rating * 0.35d) + (distance * 0.12d) + open;
        }

        if (intent.RankingGoal == "parking_match_then_distance")
        {
            return (ScoreParking(candidate) * 0.55d) + (distance * 0.30d) + (rating * 0.15d);
        }

        if (intent.RankingGoal == "distance")
        {
            return (distance * 0.70d) + (concept * 0.20d) + (rating * 0.10d);
        }

        return (concept * 0.40d) + (distance * 0.30d) + (rating * 0.20d) + (soft * 0.10d) + open;
    }

    private static double ScoreBrand(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(intent.BrandOrEntity))
        {
            return 0.45d;
        }

        return candidate.DisplayName.Contains(intent.BrandOrEntity, StringComparison.OrdinalIgnoreCase) ? 1d : 0.05d;
    }

    private static double ScoreConcept(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var query = Normalize(intent.PlaceQuery);
        var haystack = Normalize($"{candidate.DisplayName} {candidate.PrimaryType} {candidate.PrimaryTypeDisplayName} {string.Join(' ', candidate.Types)} {string.Join(' ', candidate.LightweightAttributes.Values)}");
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0.50d;
        }

        if (query.Contains("fine dining", StringComparison.Ordinal)
            || intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
        {
            var upscale = haystack.Contains("fine dining", StringComparison.Ordinal)
                          || haystack.Contains("upscale", StringComparison.Ordinal)
                          || haystack.Contains("restaurant", StringComparison.Ordinal)
                          && (candidate.PriceLevel?.Contains("EXPENSIVE", StringComparison.OrdinalIgnoreCase) == true
                              || candidate.LightweightAttributes.TryGetValue("reservable", out var reservable)
                              && bool.TryParse(reservable, out var parsed)
                              && parsed);
            return upscale ? 1d : 0.20d;
        }

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 4 && haystack.Contains(token, StringComparison.Ordinal))
            {
                return 0.92d;
            }
        }

        return 0.35d;
    }

    private static double ScoreParking(CompanionPlacePoolCandidate candidate)
    {
        var haystack = Normalize($"{candidate.PrimaryType} {candidate.PrimaryTypeDisplayName} {string.Join(' ', candidate.Types)} {string.Join(' ', candidate.LightweightAttributes.Values)}");
        return haystack.Contains("parking", StringComparison.Ordinal) || haystack.Contains("car park", StringComparison.Ordinal)
            ? 1d
            : 0.25d;
    }

    private static double ScoreSoftPreferences(CompanionSemanticIntent intent, CompanionPlacePoolCandidate candidate)
    {
        var score = 0.40d;
        if (intent.SoftPreferences.Contains("not_too_expensive", StringComparer.OrdinalIgnoreCase))
        {
            score = Math.Max(score, candidate.PriceLevel is null
                ? 0.50d
                : candidate.PriceLevel.Contains("INEXPENSIVE", StringComparison.OrdinalIgnoreCase)
                  || candidate.PriceLevel.Contains("MODERATE", StringComparison.OrdinalIgnoreCase)
                    ? 1d
                    : 0.15d);
        }

        if (intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
        {
            score = Math.Max(score, candidate.PriceLevel?.Contains("EXPENSIVE", StringComparison.OrdinalIgnoreCase) == true ? 0.80d : 0.50d);
        }

        return score;
    }

    private static double ScoreDistance(double? distanceMeters)
    {
        if (!distanceMeters.HasValue)
        {
            return 0.15d;
        }

        var distance = Math.Max(0d, distanceMeters.Value);
        return distance switch
        {
            <= 500d => 1.00d,
            <= 1_500d => 0.88d,
            <= 3_000d => 0.70d,
            <= 6_000d => 0.48d,
            <= 10_000d => 0.26d,
            _ => 0.08d
        };
    }

    private static double ScoreRating(CompanionPlacePoolCandidate candidate)
    {
        var rating = candidate.Rating.HasValue
            ? Math.Clamp((candidate.Rating.Value - 3.0d) / 2.0d, 0d, 1d)
            : 0.35d;
        var count = candidate.UserRatingCount.HasValue
            ? Math.Clamp(Math.Log10(candidate.UserRatingCount.Value + 1) / 3d, 0d, 1d)
            : 0.20d;
        return (rating * 0.75d) + (count * 0.25d);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
    }
}

public sealed class CompanionPlaceFinalistEnrichmentService(
    IPlaceDetailsService placeDetailsService,
    IGooglePlacesPhotoService? photoService,
    IPlacesShortLivedCache cache,
    IOptions<GooglePlacesOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceFinalistEnrichmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CompanionPlaceFinalistResult> EnrichAsync(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> rankedCandidates,
        int maxCards,
        CancellationToken cancellationToken)
    {
        var finalists = rankedCandidates.Take(Math.Clamp(maxCards, 1, 10)).ToArray();
        var cards = new List<CompanionPlaceCardResult>(finalists.Length);
        var enrichedCount = 0;
        foreach (var candidate in finalists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = await GetDetailsCachedAsync(candidate.PlaceId, cancellationToken);
            if (details is not null)
            {
                enrichedCount++;
            }

            cards.Add(BuildCard(candidate, details));
        }

        await telemetry.TrackAsync(
            "places.finalists.enriched_count",
            new Dictionary<string, object?>
            {
                ["enrichedCount"] = enrichedCount,
                ["visibleCardCount"] = cards.Count
            },
            cancellationToken);

        return new CompanionPlaceFinalistResult(
            StructuredResults: cards.Count == 0 ? null : new CompanionStructuredResults("places", cards),
            Finalists: finalists,
            Diagnostics: ["places_finalists_enriched"],
            EnrichedCount: enrichedCount);
    }

    private async Task<PlaceDetailsResult?> GetDetailsCachedAsync(string placeId, CancellationToken cancellationToken)
    {
        var cacheKey = Hash("place_details_v2");
        var cached = await cache.GetAsync<PlaceDetailsResult>("google_places", placeId, cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var details = await placeDetailsService.GetDetailsAsync(placeId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(details.PlaceId))
            {
                await cache.SetAsync(
                    "google_places",
                    placeId,
                    cacheKey,
                    details,
                    TimeSpan.FromSeconds(Math.Max(30, options.Value.PlaceDetailsCacheTtlSeconds)),
                    cancellationToken);
                return details;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return null;
    }

    private CompanionPlaceCardResult BuildCard(CompanionPlacePoolCandidate candidate, PlaceDetailsResult? details)
    {
        var photoUrls = BuildPhotoUrls(candidate);
        var address = details?.Address ?? candidate.LightweightAttributes.GetValueOrDefault("formatted_address");
        return new CompanionPlaceCardResult(
            Id: candidate.PlaceId,
            Name: details?.Name ?? candidate.DisplayName,
            DistanceMeters: candidate.DistanceMeters,
            PhotoUrl: photoUrls.FirstOrDefault(),
            PhotoUrls: photoUrls,
            FormattedAddress: address,
            ShortFormattedAddress: candidate.ShortFormattedAddress,
            Rating: details?.Rating ?? candidate.Rating,
            OpenNow: details?.OpeningHours?.OpenNow ?? candidate.OpenNow,
            PriceLevel: details?.PriceLevel ?? candidate.PriceLevel,
            WebsiteUrl: details?.Website ?? candidate.LightweightAttributes.GetValueOrDefault("website_url"),
            Category: details?.PrimaryTypeDisplayName ?? candidate.PrimaryTypeDisplayName ?? Humanize(candidate.PrimaryType),
            PrimaryTypeDisplayName: details?.PrimaryTypeDisplayName ?? candidate.PrimaryTypeDisplayName,
            ClosesInMinutes: null,
            OpensInMinutes: details?.OpeningHours?.OpenNow == false ? TryComputeFutureMinutes(details.OpeningHours.NextOpenTimeUtc) : null,
            PhoneNumber: details?.NationalPhoneNumber ?? candidate.LightweightAttributes.GetValueOrDefault("phone_number"),
            MenuUrl: TryResolveMenuUrl(details?.Website ?? candidate.LightweightAttributes.GetValueOrDefault("website_url")),
            GoogleMapsUri: details?.GoogleMapsUri ?? candidate.LightweightAttributes.GetValueOrDefault("google_maps_uri"),
            Latitude: details?.Location?.Latitude ?? candidate.Latitude,
            Longitude: details?.Location?.Longitude ?? candidate.Longitude);
    }

    private IReadOnlyList<string> BuildPhotoUrls(CompanionPlacePoolCandidate candidate)
    {
        if (photoService is null
            || !candidate.LightweightAttributes.TryGetValue("photos_json", out var json)
            || string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<IReadOnlyList<PlacePhotoSummary>>(json, JsonOptions) ?? [])
                .Select(photo => photoService.BuildAppPhotoUrl(photo.Name, 900, 520))
                .Where(static url => !string.IsNullOrWhiteSpace(url))
                .Select(static url => url!)
                .Take(8)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? TryComputeFutureMinutes(DateTimeOffset? futureUtc)
    {
        if (!futureUtc.HasValue)
        {
            return null;
        }

        var minutes = (int)Math.Ceiling((futureUtc.Value - DateTimeOffset.UtcNow).TotalMinutes);
        return minutes > 0 && minutes < (7 * 24 * 60) ? minutes : null;
    }

    private static string? TryResolveMenuUrl(string? websiteUri)
    {
        return string.IsNullOrWhiteSpace(websiteUri) || !websiteUri.Contains("menu", StringComparison.OrdinalIgnoreCase)
            ? null
            : websiteUri.Trim();
    }

    private static string? Humanize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((word, index) => index == 0
                    ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())
                    : word.ToLowerInvariant()));
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

public sealed class CompanionPlaceSessionMemoryService(
    IResultContextService resultContextService,
    IChatTelemetry telemetry) : ICompanionPlaceSessionMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveSearchContextAsync(
        UserChatRequest request,
        ConversationStateSnapshot state,
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidatePool,
        CompanionStructuredResults? visibleCards,
        CancellationToken cancellationToken)
    {
        if (!request.UserId.HasValue || !request.ConversationThreadId.HasValue)
        {
            return;
        }

        var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pipeline"] = "places_intelligence_v2",
            ["semantic_intent_json"] = JsonSerializer.Serialize(intent, JsonOptions),
            ["semantic_place_query"] = intent.PlaceQuery ?? string.Empty,
            ["candidate_pool_count"] = candidatePool.Count.ToString(CultureInfo.InvariantCulture),
            ["visible_card_count"] = (visibleCards?.Items.Count ?? 0).ToString(CultureInfo.InvariantCulture)
        };

        var entities = candidatePool
            .Select((candidate, index) => new ResultContextEntity(
                EntityId: candidate.PlaceId,
                Label: candidate.DisplayName,
                Rank: index + 1,
                StableReference: candidate.LightweightAttributes.GetValueOrDefault("google_maps_uri"),
                Category: candidate.PrimaryTypeDisplayName ?? candidate.PrimaryType,
                Attributes: BuildAttributes(candidate, visibleCards)))
            .ToArray();

        await resultContextService.WriteAsync(
            new ResultContextWriteRequest(
                UserId: request.UserId.Value,
                ConversationThreadId: request.ConversationThreadId.Value,
                SourceMode: ConversationMode.Exploration,
                SourceSubtype: ExplorationSubtype.Structured,
                QueryFingerprint: BuildFingerprint(intent),
                NormalizedConstraints: constraints,
                SuggestedEntities: entities,
                SelectedEntityId: null,
                ParentResultSetId: state.ResultContextRef?.ActiveResultSetId,
                BranchRootResultSetId: state.ResultContextRef?.BranchRootResultSetId,
                CreatedUtc: DateTime.UtcNow),
            cancellationToken);

        await telemetry.TrackAsync(
            "places.session_memory.saved",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["candidatePoolCount"] = candidatePool.Count,
                ["visibleCardCount"] = visibleCards?.Items.Count ?? 0
            },
            cancellationToken);
    }

    public Task<CompanionPlaceSearchContext?> LoadActiveSearchContextAsync(
        UserChatRequest request,
        ResultContextSnapshot? activeResultContext,
        CancellationToken cancellationToken)
    {
        if (activeResultContext is null)
        {
            return Task.FromResult<CompanionPlaceSearchContext?>(null);
        }

        activeResultContext.NormalizedConstraints.TryGetValue("semantic_intent_json", out var intentJson);
        CompanionSemanticIntent? intent = null;
        if (!string.IsNullOrWhiteSpace(intentJson))
        {
            try
            {
                intent = JsonSerializer.Deserialize<CompanionSemanticIntent>(intentJson, JsonOptions);
            }
            catch (JsonException)
            {
                intent = null;
            }
        }

        var pool = activeResultContext.SuggestedEntities
            .Select(MapCandidate)
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .ToArray();
        if (intent is null && pool.Length == 0)
        {
            return Task.FromResult<CompanionPlaceSearchContext?>(null);
        }

        _ = telemetry.TrackAsync(
            "places.session_memory.used",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["candidatePoolCount"] = pool.Length
            },
            CancellationToken.None);

        return Task.FromResult<CompanionPlaceSearchContext?>(new CompanionPlaceSearchContext(
            Intent: intent ?? BuildFallbackIntent(activeResultContext),
            CandidatePool: pool,
            VisibleCards: null,
            ResultContext: activeResultContext));
    }

    private static IReadOnlyDictionary<string, string> BuildAttributes(
        CompanionPlacePoolCandidate candidate,
        CompanionStructuredResults? visibleCards)
    {
        var attributes = new Dictionary<string, string>(candidate.LightweightAttributes, StringComparer.OrdinalIgnoreCase);
        Add("primary_type", candidate.PrimaryType);
        Add("primary_type_display_name", candidate.PrimaryTypeDisplayName);
        Add("types", candidate.Types.Count > 0 ? string.Join("|", candidate.Types) : null);
        Add("latitude", candidate.Latitude?.ToString(CultureInfo.InvariantCulture));
        Add("longitude", candidate.Longitude?.ToString(CultureInfo.InvariantCulture));
        Add("distance_meters", candidate.DistanceMeters?.ToString("0", CultureInfo.InvariantCulture));
        Add("rating", candidate.Rating?.ToString("0.0", CultureInfo.InvariantCulture));
        Add("user_rating_count", candidate.UserRatingCount?.ToString(CultureInfo.InvariantCulture));
        Add("price_level", candidate.PriceLevel);
        Add("open_now", candidate.OpenNow?.ToString());
        var card = visibleCards?.Items.FirstOrDefault(item => string.Equals(item.Id, candidate.PlaceId, StringComparison.OrdinalIgnoreCase));
        if (card is not null)
        {
            Add("photo_url", card.PhotoUrl);
            Add("photo_urls", card.PhotoUrls.Count > 0 ? string.Join("|", card.PhotoUrls) : null);
        }

        return attributes;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[key] = value.Trim();
            }
        }
    }

    private static CompanionPlacePoolCandidate? MapCandidate(ResultContextEntity entity)
    {
        var attributes = entity.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new CompanionPlacePoolCandidate(
            PlaceId: entity.EntityId,
            DisplayName: entity.Label,
            PrimaryType: Read(attributes, "primary_type"),
            PrimaryTypeDisplayName: Read(attributes, "primary_type_display_name") ?? entity.Category,
            Types: ReadList(attributes, "types"),
            Latitude: ReadDouble(attributes, "latitude"),
            Longitude: ReadDouble(attributes, "longitude"),
            DistanceMeters: ReadDouble(attributes, "distance_meters"),
            ShortFormattedAddress: Read(attributes, "short_address"),
            Rating: ReadDouble(attributes, "rating"),
            UserRatingCount: ReadInt(attributes, "user_rating_count"),
            PriceLevel: Read(attributes, "price_level"),
            OpenNow: ReadBool(attributes, "open_now"),
            LightweightAttributes: attributes);
    }

    private static CompanionSemanticIntent BuildFallbackIntent(ResultContextSnapshot snapshot)
    {
        snapshot.NormalizedConstraints.TryGetValue("semantic_place_query", out var placeQuery);
        return new CompanionSemanticIntent(
            IntentFamily: "places",
            ActionKind: "filter_previous_results",
            PlaceQuery: placeQuery,
            BrandOrEntity: null,
            Location: new CompanionLocationIntent("previous_place", null, null, null, false),
            HardFilters: [],
            NegativeFilters: [],
            SoftPreferences: [],
            NonSearchablePreferences: [],
            RequestedDetailFields: [],
            RankingGoal: "preserve_previous_search_apply_filters",
            RequestedMaxResults: null,
            Confidence: 0.65d,
            Ambiguities: []);
    }

    private static string BuildFingerprint(CompanionSemanticIntent intent)
    {
        var raw = JsonSerializer.Serialize(intent, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string? Read(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static IReadOnlyList<string> ReadList(IReadOnlyDictionary<string, string> values, string key)
    {
        return Read(values, key)?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var raw) && double.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var raw) && int.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed) ? parsed : null;
    }
}

public sealed class PlacesShortLivedCache(AppDbContext dbContext, IChatTelemetry telemetry) : IPlacesShortLivedCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entry = await dbContext.PlacesShortLivedCache
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Provider == provider
                        && item.PlaceId == placeId
                        && item.FieldMaskHash == fieldMaskHash
                        && item.ExpiresAtUtc > now,
                ct);
        await telemetry.TrackAsync(
            entry is null ? "places.cache.miss" : "places.cache.hit",
            new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["fieldMaskHash"] = fieldMaskHash
            },
            ct);
        if (entry is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(entry.PayloadJson, JsonOptions);
    }

    public async Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct)
    {
        if (payload is null || ttl <= TimeSpan.Zero)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var entry = await dbContext.PlacesShortLivedCache
            .SingleOrDefaultAsync(
                item => item.Provider == provider
                        && item.PlaceId == placeId
                        && item.FieldMaskHash == fieldMaskHash,
                ct);
        if (entry is null)
        {
            entry = new PlacesShortLivedCacheEntry
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                PlaceId = placeId,
                FieldMaskHash = fieldMaskHash
            };
            dbContext.PlacesShortLivedCache.Add(entry);
        }

        entry.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        entry.CreatedAtUtc = now;
        entry.ExpiresAtUtc = now.Add(ttl);
        await dbContext.SaveChangesAsync(ct);
    }
}

public sealed class PlaceRegistryService(AppDbContext dbContext, IChatTelemetry telemetry) : IPlaceRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RegisterSeenAsync(
        string provider,
        string providerPlaceId,
        IReadOnlyList<string> internalTags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerPlaceId))
        {
            return;
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedPlaceId = providerPlaceId.Trim();
        var now = DateTime.UtcNow;
        var entity = await dbContext.PlaceRegistry
            .SingleOrDefaultAsync(
                item => item.Provider == normalizedProvider && item.ProviderPlaceId == normalizedPlaceId,
                cancellationToken);
        if (entity is null)
        {
            entity = new PlaceRegistryEntry
            {
                Id = Guid.NewGuid(),
                Provider = normalizedProvider,
                ProviderPlaceId = normalizedPlaceId,
                FirstSeenAtUtc = now
            };
            dbContext.PlaceRegistry.Add(entity);
        }

        entity.LastSeenAtUtc = now;
        entity.InternalTagsJson = JsonSerializer.Serialize(
            internalTags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToArray(),
            JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
        await telemetry.TrackAsync(
            "places.registry.place_seen",
            new Dictionary<string, object?>
            {
                ["provider"] = normalizedProvider
            },
            cancellationToken);
    }
}
