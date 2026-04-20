using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class UserChatOrchestratorCompanionHandoffTests
{
    [Fact]
    public async Task ExecuteAsync_LocalPlacesWithGpsMetadata_UsesCompanionHandoff_WithoutGenericAI()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "1. Cafe One - open now\n2. Bistro Two - moderate pricing",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: ["IPlacesSearchService"],
                Warnings: ["places_response_built_from_grounded_candidates"],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "places_grounded_response",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Find coffee near me",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-1",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "3000"
                },
                ClientRequestId: "req-1",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("nearby options", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chat_path_companion_local_places", response.Warnings);
        Assert.Contains("real_world_route_authoritative", response.Warnings);
        Assert.DoesNotContain(response.Warnings, warning =>
            warning.Contains("intent_unsupported", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("fallback_provider_unavailable", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_LocalPlacesWithGpsStateConstraints_UsesCompanionHandoff()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "1. City Cinema - open now\n2. Riverside Cinema - moderate pricing",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: ["IPlacesSearchService"],
                Warnings: ["places_response_built_from_grounded_candidates"],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "places_grounded_response",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "can you please show me some cinemas near me?",
                RecentTurns: [],
                State: new ConversationStateSnapshot(
                    ActiveTopic: null,
                    UserIntent: null,
                    Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Source] = "gps",
                        [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                        [CompanionLocationMetadataKeys.RadiusMeters] = "1500"
                    },
                    Summaries: [],
                    BudgetPreference: null,
                    LocationPreference: "current_location",
                    MerchantInvestigationSubject: null,
                    RecentConclusions: []),
                CorrelationId: "corr-1b",
                Metadata: null,
                ClientRequestId: "req-1b",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("nearby options", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("real_world_grounding_coordinates_present", response.Warnings);
        Assert.DoesNotContain("nearby_location_missing", response.Warnings);
        Assert.DoesNotContain("fallback_provider_unavailable", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_LocalPlacesWithoutGrounding_ReturnsSafePrompt_WithoutProviderInvocation()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Any brunch places near me?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-2",
                Metadata: null,
                ClientRequestId: "req-2",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("location permission", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nearby_location_missing", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_LocalPlacesWithoutGroundingButPermissionContext_EmitsGroundingDiagnostics()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Any cinemas near me?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-2b",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.NearbyPrompt] = "true",
                    [CompanionLocationMetadataKeys.PermissionState] = "granted",
                    [CompanionLocationMetadataKeys.RefreshAttempted] = "true",
                    [CompanionLocationMetadataKeys.RefreshOutcome] = "failed"
                },
                ClientRequestId: "req-2b",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("location permission", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("real_world_grounding_missing_despite_permission_context", response.Warnings);
        Assert.Contains("real_world_grounding_send_without_coordinates_after_refresh_attempt", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_NonPlacesPrompt_UsesGenericAIPath()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "How is my monthly budget doing?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-3",
                Metadata: null,
                ClientRequestId: "req-3",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("generic assistant reply", response.ReplyText);
        Assert.Equal(1, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_LocalityPromptWithoutGps_UsesCompanionHandoff()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "1. National Museum - open now",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: ["IPlacesSearchService"],
                Warnings: ["places_response_built_from_grounded_candidates"],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "places_grounded_response",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Museums around Dublin",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-4",
                Metadata: null,
                ClientRequestId: "req-4",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("nearby options", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nearby_grounding_source:query_locality", response.Warnings);
        Assert.DoesNotContain("fallback_provider_unavailable", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_BroadDiscoveryWithoutGrounding_ReturnsSafePrompt()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Where can I go with family this weekend?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-5",
                Metadata: null,
                ClientRequestId: "req-5",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("typed area", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nearby_location_missing", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FinancePlanningWithProductMention_DoesNotTriggerPlacesHandoff()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "How can I save for an Xbox in 2 months?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-6",
                Metadata: null,
                ClientRequestId: "req-6",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("generic assistant reply", response.ReplyText);
        Assert.Equal(1, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FinancePlanningWithAttachedGpsMetadata_RemainsFinancialPath()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "What can I cut to afford a PS5?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-6b",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "1500"
                },
                ClientRequestId: "req-6b",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("generic assistant reply", response.ReplyText);
        Assert.Equal(1, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExploratoryWithTypedArea_UsesDirectGroupedPlacesExecution()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var places = new TrackingPlacesSearchService();
        var sut = CreateSut(companion, aiClient, places);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "What can I do later tonight?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-7",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "typed_area",
                    [CompanionLocationMetadataKeys.TypedArea] = "Dublin city centre"
                },
                ClientRequestId: "req-7",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("nearby options across different categories", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.True(places.CallCount >= 2);
        Assert.Equal(0, companion.CallCount);
        Assert.Equal(0, aiClient.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DirectPlacesInvalidRequest_UsesProviderRequestFailureClassification()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient, new FailingPlacesSearchService("INVALID_ARGUMENT"));

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "coffee shops near me",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-8",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "1500"
                },
                ClientRequestId: "req-8",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("fallback_provider_request_failure", response.Warnings);
        Assert.Contains("real_world_failure_scenario:providerrequestfailure", response.Warnings);
        Assert.DoesNotContain("fallback_provider_unavailable", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitAreaOverridesDeviceLocation_ForSearchScope()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var places = new FixedResultPlacesSearchService(BuildMovieItems(4));
        var sut = CreateSut(companion, aiClient, places);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Where can I go drinking in Dublin 2?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-7b",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "1800"
                },
                ClientRequestId: "req-7b",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains(
            "real_world_search_scope:explicit_area_overrode_device_location",
            response.Warnings);
        Assert.Equal("explicit_area", places.LastLocationContext?.SearchScope);
        Assert.Null(places.LastLocationContext?.Latitude);
        Assert.Null(places.LastLocationContext?.Longitude);
        Assert.Equal("dublin 2", places.LastLocationContext?.TypedArea);
        Assert.True(places.LastLocationContext?.DeviceLatitude.HasValue);
        Assert.True(places.LastLocationContext?.DeviceLongitude.HasValue);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FocusedMovieTheaterRequest_SurfacesFullBoundedShortlist()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var places = new FixedResultPlacesSearchService(BuildMovieItems(8));
        var sut = CreateSut(companion, aiClient, places);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "cinemas near me",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-9",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "1500"
                },
                ClientRequestId: "req-9",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("1. Cinema 1", response.ReplyText, StringComparison.Ordinal);
        Assert.Contains("8. Cinema 8", response.ReplyText, StringComparison.Ordinal);
        Assert.Contains("real_world_places_results_returned:movietheater:8", response.Warnings);
        Assert.Contains("real_world_places_results_surfaced:movietheater:8", response.Warnings);
        Assert.Equal("movie theaters", places.LastQuery);
        Assert.True(places.LastLocationContext?.PlannerAuthoritative);
        Assert.Equal(RealWorldDiscoveryDomain.MovieTheater, places.LastLocationContext?.PlannerSelectedDomain);
        Assert.True(places.LastLocationContext?.HasNearMeSemantic);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FocusedMovieTheaterRequest_TrimsNonDomainResults_WithDiagnostic()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var mixedItems = BuildMovieItems(5)
            .Concat(
            [
                BuildPlaceItem("coffee-1", "Cafe 1", "Cafe", "cafe"),
                BuildPlaceItem("coffee-2", "Cafe 2", "Cafe", "cafe"),
                BuildPlaceItem("store-1", "Store 1", "Store", "store")
            ])
            .ToArray();
        var places = new FixedResultPlacesSearchService(mixedItems);
        var sut = CreateSut(companion, aiClient, places);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "cinemas near me",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-10",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "1500"
                },
                ClientRequestId: "req-10",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.DoesNotContain("Cafe 1", response.ReplyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Store 1", response.ReplyText, StringComparison.Ordinal);
        Assert.Contains("real_world_places_domain_filter_applied:movietheater", response.Warnings);
        Assert.Contains("real_world_places_surface_quality_trim_applied", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    private static UserChatOrchestrator CreateSut(
        TrackingCompanionService companion,
        TrackingAIClient aiClient,
        IPlacesSearchService? placesSearchService = null)
    {
        placesSearchService ??= new TrackingPlacesSearchService();
        var interpreter = new RealWorldIntentInterpreter(
            new FixedModelRouter(),
            new NoopInterpreterAIClient(),
            new RealWorldIntentInterpreterPromptBuilder(),
            NullLogger<RealWorldIntentInterpreter>.Instance);
        var handoff = new UserChatCompanionHandoffService(
            new LocalDiscoveryConstraintExtractor(),
            new RealWorldConversationSearchContextService(),
            new RealWorldSearchScopeResolver(),
            interpreter,
            new RealWorldExecutionModePlanner(new ExploratoryDomainSelectionPolicy()),
            new RealWorldPlacesExecutionService(
                placesSearchService,
                NullLogger<RealWorldPlacesExecutionService>.Instance),
            new RealWorldFailureMessageBuilder(),
            companion,
            NullLogger<UserChatCompanionHandoffService>.Instance);
        return new UserChatOrchestrator(
            complexityClassifier: new FixedComplexityClassifier(),
            contextService: new PassThroughContextService(),
            modelRouter: new FixedModelRouter(),
            promptBuilder: new FixedPromptBuilder(),
            responseParser: new FixedResponseParser(),
            aiClient: aiClient,
            logger: NullLogger<UserChatOrchestrator>.Instance,
            options: Options.Create(CreateOptions()),
            companionHandoffService: handoff);
    }

    private static AIIntegrationOptions CreateOptions()
    {
        return new AIIntegrationOptions
        {
            Routing = new AIModelRoutingOptions
            {
                FastModelName = "fast-model",
                FastDeploymentName = "fast-deployment",
                HeavyModelName = "heavy-model",
                HeavyDeploymentName = "heavy-deployment",
                HeavyModelEnabled = true
            }
        };
    }

    private sealed class FixedComplexityClassifier : IUserChatComplexityClassifier
    {
        public UserChatComplexityEvaluation Evaluate(UserChatRequest request)
        {
            return new UserChatComplexityEvaluation(
                Complexity: UserChatComplexity.Simple,
                ReasonCodes: ["test_simple"],
                ConstraintCount: 0,
                FinancialReasoningIntent: false,
                RankingIntent: false,
                MultiStepLanguageDetected: false);
        }
    }

    private sealed class PassThroughContextService : IConversationContextService
    {
        public ConversationContextBuildResult BuildContext(ConversationContextBuildRequest request)
        {
            return new ConversationContextBuildResult(
                ContextMessages:
                [
                    AIMessage.User(request.CurrentUserMessage ?? string.Empty)
                ],
                IncludedTurns: [],
                ExcludedTurns: [],
                ContextSummary: "test_context",
                StructuredState: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReasonCodes: ["context_pass_through"]);
        }
    }

    private sealed class FixedModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(
            AITaskType taskType,
            AIModelClass preferredModelClass,
            string? complexityHint = null)
        {
            return new AIModelRoute(
                taskType,
                preferredModelClass == AIModelClass.Any ? AIModelClass.Fast : preferredModelClass,
                Model: "fast-model",
                Deployment: "fast-deployment",
                IsFallback: false,
                Reason: "test_route",
                Notes: []);
        }
    }

    private sealed class FixedPromptBuilder : IPromptBuilder
    {
        public PromptBuildResult BuildMerchantInvestigationPrompt(MerchantInvestigationPromptInput input)
        {
            throw new NotSupportedException();
        }

        public PromptBuildResult BuildUserChatPrompt(UserChatPromptInput input)
        {
            return new PromptBuildResult(
                SystemInstructions: "test",
                Messages: [AIMessage.User(input.ChatRequest.UserMessage)],
                StructuredSchemaName: "user_chat_response_v1",
                ReasonCodes: ["prompt_built"]);
        }
    }

    private sealed class FixedResponseParser : IUserChatResponseParser
    {
        public bool TryParse(
            AIResponse response,
            AIModelRoute route,
            out UserChatResponse parsedResponse,
            out IReadOnlyList<string> reasonCodes)
        {
            parsedResponse = new UserChatResponse(
                ReplyText: "generic assistant reply",
                ModelUsed: route.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: null,
                Warnings: [],
                FollowUpIntentHints: [],
                Succeeded: true,
                FailureReason: null);
            reasonCodes = ["structured_parse_success"];
            return true;
        }
    }

    private sealed class TrackingAIClient : IAIClient
    {
        public int CallCount { get; private set; }

        public Task<AIResponse> SendAsync(
            AIRequest request,
            AIModelRoute route,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;
            return Task.FromResult(
                new AIResponse(
                    Content: "{\"replyText\":\"generic assistant reply\"}",
                    StructuredPayloadJson: "{\"replyText\":\"generic assistant reply\"}",
                    FinishReason: "stop",
                    Provider: "mock",
                    Model: route.Model,
                    Deployment: route.Deployment,
                    InputTokenEstimate: 20,
                    OutputTokenEstimate: 20,
                    LatencyMs: 2,
                    WasMocked: true,
                    RawDiagnostics: null,
                    Succeeded: true,
                    FailureReason: null));
        }
    }

    private sealed class TrackingCompanionService(
        FinancialCompanionResponse response) : IFinancialCompanionService
    {
        public int CallCount { get; private set; }

        public Task<FinancialCompanionResponse> ExecuteAsync(
            FinancialCompanionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;
            return Task.FromResult(response);
        }
    }

    private sealed class NoopInterpreterAIClient : IAIClient
    {
        public Task<AIResponse> SendAsync(
            AIRequest request,
            AIModelRoute route,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                AIResponse.Failed(
                    provider: "mock",
                    model: route.Model,
                    deployment: route.Deployment,
                    failureReason: "noop_interpreter_ai_disabled",
                    wasMocked: true));
        }
    }

    private sealed class TrackingPlacesSearchService : IPlacesSearchService
    {
        public int CallCount { get; private set; }

        public Task<PlaceSearchResult> SearchAsync(
            string query,
            string country,
            PlaceSearchLocationContext? locationContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;

            var normalized = query.ToLowerInvariant();
            var item = normalized switch
            {
                var q when q.Contains("cafe", StringComparison.Ordinal)
                    || q.Contains("coffee", StringComparison.Ordinal)
                    => BuildItem("cafe-1", "Cafe One", "Cafe"),
                var q when q.Contains("pub", StringComparison.Ordinal)
                    => BuildItem("pub-1", "Riverside Pub", "Pub"),
                var q when q.Contains("movie", StringComparison.Ordinal)
                    || q.Contains("cinema", StringComparison.Ordinal)
                    => BuildItem("cinema-1", "City Cinema", "Cinema"),
                var q when q.Contains("museum", StringComparison.Ordinal)
                    => BuildItem("museum-1", "National Museum", "Museum"),
                var q when q.Contains("restaurant", StringComparison.Ordinal)
                    || q.Contains("food", StringComparison.Ordinal)
                    => BuildItem("restaurant-1", "Dockside Kitchen", "Restaurant"),
                _ => BuildItem("park-1", "Canal Walk Park", "Park")
            };

            return Task.FromResult(
                new PlaceSearchResult(
                [
                    item
                ]));
        }

        private static PlaceSearchItem BuildItem(string id, string name, string category)
        {
            return new PlaceSearchItem(
                PlaceId: id,
                Name: name,
                Category: category,
                PriceLevel: "PRICE_LEVEL_MODERATE",
                ShortFormattedAddress: "Dublin",
                Rating: 4.4,
                UserRatingCount: 100,
                OpeningHours: new PlaceOpeningHoursSummary(
                    OpenNow: true,
                    WeekdayDescriptions: [],
                    NextOpenTimeUtc: null));
        }
    }

    private sealed class FailingPlacesSearchService(string providerErrorCode) : IPlacesSearchService
    {
        public Task<PlaceSearchResult> SearchAsync(
            string query,
            string country,
            PlaceSearchLocationContext? locationContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new PlaceSearchResult(
                    Items: [],
                    Metadata: new PlaceSearchMetadata(
                        UseCase: "companion_discovery",
                        FromCache: false,
                        RequestedCandidateCount: 8,
                        ReturnedCandidateCount: 0,
                        FieldMaskVariant: "companion_discovery",
                        Elapsed: TimeSpan.FromMilliseconds(5),
                        TimedOut: false,
                        ProviderErrorCode: providerErrorCode),
                    Warnings: ["places_test_failure_injected"]));
        }
    }

    private sealed class FixedResultPlacesSearchService(
        IReadOnlyList<PlaceSearchItem> items) : IPlacesSearchService
    {
        public string? LastQuery { get; private set; }
        public PlaceSearchLocationContext? LastLocationContext { get; private set; }

        public Task<PlaceSearchResult> SearchAsync(
            string query,
            string country,
            PlaceSearchLocationContext? locationContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = query;
            LastLocationContext = locationContext;
            return Task.FromResult(
                new PlaceSearchResult(
                    Items: items,
                    Metadata: new PlaceSearchMetadata(
                        UseCase: "companion_discovery",
                        FromCache: false,
                        RequestedCandidateCount: 8,
                        ReturnedCandidateCount: items.Count,
                        FieldMaskVariant: "companion_discovery",
                        Elapsed: TimeSpan.FromMilliseconds(5),
                        TimedOut: false,
                        ProviderErrorCode: null),
                    Warnings: []));
        }
    }

    private static IReadOnlyList<PlaceSearchItem> BuildMovieItems(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => BuildPlaceItem($"cinema-{index}", $"Cinema {index}", "Cinema", "movie_theater"))
            .ToArray();
    }

    private static PlaceSearchItem BuildPlaceItem(
        string placeId,
        string name,
        string category,
        string primaryType)
    {
        return new PlaceSearchItem(
            PlaceId: placeId,
            Name: name,
            Category: category,
            PriceLevel: "PRICE_LEVEL_MODERATE",
            PrimaryType: primaryType,
            PrimaryTypeDisplayName: category,
            Types: [primaryType],
            ShortFormattedAddress: "Dublin");
    }
}
