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
                intent = NormalizeIntent(JsonSerializer.Deserialize<CompanionSemanticIntent>(intentJson, JsonOptions));
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
            Role: new CompanionPlaceRoleIntent(null, [], [], [], [], "loose"),
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

    private static CompanionSemanticIntent? NormalizeIntent(CompanionSemanticIntent? intent)
    {
        if (intent is null || intent.Role is not null)
        {
            return intent;
        }

        return intent with
        {
            Role = new CompanionPlaceRoleIntent(null, [], [], [], [], "loose")
        };
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
