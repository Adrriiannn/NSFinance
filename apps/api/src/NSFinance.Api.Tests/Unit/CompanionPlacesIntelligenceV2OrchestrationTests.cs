using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlacesIntelligenceV2OrchestrationTests
{
    [Fact]
    public async Task StarbucksNearMe_UsesV2PoolAndReturnsStructuredCards()
    {
        var pool = new FixedPoolService(
            [
                Candidate("starbucks-1", "Starbucks Santry", "cafe", ["cafe"], 400, "Coffee shop"),
                Candidate("other-1", "Other Coffee", "cafe", ["cafe"], 200, "Coffee shop")
            ]);
        var telemetry = new RecordingTelemetry();
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.NewPlaceSearch, placeQuery: "Starbucks", locationQuery: "near me"),
            pool,
            telemetry: telemetry);

        var response = await orchestrator.ExecuteAsync(Request("Starbucks near me"), CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("places", response.StructuredResults?.Type);
        Assert.All(response.StructuredResults!.Items, item => Assert.Contains("Starbucks", item.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, pool.CallCount);
        Assert.Contains(telemetry.Events, item => item.Name == "places.v2.used");
    }

    [Fact]
    public async Task FineDining_RemovesFastFoodAndDoesNotPadRejectedCandidates()
    {
        var orchestrator = CreateOrchestrator(
            Action(
                CompanionActionKind.NewPlaceSearch,
                placeQuery: "fine dining restaurants",
                locationQuery: "near me",
                excludeConcepts: ["fast_food", "takeaway"],
                preferences: ["upscale"]),
            new FixedPoolService(
                [
                    Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 300, "Fast food restaurant"),
                    Candidate("takeaway", "City Takeaway", "meal_takeaway", ["meal_takeaway"], 200, "Meal takeaway"),
                    Candidate("chapter", "Chapter One", "restaurant", ["restaurant"], 5_000, "Fine dining restaurant", priceLevel: "PRICE_LEVEL_EXPENSIVE"),
                    Candidate("patrick", "Restaurant Patrick Guilbaud", "restaurant", ["restaurant"], 6_000, "Fine dining restaurant", priceLevel: "PRICE_LEVEL_EXPENSIVE")
                ]));

        var response = await orchestrator.ExecuteAsync(Request("fine dining restaurants near me"), CancellationToken.None);

        var names = response.StructuredResults?.Items.Select(item => item.Name).ToArray() ?? [];
        Assert.Equal(["Chapter One", "Restaurant Patrick Guilbaud"], names);
    }

    [Fact]
    public async Task NotFastFoodFollowUp_UsesHiddenSessionPool()
    {
        var hiddenPool = new[]
        {
            Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 300, "Fast food restaurant"),
            Candidate("chapter", "Chapter One", "restaurant", ["restaurant"], 5_000, "Fine dining restaurant", priceLevel: "PRICE_LEVEL_EXPENSIVE")
        };
        var pool = new FixedPoolService([]);
        var session = new FixedSessionMemoryService(Context(hiddenPool));
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.FilterPreviousResults, excludeConcepts: ["fast_food", "takeaway"]),
            pool,
            session);

        var response = await orchestrator.ExecuteAsync(Request("not fast food"), CancellationToken.None);

        Assert.Equal(0, pool.CallCount);
        var card = Assert.Single(response.StructuredResults?.Items ?? []);
        Assert.Equal("Chapter One", card.Name);
    }

    [Fact]
    public async Task ParkingFollowUp_UsesHiddenPoolAndReturnsOnlyParkingMatches()
    {
        var session = new FixedSessionMemoryService(Context(
            [
                Candidate("one", "Coffee One", "cafe", ["cafe"], 100, "Coffee shop"),
                Candidate("two", "Coffee Two", "cafe", ["cafe", "parking"], 200, "Coffee shop parking")
            ]));
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.FilterPreviousResults, requirement: "parking"),
            new FixedPoolService([]),
            session);

        var response = await orchestrator.ExecuteAsync(Request("which ones have parking?"), CancellationToken.None);

        var card = Assert.Single(response.StructuredResults?.Items ?? []);
        Assert.Equal("Coffee Two", card.Name);
        Assert.DoesNotContain("rating_signal", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score:", response.ReplyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClosestFollowUp_ReturnsSingleClosestCard()
    {
        var session = new FixedSessionMemoryService(Context(
            [
                Candidate("far", "Far Coffee", "cafe", ["cafe"], 900, "Cafe"),
                Candidate("near", "Near Coffee", "cafe", ["cafe"], 100, "Cafe")
            ]));
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.SortPreviousResults, sortGoal: "distance"),
            new FixedPoolService([]),
            session);

        var response = await orchestrator.ExecuteAsync(Request("just the closest please"), CancellationToken.None);

        var card = Assert.Single(response.StructuredResults?.Items ?? []);
        Assert.Equal("Near Coffee", card.Name);
        Assert.Equal("Here's the closest one I found:", response.ReplyText);
    }

    [Fact]
    public async Task RatingFollowUp_DoesNotPadBelowThresholdResults()
    {
        var session = new FixedSessionMemoryService(Context(
            [
                Candidate("low", "Low Rated", "restaurant", ["restaurant"], 100, "Restaurant", rating: 4.2),
                Candidate("high", "High Rated", "restaurant", ["restaurant"], 200, "Restaurant", rating: 4.8)
            ]));
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.FilterPreviousResults, requirement: "rating>=4.7"),
            new FixedPoolService([]),
            session);

        var response = await orchestrator.ExecuteAsync(Request("only 4.7 and up"), CancellationToken.None);

        var card = Assert.Single(response.StructuredResults?.Items ?? []);
        Assert.Equal("High Rated", card.Name);
    }

    [Fact]
    public async Task NoMatches_DoesNotReintroduceRejectedCandidates()
    {
        var orchestrator = CreateOrchestrator(
            Action(
                CompanionActionKind.NewPlaceSearch,
                placeQuery: "fine dining restaurants",
                locationQuery: "near me",
                requirement: "parking",
                excludeConcepts: ["fast_food", "takeaway"],
                timeFilters: ["open_now"]),
            new FixedPoolService(
                [
                    Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 100, "Fast food restaurant", openNow: false),
                    Candidate("closed", "Closed Fine Dining", "restaurant", ["restaurant"], 300, "Fine dining restaurant", openNow: false)
                ]));

        var response = await orchestrator.ExecuteAsync(Request("fine dining restaurants open now with parking near me"), CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Null(response.StructuredResults);
        Assert.Equal("I couldn’t find any strong matches for that exact requirement nearby.", response.ReplyText);
    }

    [Fact]
    public async Task AibBanksNearMe_RejectsAtms()
    {
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.NewPlaceSearch, placeQuery: "AIB banks", locationQuery: "near me"),
            new FixedPoolService(
                [
                    Candidate("aib-atm", "AIB ATM", "atm", ["atm"], 100, "ATM"),
                    Candidate("aib-branch", "AIB Bank Santry", "bank", ["bank"], 300, "Bank")
                ]));

        var response = await orchestrator.ExecuteAsync(Request("AIB banks near me"), CancellationToken.None);

        var card = Assert.Single(response.StructuredResults?.Items ?? []);
        Assert.Equal("AIB Bank Santry", card.Name);
    }

    [Fact]
    public async Task AibAtmsNearMe_AllowsAtms()
    {
        var orchestrator = CreateOrchestrator(
            Action(CompanionActionKind.NewPlaceSearch, placeQuery: "AIB ATMs", locationQuery: "near me"),
            new FixedPoolService(
                [
                    Candidate("aib-atm", "AIB ATM", "atm", ["atm"], 100, "ATM"),
                    Candidate("aib-branch", "AIB Bank Santry", "bank", ["bank"], 300, "Bank")
                ]));

        var response = await orchestrator.ExecuteAsync(Request("AIB ATMs near me"), CancellationToken.None);

        var card = Assert.Single(response.StructuredResults?.Items ?? []);
        Assert.Equal("AIB ATM", card.Name);
    }

    private static ConversationLayerOrchestrator CreateOrchestrator(
        CompanionResolvedAction action,
        FixedPoolService pool,
        FixedSessionMemoryService? session = null,
        RecordingTelemetry? telemetry = null)
    {
        telemetry ??= new RecordingTelemetry();
        var details = new Dictionary<string, PlaceDetailsResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in pool.Candidates.Concat(session?.Previous?.CandidatePool ?? []))
        {
            details[candidate.PlaceId] = Details(candidate);
        }

        return new ConversationLayerOrchestrator(
            contextService: new EmptyContextService(),
            behaviorEngine: new ThrowingBehaviorEngine(),
            modeRouter: new ThrowingModeRouter(),
            responseComposer: new ThrowingResponseComposer(),
            logger: NullLogger<ConversationLayerOrchestrator>.Instance,
            options: Options.Create(new AIIntegrationOptions
            {
                ChatTurns = new ChatTurnOptions
                {
                    MaxUserMessageChars = 4000,
                    MaxClientRequestIdLength = 128
                },
                Architecture = new ConversationArchitectureOptions
                {
                    EmitTelemetryEvents = true,
                    CompanionActionResolverEnabled = true,
                    PlacesIntelligenceV2Enabled = true,
                    ResolvedActionDirectExecutionEnabled = true
                }
            }),
            telemetry: telemetry,
            companionActionResolver: new FixedActionResolver(action),
            companionSemanticIntentService: new CompanionSemanticIntentService(telemetry),
            companionPlaceCandidatePoolService: pool,
            companionPlaceConstraintEngine: new CompanionPlaceConstraintEngine(telemetry),
            companionPlaceRankingServiceV2: new CompanionPlaceIntelligenceRankingService(),
            companionPlaceFinalistEnrichmentService: new CompanionPlaceFinalistEnrichmentService(
                new DictionaryPlaceDetailsService(details),
                photoService: null,
                new InMemoryPlacesShortLivedCache(),
                Options.Create(new GooglePlacesOptions()),
                telemetry),
            companionPlaceSessionMemoryService: session ?? new FixedSessionMemoryService(null),
            companionPlaceResultContextBinder: new CompanionPlaceResultContextBinder(telemetry),
            companionPlaceParkingEvidenceService: new CompanionPlaceParkingEvidenceService(new EmptyDiscoveryService(), telemetry),
            companionPlaceDuplicateClusterService: new CompanionPlaceDuplicateClusterService(telemetry),
            companionPlaceCategoryCompatibilityService: new CompanionPlaceCategoryCompatibilityService(telemetry),
            companionPlaceBrandIdentityService: new CompanionPlaceBrandIdentityService(telemetry));
    }

    private static UserChatRequest Request(string message)
    {
        return new UserChatRequest(
            UserMessage: message,
            RecentTurns: [],
            State: new ConversationStateSnapshot(
                ActiveTopic: null,
                UserIntent: null,
                Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Summaries: [],
                BudgetPreference: null,
                LocationPreference: null,
                MerchantInvestigationSubject: null,
                RecentConclusions: []),
            CorrelationId: $"corr-{Guid.NewGuid():N}",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CompanionLocationMetadataKeys.Latitude] = "53.3498",
                [CompanionLocationMetadataKeys.Longitude] = "-6.2603"
            },
            UserId: Guid.NewGuid(),
            ConversationThreadId: Guid.NewGuid());
    }

    private static CompanionResolvedAction Action(
        CompanionActionKind kind,
        string? placeQuery = null,
        string? locationQuery = null,
        string? requirement = null,
        string? sortGoal = null,
        IReadOnlyList<string>? excludeConcepts = null,
        IReadOnlyList<string>? preferences = null,
        IReadOnlyList<string>? timeFilters = null)
    {
        return new CompanionResolvedAction(
            kind,
            Reason: "test",
            RequiresToolExecution: kind is not CompanionActionKind.CloseConversation,
            RequiresClarification: false,
            ClarificationNeed: null,
            PlaceQuery: placeQuery,
            LocationQuery: locationQuery,
            Requirement: requirement,
            SortGoal: sortGoal,
            TargetResultSetId: "active_result_set",
            IncludeConcepts: [],
            ExcludeConcepts: excludeConcepts ?? [],
            Preferences: preferences ?? [],
            TimeFilters: timeFilters ?? [],
            Warnings: []);
    }

    private static CompanionPlacePoolCandidate Candidate(
        string id,
        string name,
        string? primaryType,
        IReadOnlyList<string> types,
        double? distanceMeters,
        string? primaryTypeDisplayName = null,
        double? rating = 4.8,
        string? priceLevel = null,
        bool? openNow = true)
    {
        return new CompanionPlacePoolCandidate(
            id,
            name,
            primaryType,
            primaryTypeDisplayName ?? primaryType,
            types,
            53.3,
            -6.2,
            distanceMeters,
            "Test Street",
            rating,
            100,
            priceLevel,
            openNow,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static CompanionPlaceSearchContext Context(IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        return new CompanionPlaceSearchContext(
            new CompanionSemanticIntent(
                "places",
                "new_place_search",
                "restaurants",
                null,
                new CompanionLocationIntent("near_me", null, 53.3498, -6.2603, false),
                new CompanionPlaceRoleIntent(null, [], [], [], [], "loose"),
                [],
                [],
                [],
                [],
                [],
                "intent_fit_then_distance",
                null,
                0.9,
                []),
            candidates,
            VisibleCards: null,
            ResultContext: null);
    }

    private static PlaceDetailsResult Details(CompanionPlacePoolCandidate candidate)
    {
        return new PlaceDetailsResult(
            candidate.PlaceId,
            candidate.DisplayName,
            candidate.ShortFormattedAddress,
            Website: null,
            candidate.PriceLevel,
            NationalPhoneNumber: null,
            GoogleMapsUri: null,
            BusinessStatus: "OPERATIONAL",
            Rating: candidate.Rating,
            UserRatingCount: candidate.UserRatingCount,
            PrimaryType: candidate.PrimaryType,
            PrimaryTypeDisplayName: candidate.PrimaryTypeDisplayName,
            Types: candidate.Types,
            OpeningHours: new PlaceOpeningHoursSummary(candidate.OpenNow, [], null),
            PaymentOptions: null,
            AccessibilityOptions: null,
            EditorialSummary: null,
            Location: new PlaceLocationSummary(candidate.Latitude ?? 0, candidate.Longitude ?? 0),
            Photos: null);
    }

    private sealed class FixedActionResolver(CompanionResolvedAction action) : ICompanionActionResolver
    {
        public CompanionResolvedAction Resolve(
            UserChatRequest request,
            ConversationStateSnapshot state,
            ResultContextReadResult resultContext,
            TurnInterpretationV2? interpretation,
            PlaceRetrievalPlanV1? retrievalPlan,
            ConversationIntelligenceResult? intelligence)
        {
            return action;
        }
    }

    private sealed class FixedPoolService(IReadOnlyList<CompanionPlacePoolCandidate> candidates) : ICompanionPlaceCandidatePoolService
    {
        public IReadOnlyList<CompanionPlacePoolCandidate> Candidates { get; } = candidates;
        public int CallCount { get; private set; }

        public Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
            CompanionSemanticIntent intent,
            UserChatRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CompanionPlaceCandidatePoolResult(Candidates, ["test"], [], false, null));
        }
    }

    private sealed class EmptyDiscoveryService : ICompanionPlaceDiscoveryService
    {
        public Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
            CompanionPlaceDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result(request.MaxCandidates ?? 0));
        }

        public Task<CompanionPlaceDiscoveryResult> DiscoverNearbyAsync(
            CompanionNearbyDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result(0));
        }

        private static CompanionPlaceDiscoveryResult Result(int requested)
        {
            return new CompanionPlaceDiscoveryResult(
                Succeeded: true,
                Candidates: [],
                Metadata: new PlaceSearchMetadata("test", false, requested, 0, "test", TimeSpan.Zero, false),
                Warnings: []);
        }
    }

    private sealed class FixedSessionMemoryService(CompanionPlaceSearchContext? previous) : ICompanionPlaceSessionMemoryService
    {
        public CompanionPlaceSearchContext? Previous { get; } = previous;

        public Task SaveSearchContextAsync(
            UserChatRequest request,
            ConversationStateSnapshot state,
            CompanionSemanticIntent intent,
            IReadOnlyList<CompanionPlacePoolCandidate> candidatePool,
            CompanionStructuredResults? visibleCards,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<CompanionPlaceSearchContext?> LoadActiveSearchContextAsync(
            UserChatRequest request,
            ResultContextSnapshot? activeResultContext,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Previous);
        }
    }

    private sealed class DictionaryPlaceDetailsService(IReadOnlyDictionary<string, PlaceDetailsResult> details) : IPlaceDetailsService
    {
        public Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken)
        {
            return Task.FromResult(details.TryGetValue(placeId, out var value)
                ? value
                : new PlaceDetailsResult(placeId, placeId, null, null, null));
        }
    }

    private sealed class InMemoryPlacesShortLivedCache : IPlacesShortLivedCache
    {
        private readonly Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct)
        {
            return Task.FromResult(values.TryGetValue($"{provider}:{placeId}:{fieldMaskHash}", out var value) ? (T)value : default);
        }

        public Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct)
        {
            if (payload is not null)
            {
                values[$"{provider}:{placeId}:{fieldMaskHash}"] = payload;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTelemetry : IChatTelemetry
    {
        public List<(string Name, IReadOnlyDictionary<string, object?> Properties)> Events { get; } = [];

        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken)
        {
            Events.Add((eventName, new Dictionary<string, object?>(properties)));
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyContextService : IConversationContextService
    {
        public ConversationContextBuildResult BuildContext(ConversationContextBuildRequest request)
        {
            return new ConversationContextBuildResult([], [], [], null, new Dictionary<string, string>(), []);
        }
    }

    private sealed class ThrowingBehaviorEngine : IConversationBehaviorEngine
    {
        public Task<ConversationBehaviorResult> EvaluateAsync(ConversationBehaviorRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Behavior engine should not run for v2 Places tests.");
        }
    }

    private sealed class ThrowingModeRouter : IModeRouter
    {
        public Task<ConversationModeExecutionResult> RouteAsync(ConversationModeRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Mode router should not run for v2 Places tests.");
        }
    }

    private sealed class ThrowingResponseComposer : IResponseComposer
    {
        public Task<ResponseCompositionResult> ComposeAsync(
            ResponseCompositionRequest request,
            string correlationId,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Response composer should not run for v2 Places tests.");
        }
    }
}
