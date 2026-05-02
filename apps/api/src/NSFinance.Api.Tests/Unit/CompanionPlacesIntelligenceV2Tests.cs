using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlacesIntelligenceV2Tests
{
    [Fact]
    public void SemanticIntent_PreservesBrandSearchAndGpsGrounding()
    {
        var service = new CompanionSemanticIntentService();
        var action = new CompanionResolvedAction(
            CompanionActionKind.NewPlaceSearch,
            Reason: "brand search",
            RequiresToolExecution: true,
            RequiresClarification: false,
            ClarificationNeed: null,
            PlaceQuery: "Starbucks",
            LocationQuery: "near me",
            Requirement: null,
            SortGoal: null,
            TargetResultSetId: null,
            IncludeConcepts: ["coffee shop"],
            ExcludeConcepts: [],
            Preferences: [],
            TimeFilters: [],
            Warnings: []);

        var intent = service.Build(
            BuildRequest("Starbucks near me"),
            BuildState(),
            resultContext: null,
            interpretation: null,
            retrievalPlan: null,
            intelligence: null,
            resolvedAction: action);

        Assert.Equal("new_place_search", intent.ActionKind);
        Assert.Equal("Starbucks", intent.PlaceQuery);
        Assert.Equal("Starbucks", intent.BrandOrEntity);
        Assert.Equal("near_me", intent.Location.Mode);
        Assert.False(intent.Location.RequiresLocation);
        Assert.Equal("brand_match_then_distance", intent.RankingGoal);
    }

    [Fact]
    public void ConstraintEngine_HardFiltersFastFoodAndTakeawayForFineDining()
    {
        var engine = new CompanionPlaceConstraintEngine(new NoOpChatTelemetry());
        var intent = BuildIntent(
            placeQuery: "fine dining restaurants",
            rankingGoal: "concept_fit_then_distance",
            negativeFilters: ["fast_food", "takeaway"]);

        var result = engine.Apply(
            intent,
            [
                Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 300),
                Candidate("takeaway", "Local Takeaway", "meal_takeaway", ["meal_takeaway"], 250),
                Candidate("chapter", "Chapter One Fine Dining", "restaurant", ["restaurant"], 5_000, "Fine dining restaurant")
            ]);

        Assert.Single(result.Candidates);
        Assert.Equal("chapter", result.Candidates[0].PlaceId);
        Assert.Contains(result.Rejected, item => item.PlaceId == "mcd" && item.Reason.Contains("fast_food", StringComparison.Ordinal));
        Assert.Contains(result.Rejected, item => item.PlaceId == "takeaway" && item.Reason.Contains("takeaway", StringComparison.Ordinal));
    }

    [Fact]
    public void Ranking_FineDiningConceptOutranksNearbyFastFood()
    {
        var ranking = new CompanionPlaceIntelligenceRankingService();
        var intent = BuildIntent(
            placeQuery: "fine dining restaurants",
            rankingGoal: "concept_fit_then_distance",
            softPreferences: ["upscale"],
            negativeFilters: ["fast_food", "takeaway"]);

        var result = ranking.Rank(
            intent,
            [
                Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 300, "Fast food restaurant", rating: 4.4),
                Candidate("chapter", "Chapter One Fine Dining", "restaurant", ["restaurant"], 5_000, "Fine dining restaurant", rating: 4.7, priceLevel: "PRICE_LEVEL_EXPENSIVE"),
                Candidate("takeaway", "City Takeaway", "meal_takeaway", ["meal_takeaway"], 250, "Meal takeaway", rating: 4.8)
            ]);

        Assert.Equal("chapter", result.RankedCandidates[0].PlaceId);
    }

    [Fact]
    public async Task FinalistEnrichment_EnrichesOnlyTopTenAndUsesCache()
    {
        var details = new CountingPlaceDetailsService();
        var cache = new InMemoryPlacesShortLivedCache();
        var service = new CompanionPlaceFinalistEnrichmentService(
            details,
            photoService: null,
            cache,
            Options.Create(new GooglePlacesOptions { PlaceDetailsCacheTtlSeconds = 900 }),
            new NoOpChatTelemetry());
        var candidates = Enumerable.Range(1, 12)
            .Select(index => Candidate($"place-{index}", $"Place {index}", "cafe", ["cafe"], index * 100))
            .ToArray();

        var first = await service.EnrichAsync(
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            candidates,
            maxCards: 10,
            CancellationToken.None);
        var second = await service.EnrichAsync(
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            candidates,
            maxCards: 10,
            CancellationToken.None);

        Assert.Equal(10, first.StructuredResults?.Items.Count);
        Assert.Equal(10, first.EnrichedCount);
        Assert.Equal(10, details.CallCount);
        Assert.Equal(10, second.StructuredResults?.Items.Count);
        Assert.Equal(10, second.EnrichedCount);
        Assert.Equal(10, details.CallCount);
    }

    [Fact]
    public async Task SessionMemory_SavesHiddenPoolAndVisibleCardCounts()
    {
        var contextService = new CapturingResultContextService();
        var service = new CompanionPlaceSessionMemoryService(contextService, new NoOpChatTelemetry());
        var pool = Enumerable.Range(1, 12)
            .Select(index => Candidate($"place-{index}", $"Place {index}", "cafe", ["cafe"], index * 100))
            .ToArray();
        var cards = new CompanionStructuredResults(
            "places",
            [
                new CompanionPlaceCardResult(
                    Id: "place-1",
                    Name: "Place 1",
                    DistanceMeters: 100,
                    PhotoUrl: null,
                    PhotoUrls: [],
                    FormattedAddress: "1 Test Street",
                    ShortFormattedAddress: "1 Test Street",
                    Rating: 4.5,
                    OpenNow: true,
                    PriceLevel: null,
                    WebsiteUrl: null,
                    Category: "Cafe",
                    PrimaryTypeDisplayName: "Cafe",
                    ClosesInMinutes: null,
                    OpensInMinutes: null,
                    PhoneNumber: null,
                    MenuUrl: null,
                    GoogleMapsUri: null,
                    Latitude: 53.3,
                    Longitude: -6.2)
            ]);

        await service.SaveSearchContextAsync(
            BuildRequest("coffee shops near me"),
            BuildState(),
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            pool,
            cards,
            CancellationToken.None);

        Assert.NotNull(contextService.LastWrite);
        Assert.Equal(12, contextService.LastWrite!.SuggestedEntities.Count);
        Assert.Equal("12", contextService.LastWrite.NormalizedConstraints["candidate_pool_count"]);
        Assert.Equal("1", contextService.LastWrite.NormalizedConstraints["visible_card_count"]);
        Assert.Equal("coffee shops", contextService.LastWrite.NormalizedConstraints["semantic_place_query"]);
    }

    [Fact]
    public async Task PlaceRegistry_StoresProviderIdsWithoutDuplicatingPlaceDetails()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"places-registry-{Guid.NewGuid():N}")
                .Options);
        var registry = new PlaceRegistryService(db, new NoOpChatTelemetry());

        await registry.RegisterSeenAsync("google_places", "place-1", ["coffee"], CancellationToken.None);
        await registry.RegisterSeenAsync("google_places", "place-1", ["coffee", "nearby"], CancellationToken.None);

        var row = Assert.Single(await db.PlaceRegistry.ToListAsync());
        Assert.Equal("google_places", row.Provider);
        Assert.Equal("place-1", row.ProviderPlaceId);
        Assert.Contains("nearby", row.InternalTagsJson);
        Assert.Null(row.LastRefreshedAtUtc);
    }

    private static UserChatRequest BuildRequest(string message)
    {
        return new UserChatRequest(
            UserMessage: message,
            RecentTurns: [],
            State: BuildState(),
            CorrelationId: "test-correlation",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CompanionLocationMetadataKeys.Latitude] = "53.3498",
                [CompanionLocationMetadataKeys.Longitude] = "-6.2603",
                [CompanionLocationMetadataKeys.RadiusMeters] = "1200"
            },
            UserId: Guid.NewGuid(),
            ConversationThreadId: Guid.NewGuid());
    }

    private static ConversationStateSnapshot BuildState()
    {
        return new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: null,
            MerchantInvestigationSubject: null,
            RecentConclusions: []);
    }

    private static CompanionSemanticIntent BuildIntent(
        string placeQuery,
        string rankingGoal,
        IReadOnlyList<string>? hardFilters = null,
        IReadOnlyList<string>? negativeFilters = null,
        IReadOnlyList<string>? softPreferences = null)
    {
        return new CompanionSemanticIntent(
            IntentFamily: "places",
            ActionKind: "new_place_search",
            PlaceQuery: placeQuery,
            BrandOrEntity: null,
            Location: new CompanionLocationIntent("near_me", null, 53.3498, -6.2603, RequiresLocation: false),
            HardFilters: hardFilters ?? [],
            NegativeFilters: negativeFilters ?? [],
            SoftPreferences: softPreferences ?? [],
            NonSearchablePreferences: [],
            RequestedDetailFields: [],
            RankingGoal: rankingGoal,
            RequestedMaxResults: null,
            Confidence: 0.9,
            Ambiguities: []);
    }

    private static CompanionPlacePoolCandidate Candidate(
        string id,
        string name,
        string? primaryType,
        IReadOnlyList<string> types,
        double? distanceMeters,
        string? primaryTypeDisplayName = null,
        double? rating = 4.5,
        string? priceLevel = null)
    {
        return new CompanionPlacePoolCandidate(
            PlaceId: id,
            DisplayName: name,
            PrimaryType: primaryType,
            PrimaryTypeDisplayName: primaryTypeDisplayName ?? primaryType,
            Types: types,
            Latitude: 53.3,
            Longitude: -6.2,
            DistanceMeters: distanceMeters,
            ShortFormattedAddress: "Test Street",
            Rating: rating,
            UserRatingCount: 100,
            PriceLevel: priceLevel,
            OpenNow: true,
            LightweightAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class NoOpChatTelemetry : IChatTelemetry
    {
        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CountingPlaceDetailsService : IPlaceDetailsService
    {
        public int CallCount { get; private set; }

        public Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                new PlaceDetailsResult(
                    PlaceId: placeId,
                    Name: placeId,
                    Address: "1 Test Street",
                    Website: $"https://{placeId}.example",
                    PriceLevel: null,
                    NationalPhoneNumber: "01 234 5678",
                    GoogleMapsUri: $"https://maps.example/{placeId}",
                    BusinessStatus: "OPERATIONAL",
                    Rating: 4.6,
                    UserRatingCount: 120,
                    PrimaryType: "cafe",
                    PrimaryTypeDisplayName: "Cafe",
                    Types: ["cafe"],
                    OpeningHours: new PlaceOpeningHoursSummary(true, [], null),
                    PaymentOptions: null,
                    AccessibilityOptions: null,
                    EditorialSummary: null,
                    Location: new PlaceLocationSummary(53.3, -6.2)));
        }
    }

    private sealed class InMemoryPlacesShortLivedCache : IPlacesShortLivedCache
    {
        private readonly Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct)
        {
            return Task.FromResult(values.TryGetValue(Key(provider, placeId, fieldMaskHash), out var value) ? (T)value : default);
        }

        public Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct)
        {
            if (payload is not null)
            {
                values[Key(provider, placeId, fieldMaskHash)] = payload;
            }

            return Task.CompletedTask;
        }

        private static string Key(string provider, string placeId, string fieldMaskHash)
        {
            return $"{provider}:{placeId}:{fieldMaskHash}";
        }
    }

    private sealed class CapturingResultContextService : IResultContextService
    {
        public ResultContextWriteRequest? LastWrite { get; private set; }

        public Task<ResultContextReadResult> ReadAsync(
            ResultContextReadRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ResultContextReadResult(
                    ActiveResultContext: null,
                    BindingClassification: ResultContextBindingClassification.None,
                    UsedClientResultSetId: false,
                    ExpiredBindingCleared: false,
                    ReasonCodes: []));
        }

        public Task<ResultContextWriteResult> WriteAsync(
            ResultContextWriteRequest request,
            CancellationToken cancellationToken)
        {
            LastWrite = request;
            var resultSetId = Guid.NewGuid();
            var snapshot = new ResultContextSnapshot(
                ResultSetId: resultSetId,
                ParentResultSetId: request.ParentResultSetId,
                BranchRootResultSetId: request.BranchRootResultSetId ?? resultSetId,
                SourceMode: request.SourceMode,
                SourceSubtype: request.SourceSubtype,
                QueryFingerprint: request.QueryFingerprint,
                NormalizedConstraints: request.NormalizedConstraints,
                SuggestedEntities: request.SuggestedEntities,
                SelectedEntityId: request.SelectedEntityId,
                ActiveUntilUtc: request.CreatedUtc.AddMinutes(30),
                ExpiresUtc: request.CreatedUtc.AddHours(2),
                IsExpired: false,
                IsActiveWindowExpired: false);
            return Task.FromResult(
                new ResultContextWriteResult(
                    snapshot,
                    new ConversationResultContextReference(
                        resultSetId,
                        snapshot.BranchRootResultSetId,
                        snapshot.ActiveUntilUtc,
                        snapshot.ExpiresUtc),
                    ReasonCodes: []));
        }

        public Task<ResultContextWriteResult?> TrySelectEntityAsync(
            Guid userId,
            Guid conversationThreadId,
            Guid resultSetId,
            string selectedEntityId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ResultContextWriteResult?>(null);
        }

        public Task ClearExpiredBindingsAsync(Guid conversationThreadId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
