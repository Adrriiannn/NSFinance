using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class ConversationArchitectureGuardTests
{
    [Fact]
    public void ConversationIntelligenceParser_ParsesFollowUpParkingRequirement()
    {
        var parser = new ConversationIntelligenceParser();
        var payload = """
            {
              "conversation_phase": "refinement",
              "user_emotional_state": "neutral",
              "user_intent_confidence": 0.93,
              "should_continue_task": true,
              "should_clarify": false,
              "should_execute_tool": true,
              "should_acknowledge_issue": false,
              "response_style": {
                "tone": "helpful",
                "verbosity": "medium",
                "avoid_repetition": true
              },
              "task_state": {
                "is_new_task": false,
                "is_follow_up": true,
                "is_refinement": true,
                "is_user_correction": false,
                "target_previous_results": true
              },
              "next_action": {
                "type": "filter_previous_results",
                "reason": "User asked for parking information about previous results.",
                "target": "active_result_set",
                "requirement": "parking"
              },
              "reason_codes": ["test_follow_up_parking"]
            }
            """;

        var parsed = parser.TryParse(
            BuildAIResponse(payload),
            BuildRoute(),
            out var intelligence,
            out var reasonCodes,
            out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.NotNull(intelligence);
        Assert.Equal("refinement", intelligence!.ConversationPhase);
        Assert.True(intelligence.TaskState.TargetPreviousResults);
        Assert.Equal("filter_previous_results", intelligence.NextAction.Type);
        Assert.Equal("parking", intelligence.NextAction.Requirement);
        Assert.Contains("conversation_intelligence_parse_success", reasonCodes);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_UsesConversationIntelligenceClosing_ToAvoidToolOrFollowUp()
    {
        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                ModeCandidate: ConversationMode.Exploration,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R3_StructuredIncomplete,
                    To: ConversationReadinessLevel.R4_ToolReady),
                Confidence: 0.92d,
                FollowUpBindingType: FollowUpBindingType.Refine,
                ClarificationQuestion: "Old scripted question should be ignored.",
                SuggestedOptions: ["Old option"],
                ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                ReasonCodes: ["stub_tool_ready"]),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: 0.91d,
                ToolPathEligible: true,
                PrimaryWhy: "Stub structured exploration.",
                MissingConstraints: [],
                ReasonCodes: ["stub_structured"]));

        var request = BuildBehaviorRequest(
            userMessage: "thanks") with
        {
            ConversationIntelligence = BuildConversationIntelligence(
                phase: "closing",
                emotionalState: "satisfied",
                nextAction: "answer_directly",
                shouldExecuteTool: false,
                shouldContinueTask: false)
        };

        var result = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.True(result.StayInDirectMode);
        Assert.False(result.RouteToModeHandler);
        Assert.Equal(ConversationBehaviorStrategy.DirectAnswer, result.StrategyDecision.Strategy);
        Assert.Equal(ToolExecutionPermission.Forbidden, result.StrategyDecision.ToolExecutionPermission);
        Assert.Equal(FollowUpBindingType.None, result.StrategyDecision.FollowUpBindingType);
        Assert.Null(result.StrategyDecision.ClarificationQuestion);
        Assert.Empty(result.StrategyDecision.SuggestedOptions);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_DowngradesStructuredNearMeWithoutLocationEvidence_ToR3AndForbidsTools()
    {
        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                ModeCandidate: ConversationMode.Exploration,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R1_Vague,
                    To: ConversationReadinessLevel.R4_ToolReady),
                Confidence: 0.92d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                ReasonCodes: ["stub_tool_ready"]),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: 0.91d,
                ToolPathEligible: true,
                PrimaryWhy: "Stub structured exploration.",
                MissingConstraints: [],
                ReasonCodes: ["stub_structured"]));

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest(
                userMessage: "restaurants near me",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        Assert.Equal(ConversationReadinessLevel.R3_StructuredIncomplete, result.StrategyDecision.Readiness.To);
        Assert.Equal(ToolExecutionPermission.Forbidden, result.StrategyDecision.ToolExecutionPermission);
        Assert.Equal(ConversationBehaviorStrategy.SuggestAndClarify, result.StrategyDecision.Strategy);
        Assert.Contains("behavior_guard_near_me_requires_location_evidence", result.StrategyDecision.ReasonCodes);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_PreventsFinancialActivation_FromVagueInput()
    {
        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.ConfirmAndTransition,
                ModeCandidate: ConversationMode.Financial,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R0_Unknown,
                    To: ConversationReadinessLevel.R1_Vague),
                Confidence: 0.78d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: "Should I review spending or subscriptions?",
                SuggestedOptions: ["Spending", "Subscriptions"],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes: ["stub_financial"]),
            null);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest(userMessage: "I feel stressed about my budget"),
            CancellationToken.None);

        Assert.Equal(ConversationMode.Conversation, result.StrategyDecision.ModeCandidate);
        Assert.Equal(ConversationBehaviorStrategy.AcknowledgeAndGuide, result.StrategyDecision.Strategy);
        Assert.False(result.RouteToModeHandler);
        Assert.True(result.StayInDirectMode);
        Assert.Contains("behavior_guard_financial_requires_guided_validation", result.StrategyDecision.ReasonCodes);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_LeavesGeneralKnowledgeAtR1_InDirectMode()
    {
        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.DirectAnswer,
                ModeCandidate: ConversationMode.GeneralKnowledge,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R0_Unknown,
                    To: ConversationReadinessLevel.R1_Vague),
                Confidence: 0.83d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes: ["stub_general_knowledge"]),
            null);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest(userMessage: "What is compound interest?"),
            CancellationToken.None);

        Assert.True(result.StayInDirectMode);
        Assert.False(result.RouteToModeHandler);
        Assert.NotNull(result.CompositionRequest);
        Assert.Equal(ConversationMode.GeneralKnowledge, result.CompositionRequest!.Mode);
    }

    [Fact]
    public async Task StructuredExplorationHandler_BlocksToolExecutionBelowR4_WithoutCallingPlacesService()
    {
        var placesSearch = new TrackingPlacesSearchService();
        var handler = new StructuredExplorationHandler(
            new StubConstraintExtractor(),
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "parks near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-structured",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ClientRequestId: "client-structured",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R2_DirectionKnown,
                        To: ConversationReadinessLevel.R3_StructuredIncomplete),
                    Confidence: 0.80d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: "Which area do you want to search in?",
                    SuggestedOptions: ["Add an area"],
                    ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                    ReasonCodes: ["stub_below_r4"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.82d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: ["area_or_location"],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, placesSearch.CallCount);
        Assert.NotNull(result.CompositionRequest);
        Assert.Equal(ResponseCompositionType.Clarify, result.CompositionRequest!.ResponseType);
        Assert.Contains("chat.tool.guard_blocked", result.Warnings);
    }

    [Fact]
    public async Task StructuredExplorationHandler_ExecutesPlaces_WhenStructuredRequestIsCompleteAndToolReady()
    {
        var placesSearch = new TrackingPlacesSearchService();
        var handler = new StructuredExplorationHandler(
            new StubConstraintExtractor(),
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "parks near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-structured-ready",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-structured-ready",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.93d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.90d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, placesSearch.CallCount);
    }

    [Fact]
    public async Task StructuredExplorationHandler_LocksShortlistCountAndCarriesStructuredConstraints()
    {
        var placesSearch = new FixedPlacesSearchService(
            Enumerable.Range(1, 10)
                .Select(index => new PlaceSearchItem(
                    PlaceId: $"place-{index}",
                    Name: $"Place {index}",
                    Category: "park",
                    PriceLevel: null,
                    PrimaryType: "park",
                    Types: ["park", "point_of_interest"],
                    ShortFormattedAddress: $"Address {index}",
                    Rating: 4.0d + (index / 100d),
                    OpeningHours: new PlaceOpeningHoursSummary(
                        OpenNow: true,
                        WeekdayDescriptions: [],
                        NextOpenTimeUtc: null)))
                .ToArray());
        var handler = new StructuredExplorationHandler(
            new StubConstraintExtractor(),
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());
        var state = CreateDefaultState() with
        {
            Constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ConversationConstraintKeys.ExplorationTime] = "open_now"
            }
        };

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "parks near me",
                    RecentTurns: [],
                    State: state,
                    CorrelationId: "corr-structured-shortlist-lock",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-structured-shortlist-lock",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: state,
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.95d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.91d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, placesSearch.CallCount);
        Assert.NotNull(result.CompositionRequest);
        Assert.Equal(ResponseCompositionType.ResultSummary, result.CompositionRequest!.ResponseType);
        Assert.Equal(8, result.CompositionRequest.GroundedData.Entities.Count);
        Assert.Equal(8, result.State.LastSuggestedEntities?.Count);
        Assert.Equal("park", result.State.Constraints[ConversationConstraintKeys.ExplorationPlaceTypes]);
        Assert.Equal("near_me", result.State.Constraints[ConversationConstraintKeys.ExplorationArea]);
        Assert.Equal("open_now", result.State.Constraints[ConversationConstraintKeys.ExplorationTime]);
    }

    [Fact]
    public async Task StructuredExplorationHandler_FiltersShortlistToRequestedPlaceType()
    {
        var placesSearch = new FixedPlacesSearchService(
        [
            new PlaceSearchItem(
                PlaceId: "cafe-1",
                Name: "Cafe One",
                Category: "Cafe",
                PriceLevel: null,
                PrimaryType: "cafe",
                Types: ["cafe", "food"],
                ShortFormattedAddress: "Address 1"),
            new PlaceSearchItem(
                PlaceId: "store-1",
                Name: "Electronics Shop",
                Category: "Electronics store",
                PriceLevel: null,
                PrimaryType: "electronics_store",
                Types: ["electronics_store", "store"],
                ShortFormattedAddress: "Address 2"),
            new PlaceSearchItem(
                PlaceId: "furniture-1",
                Name: "Furniture World",
                Category: "Furniture store",
                PriceLevel: null,
                PrimaryType: "furniture_store",
                Types: ["furniture_store", "store"],
                ShortFormattedAddress: "Address 3")
        ]);
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.95d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "coffee shops near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-structured-type-filter",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-structured-type-filter",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.93d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.91d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CompositionRequest);
        Assert.Single(result.CompositionRequest!.GroundedData.Entities);
        Assert.All(
            result.CompositionRequest.GroundedData.SummaryFacts,
            fact => Assert.Contains("cafe", fact.Value, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("structured_exploration_place_type_filter_applied", result.Warnings);
    }

    [Fact]
    public async Task StructuredExplorationHandler_DoesNotFallbackToMixedCategories_WhenRequestedTypeHasNoMatches()
    {
        var placesSearch = new FixedPlacesSearchService(
        [
            new PlaceSearchItem(
                PlaceId: "store-1",
                Name: "Electronics Shop",
                Category: "Electronics store",
                PriceLevel: null,
                PrimaryType: "electronics_store",
                Types: ["electronics_store", "store"],
                ShortFormattedAddress: "Address 2"),
            new PlaceSearchItem(
                PlaceId: "furniture-1",
                Name: "Furniture World",
                Category: "Furniture store",
                PriceLevel: null,
                PrimaryType: "furniture_store",
                Types: ["furniture_store", "store"],
                ShortFormattedAddress: "Address 3")
        ]);
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.95d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "coffee shops near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-structured-type-no-match",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-structured-type-no-match",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.93d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.91d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CompositionRequest);
        Assert.Equal(ResponseCompositionType.Fallback, result.CompositionRequest!.ResponseType);
        Assert.Contains("structured_exploration_no_results_for_requested_place_type", result.Warnings);
    }

    [Fact]
    public async Task StructuredExplorationHandler_PrioritizesPrimaryType_WhenRequestedTypeIsSpecific()
    {
        var placesSearch = new FixedPlacesSearchService(
        [
            new PlaceSearchItem(
                PlaceId: "venue-1",
                Name: "Axis Art Centre and Theatre",
                Category: null,
                PriceLevel: null,
                PrimaryType: "educational_institution",
                Types: ["educational_institution", "cafe"],
                ShortFormattedAddress: "Address 1",
                ServesCoffee: true),
            new PlaceSearchItem(
                PlaceId: "cafe-1",
                Name: "Sweet Paradise Cafe",
                Category: "Cafe",
                PriceLevel: null,
                PrimaryType: "cafe",
                Types: ["cafe", "food"],
                ShortFormattedAddress: "Address 2")
        ]);
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.95d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "coffee shops near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-primary-type-priority",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-primary-type-priority",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.93d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.91d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CompositionRequest);
        Assert.Single(result.CompositionRequest!.GroundedData.Entities);
        Assert.Equal("Sweet Paradise Cafe", result.CompositionRequest.GroundedData.Entities[0].Label);
        Assert.Contains("structured_exploration_place_type_filter_applied", result.Warnings);
    }

    [Fact]
    public async Task StructuredExplorationHandler_FormatsCategoryFromPlacesTypes_WhenCategoryMissing()
    {
        var placesSearch = new FixedPlacesSearchService(
        [
            new PlaceSearchItem(
                PlaceId: "venue-1",
                Name: "Axis Art Centre and Theatre",
                Category: null,
                PriceLevel: null,
                PrimaryType: null,
                Types: ["performing_arts_theater", "point_of_interest"],
                ShortFormattedAddress: "Address 1")
        ]);
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.95d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["performing_arts_theater"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "theatres near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-category-fallback",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-category-fallback",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.93d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.91d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CompositionRequest);
        Assert.Single(result.CompositionRequest!.GroundedData.SummaryFacts);
        Assert.Contains("Performing Arts Theater", result.CompositionRequest.GroundedData.SummaryFacts[0].Value);
    }

    [Fact]
    public async Task StructuredExplorationHandler_RejectsSecondaryCoffeeTags_WhenCanonicalTypeIsDifferent()
    {
        var placesSearch = new FixedPlacesSearchService(
        [
            new PlaceSearchItem(
                PlaceId: "axis-1",
                Name: "Axis Art Centre and Theatre",
                Category: null,
                PriceLevel: null,
                PrimaryType: null,
                Types: ["dance_hall", "cafe"],
                ShortFormattedAddress: "Address 1",
                ServesCoffee: true),
            new PlaceSearchItem(
                PlaceId: "cafe-1",
                Name: "Sweet Paradise Cafe",
                Category: "Cafe",
                PriceLevel: null,
                PrimaryType: "cafe",
                Types: ["cafe", "food"],
                ShortFormattedAddress: "Address 2")
        ]);
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.95d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "coffee shops near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-secondary-tag-reject",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                        [CompanionLocationMetadataKeys.Source] = "gps"
                    },
                    ClientRequestId: "client-secondary-tag-reject",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R3_StructuredIncomplete,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.93d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.91d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097",
                    [CompanionLocationMetadataKeys.Source] = "gps"
                }),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CompositionRequest);
        Assert.Single(result.CompositionRequest!.GroundedData.Entities);
        Assert.Equal("Sweet Paradise Cafe", result.CompositionRequest.GroundedData.Entities[0].Label);
    }

    [Fact]
    public async Task StructuredExplorationHandler_ClarifiesLocationOnly_WhenPlaceTypeKnownButLocationMissing()
    {
        var placesSearch = new TrackingPlacesSearchService();
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.91d,
                HasNearMeLanguage: false,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: ["open_now"],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "coffee shops open now",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-location-only",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.PermissionState] = "unknown"
                    },
                    ClientRequestId: "client-location-only",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R2_DirectionKnown,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.89d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.89d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: ["area_or_location"],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.PermissionState] = "unknown"
                }),
            CancellationToken.None);

        Assert.Equal(0, placesSearch.CallCount);
        Assert.NotNull(result.CompositionRequest);
        Assert.Null(result.CompositionRequest!.ClarificationQuestion);
        Assert.Empty(result.CompositionRequest.SuggestedOptions!);
        Assert.Equal(ClarificationSlot.ExplorationLocation, result.State.PendingClarification?.Slot);
    }

    [Fact]
    public async Task StructuredExplorationHandler_ClarifiesPlaceTypeOnly_WhenLocationAlreadyKnown()
    {
        var placesSearch = new TrackingPlacesSearchService();
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.82d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: [],
                AudienceHints: [],
                TimeHints: ["open_now"],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "what's open near me",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-place-type-only",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.Source] = "gps",
                        [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                        [CompanionLocationMetadataKeys.Longitude] = "-6.2603097"
                    },
                    ClientRequestId: "client-place-type-only",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R2_DirectionKnown,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.88d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.85d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: ["place_type"],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.3498053",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.2603097"
                }),
            CancellationToken.None);

        Assert.Equal(0, placesSearch.CallCount);
        Assert.NotNull(result.CompositionRequest);
        Assert.Null(result.CompositionRequest!.ClarificationQuestion);
        Assert.Empty(result.CompositionRequest.SuggestedOptions!);
        Assert.Equal(ClarificationSlot.ExplorationPlaceType, result.State.PendingClarification?.Slot);
    }

    [Fact]
    public async Task StructuredExplorationHandler_UsesDeniedPermissionPrompt_WhenLocationPermissionIsBlocked()
    {
        var placesSearch = new TrackingPlacesSearchService();
        var extractor = new FixedConstraintExtractor(
            new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.91d,
                HasNearMeLanguage: false,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: ["open_now"],
                PreferenceHints: [],
                ReasonCodes: ["fixed_extractor"]));
        var handler = new StructuredExplorationHandler(
            extractor,
            new StubQueryShaper(),
            placesSearch,
            new StubResultContextService());

        var result = await handler.ExecuteAsync(
            new ConversationModeRequest(
                Request: new UserChatRequest(
                    UserMessage: "coffee shops open now",
                    RecentTurns: [],
                    State: CreateDefaultState(),
                    CorrelationId: "corr-location-denied",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CompanionLocationMetadataKeys.PermissionState] = "denied_open_settings"
                    },
                    ClientRequestId: "client-location-denied",
                    UserId: null,
                    ConversationThreadId: null,
                    UsePersistentMemory: false,
                    AllowTransientFallbackOnPersistentFailure: false),
                ContextMessages: [],
                ContextSummary: null,
                State: CreateDefaultState(),
                ResultContext: null,
                StrategyDecision: new ConversationTurnStrategyDecision(
                    Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                    ModeCandidate: ConversationMode.Exploration,
                    Readiness: new ReadinessTransition(
                        From: ConversationReadinessLevel.R2_DirectionKnown,
                        To: ConversationReadinessLevel.R4_ToolReady),
                    Confidence: 0.89d,
                    FollowUpBindingType: FollowUpBindingType.None,
                    ClarificationQuestion: null,
                    SuggestedOptions: [],
                    ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                    ReasonCodes: ["stub_tool_ready"]),
                ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.89d,
                    ToolPathEligible: true,
                    PrimaryWhy: "Stub structured exploration.",
                    MissingConstraints: ["area_or_location"],
                    ReasonCodes: ["stub_structured"]),
                ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.PermissionState] = "denied_open_settings"
                }),
            CancellationToken.None);

        Assert.Equal(0, placesSearch.CallCount);
        Assert.Null(result.CompositionRequest!.ClarificationQuestion);
        Assert.Empty(result.CompositionRequest.SuggestedOptions!);
        Assert.Equal("location_permission_denied_or_unavailable", result.FollowUpIntentHints.Last());
    }

    [Fact]
    public async Task ConversationLayerOrchestrator_ExecutesBehaviorEngineBeforeModeRouter()
    {
        var order = new List<string>();
        var orchestrator = new ConversationLayerOrchestrator(
            contextService: new StubConversationContextService(),
            behaviorEngine: new StubBehaviorEngine(order),
            modeRouter: new TrackingModeRouter(order),
            responseComposer: new StubResponseComposer(order),
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
                    EmitTelemetryEvents = false
                }
            }),
            telemetry: new NoOpChatTelemetry());

        var response = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "quiet place for a walk",
                RecentTurns: [],
                State: CreateDefaultState(),
                CorrelationId: "corr-orchestrator",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ClientRequestId: "client-orchestrator",
                UserId: null,
                ConversationThreadId: null,
                UsePersistentMemory: false,
                AllowTransientFallbackOnPersistentFailure: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal(["behavior", "mode", "compose"], order);
    }

    [Fact]
    public async Task ConversationLayerOrchestrator_EmitsPerTurnModelUsageSummary()
    {
        var order = new List<string>();
        var telemetry = new RecordingChatTelemetry();
        var orchestrator = new ConversationLayerOrchestrator(
            contextService: new StubConversationContextService(),
            behaviorEngine: new StubBehaviorEngine(order),
            modeRouter: new TrackingModeRouter(order),
            responseComposer: new StubResponseComposer(order),
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
                    EmitTelemetryEvents = true
                }
            }),
            telemetry: telemetry);

        var response = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "quiet place for a walk",
                RecentTurns: [],
                State: CreateDefaultState(),
                CorrelationId: "corr-model-summary",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ClientRequestId: "client-model-summary",
                UserId: null,
                ConversationThreadId: null,
                UsePersistentMemory: false,
                AllowTransientFallbackOnPersistentFailure: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);

        var summary = telemetry.Single("chat.turn.model_usage_summary");
        Assert.Equal(2, summary["totalModelCallCount"]);
        Assert.Equal(0, summary["heavyModelCallCount"]);
        Assert.Equal(2, summary["fastModelCallCount"]);
        Assert.Equal(true, summary["usedDeterministicPath"]);

        var latency = telemetry.Single("chat.turn.latency_budget");
        Assert.True(Convert.ToInt64(latency["setupDurationMs"]!) >= 0);
        Assert.True(Convert.ToInt64(latency["behaviorDurationMs"]!) >= 0);
        Assert.True(Convert.ToInt64(latency["responseCompositionDurationMs"]!) >= 0);
        Assert.True(Convert.ToInt64(latency["totalDurationMs"]!) >= 0);
    }

    [Fact]
    public async Task ConversationLayerOrchestrator_CountsResponseModelInvocation_WhenDeterministicRecoveryProducesFinalReply()
    {
        var order = new List<string>();
        var telemetry = new RecordingChatTelemetry();
        var orchestrator = new ConversationLayerOrchestrator(
            contextService: new StubConversationContextService(),
            behaviorEngine: new StubBehaviorEngine(order),
            modeRouter: new TrackingModeRouter(order),
            responseComposer: new RecoveringResponseComposer(order),
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
                    EmitTelemetryEvents = true
                }
            }),
            telemetry: telemetry);

        var response = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "quiet place for a walk",
                RecentTurns: [],
                State: CreateDefaultState(),
                CorrelationId: "corr-recovery-summary",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ClientRequestId: "client-recovery-summary",
                UserId: null,
                ConversationThreadId: null,
                UsePersistentMemory: false,
                AllowTransientFallbackOnPersistentFailure: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);

        var summary = telemetry.Single("chat.turn.model_usage_summary");
        Assert.Equal(2, summary["totalModelCallCount"]);
        Assert.Equal(2, summary["fastModelCallCount"]);
        Assert.Equal(true, summary["usedDeterministicPath"]);
        Assert.Equal(true, summary["usedDeterministicRecovery"]);
        Assert.Equal(true, summary["responseUsedModelInvocation"]);
        Assert.Equal(true, summary["responseFallbackUsed"]);
        Assert.Equal("structured_parse_failed", summary["responseRecoveryReason"]);
        Assert.Equal("response_composition_safe_fallback", summary["responseSelectionReason"]);
    }

    [Fact]
    public async Task ConversationLayerOrchestrator_ReturnsGroundedShortlist_WhenStructuredResultSummaryRecoveryRuns()
    {
        var order = new List<string>();
        var telemetry = new RecordingChatTelemetry();
        var orchestrator = new ConversationLayerOrchestrator(
            contextService: new StubConversationContextService(),
            behaviorEngine: new StubBehaviorEngine(order),
            modeRouter: new StructuredResultSummaryModeRouter(order),
            responseComposer: new ResponseComposer(
                new ResponseCompositionPromptBuilder(),
                new UserChatResponseParser(),
                new StaticResponseCompositionModelRouter(),
                new InvalidStructuredResponseAIClient(),
                telemetry,
                NullLogger<ResponseComposer>.Instance),
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
                    EmitTelemetryEvents = true
                }
            }),
            telemetry: telemetry);

        var response = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "coffee shops near me",
                RecentTurns: [],
                State: CreateDefaultState(),
                CorrelationId: "corr-structured-recovery",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ClientRequestId: "client-structured-recovery",
                UserId: null,
                ConversationThreadId: null,
                UsePersistentMemory: false,
                AllowTransientFallbackOnPersistentFailure: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("I found these matching options:", response.ReplyText);
        Assert.Contains("1. Bean Room", response.ReplyText);
        Assert.Contains("Details: cafe, rating 4.6, open now", response.ReplyText);
        Assert.Contains("2. Roast House", response.ReplyText);
        Assert.Contains("Details: cafe, rating 4.4, Dublin 2", response.ReplyText);
        Assert.Contains("structured_parse_failed", response.Warnings);
        Assert.Contains("response_composition_safe_fallback", response.Warnings);

        var summary = telemetry.Single("chat.turn.model_usage_summary");
        Assert.Equal(2, summary["totalModelCallCount"]);
        Assert.Equal(2, summary["fastModelCallCount"]);
        Assert.Equal(true, summary["responseUsedModelInvocation"]);
        Assert.Equal(true, summary["responseFallbackUsed"]);
    }

    [Fact]
    public async Task ResponseComposer_UsesDeterministicFallback_WhenModelInvocationTransportCancels()
    {
        var composer = new ResponseComposer(
            new ResponseCompositionPromptBuilder(),
            new UserChatResponseParser(),
            new StaticResponseCompositionModelRouter(),
            new TransportCancelledResponseCompositionAIClient(),
            new NoOpChatTelemetry(),
            NullLogger<ResponseComposer>.Instance);

        var result = await composer.ComposeAsync(
            new ResponseCompositionRequest(
                ResponseType: ResponseCompositionType.ResultSummary,
                ToneDirective: ResponseToneDirective.Neutral,
                Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                Mode: ConversationMode.Exploration,
                ReadinessLevel: ConversationReadinessLevel.R4_ToolReady,
                UserMessage: "coffee shops near me",
                GroundedData: new GroundedDataEnvelope(
                    Entities:
                    [
                        new ConversationSuggestedEntity("cafe-1", "Bean Room", 1)
                    ],
                    SummaryFacts:
                    [
                        new GroundedDataPoint("Bean Room", "cafe, rating 4.6, open now")
                    ],
                    Warnings: []),
                Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                MissingConstraints: [],
                MaxLengthHint: 450,
                ClarificationQuestion: null,
                SuggestedOptions: ["Refine the shortlist", "Compare options"]),
            correlationId: "corr-compose-cancelled",
            cancellationToken: CancellationToken.None);

        Assert.True(result.UsedDeterministicPath);
        Assert.True(result.FallbackUsed);
        Assert.True(result.UsedModelInvocation);
        Assert.Equal("response_composition_safe_fallback", result.SelectionReason);
        Assert.Equal("response_composition_transport_cancelled", result.RecoveryReason);
        Assert.Contains("response_composition_transport_cancelled", result.Warnings);
        Assert.Contains("response_composition_safe_fallback", result.Warnings);
        Assert.Contains("I found one matching option:", result.ReplyText);
    }

    [Fact]
    public void FollowUpBindingPolicy_DoesNotCarryStaleBindingWithoutEvidence()
    {
        var policy = new FollowUpBindingPolicy();
        var state = CreateDefaultState() with
        {
            FollowUpBindingType = FollowUpBindingType.BindPrior,
            ResultContextRef = new ConversationResultContextReference(
                ActiveResultSetId: Guid.NewGuid(),
                BranchRootResultSetId: Guid.NewGuid(),
                ActiveUntilUtc: DateTime.UtcNow.AddMinutes(10),
                ExpiresUtc: DateTime.UtcNow.AddHours(1))
        };

        var result = policy.Determine(
            new UserChatRequest(
                UserMessage: "thanks",
                RecentTurns: [],
                State: state,
                CorrelationId: "corr-binding",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ClientRequestId: "client-binding",
                UserId: null,
                ConversationThreadId: null,
                UsePersistentMemory: false,
                AllowTransientFallbackOnPersistentFailure: false),
            state,
            new ResultContextReadResult(
                ActiveResultContext: null,
                BindingClassification: ResultContextBindingClassification.None,
                UsedClientResultSetId: false,
                ExpiredBindingCleared: false,
                ReasonCodes: []));

        Assert.Equal(FollowUpBindingType.None, result.BindingType);
    }

    private static ConversationBehaviorRequest BuildBehaviorRequest(
        string userMessage,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var request = new UserChatRequest(
            UserMessage: userMessage,
            RecentTurns: [],
            State: CreateDefaultState(),
            CorrelationId: Guid.NewGuid().ToString("N"),
            Metadata: metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ClientRequestId: Guid.NewGuid().ToString("N"),
            UserId: null,
            ConversationThreadId: null,
            UsePersistentMemory: false,
            AllowTransientFallbackOnPersistentFailure: false);

        return new ConversationBehaviorRequest(
            Request: request,
            ContextMessages: [],
            ContextSummary: null,
            EffectiveState: CreateDefaultState(),
            ResultContext: null,
            ResultContextReadResult: new ResultContextReadResult(
                ActiveResultContext: null,
                BindingClassification: ResultContextBindingClassification.None,
                UsedClientResultSetId: false,
                ExpiredBindingCleared: false,
                ReasonCodes: []),
            ClientMetadata: metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            FailureHistory: [],
            CancellationToken: CancellationToken.None);
    }

    private static ConversationBehaviorEngine CreateBehaviorEngine(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision)
    {
        return new ConversationBehaviorEngine(
            new StubDecisionEngine(decision, explorationSubtypeDecision),
            new ConversationModelRoutingPolicy(),
            new ReadinessTransitionPolicy(),
            new FollowUpBindingPolicy(),
            new ContradictionResolutionPolicy(),
            new FinancialActivationPolicy(),
            new ExplorationSubtypeDecisionPolicy(),
            new ToolGuardWarningPolicy(),
            new NoOpChatTelemetry());
    }

    private static ConversationStateSnapshot CreateDefaultState()
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

    private static ConversationIntelligenceResult BuildConversationIntelligence(
        string phase,
        string emotionalState,
        string nextAction,
        bool shouldExecuteTool,
        bool shouldContinueTask)
    {
        return new ConversationIntelligenceResult(
            ConversationPhase: phase,
            UserEmotionalState: emotionalState,
            UserIntentConfidence: 0.9d,
            ShouldContinueTask: shouldContinueTask,
            ShouldClarify: false,
            ShouldExecuteTool: shouldExecuteTool,
            ShouldAcknowledgeIssue: false,
            ResponseStyle: new ConversationResponseStyle(
                Tone: "warm",
                Verbosity: "short",
                AvoidRepetition: true),
            TaskState: new ConversationTaskState(
                IsNewTask: false,
                IsFollowUp: shouldContinueTask,
                IsRefinement: shouldContinueTask,
                IsUserCorrection: false,
                TargetPreviousResults: shouldContinueTask),
            NextAction: new ConversationNextAction(
                Type: nextAction,
                Reason: "Test intelligence decision."),
            ReasonCodes: ["test_conversation_intelligence"]);
    }

    private static AIResponse BuildAIResponse(string payload)
    {
        return new AIResponse(
            Content: payload,
            StructuredPayloadJson: payload,
            FinishReason: "stop",
            Provider: "test",
            Model: "test-model",
            Deployment: "test-deployment",
            InputTokenEstimate: null,
            OutputTokenEstimate: null,
            LatencyMs: 1,
            WasMocked: true,
            RawDiagnostics: null,
            Succeeded: true,
            FailureReason: null);
    }

    private static AIModelRoute BuildRoute()
    {
        return new AIModelRoute(
            TaskType: AITaskType.ConversationDecision,
            ModelClass: AIModelClass.Fast,
            Model: "test-model",
            Deployment: "test-deployment",
            IsFallback: false,
            Reason: "test",
            Notes: []);
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

    private sealed class RecordingChatTelemetry : IChatTelemetry
    {
        private readonly List<(string Name, IReadOnlyDictionary<string, object?> Properties)> events = [];

        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken)
        {
            events.Add((eventName, new Dictionary<string, object?>(properties)));
            return Task.CompletedTask;
        }

        public IReadOnlyDictionary<string, object?> Single(string eventName)
        {
            return events.Single(evt => string.Equals(evt.Name, eventName, StringComparison.Ordinal)).Properties;
        }
    }

    private sealed class StubDecisionEngine(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision) : IConversationDecisionEngine
    {
        public Task<ConversationDecisionEvaluationResult> EvaluateAsync(
            ConversationBehaviorRequest request,
            ConversationModelSelectionPlan modelSelection,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ConversationDecisionEvaluationResult(
                    Decision: decision,
                    ModelSelection: modelSelection,
                    Route: null,
                    UsedModelInvocation: false));
        }

        public Task<ExplorationSubtypeEvaluationResult> DetermineExplorationSubtypeAsync(
            ConversationModeRequest request,
            ConversationModelSelectionPlan modelSelection,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ExplorationSubtypeEvaluationResult(
                    Decision: explorationSubtypeDecision
                              ?? new ExplorationSubtypeDecision(
                                  Subtype: ExplorationSubtype.Open,
                                  Confidence: 0.50d,
                                  ToolPathEligible: false,
                                  PrimaryWhy: "Stub fallback.",
                                  MissingConstraints: [],
                                  ReasonCodes: ["stub_default_subtype"]),
                    ModelSelection: modelSelection,
                    Route: null,
                    UsedModelInvocation: false));
        }
    }

    private sealed class StubConstraintExtractor : ILocalDiscoveryConstraintExtractor
    {
        public LocalDiscoveryConstraintExtractionResult Extract(string? userQuery)
        {
            return new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.90d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["park"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: ["stub_constraint_extract"]);
        }
    }

    private sealed class FixedConstraintExtractor(LocalDiscoveryConstraintExtractionResult result) : ILocalDiscoveryConstraintExtractor
    {
        public LocalDiscoveryConstraintExtractionResult Extract(string? userQuery) => result;
    }

    private sealed class StubQueryShaper : ILocalDiscoveryQueryShaper
    {
        public LocalDiscoveryShapedQueryResult Shape(
            string userQuery,
            PlaceSearchLocationContext? locationContext,
            LocalDiscoveryConstraintExtractionResult? constraints = null)
        {
            return new LocalDiscoveryShapedQueryResult(
                Query: "parks",
                Constraints: constraints ?? new StubConstraintExtractor().Extract(userQuery),
                ReasonCodes: ["stub_query_shape"]);
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
            CallCount++;
            return Task.FromResult(new PlaceSearchResult([]));
        }
    }

    private sealed class FixedPlacesSearchService(IReadOnlyList<PlaceSearchItem> items) : IPlacesSearchService
    {
        public int CallCount { get; private set; }

        public Task<PlaceSearchResult> SearchAsync(
            string query,
            string country,
            PlaceSearchLocationContext? locationContext,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PlaceSearchResult(items));
        }
    }

    private sealed class StubResultContextService : IResultContextService
    {
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
            throw new InvalidOperationException("WriteAsync should not be called in this test.");
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

        public Task ClearExpiredBindingsAsync(
            Guid conversationThreadId,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubConversationContextService : IConversationContextService
    {
        public ConversationContextBuildResult BuildContext(ConversationContextBuildRequest request)
        {
            return new ConversationContextBuildResult(
                ContextMessages: [],
                IncludedTurns: [],
                ExcludedTurns: [],
                ContextSummary: null,
                StructuredState: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReasonCodes: ["stub_context"]);
        }
    }

    private sealed class StubModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return new AIModelRoute(
                TaskType: taskType,
                ModelClass: preferredModelClass == AIModelClass.Any ? AIModelClass.Fast : preferredModelClass,
                Model: "stub-model",
                Deployment: "stub-deployment",
                IsFallback: false,
                Reason: "stub_route",
                Notes: []);
        }
    }

    private sealed class StubBehaviorEngine(ICollection<string> order) : IConversationBehaviorEngine
    {
        public Task<ConversationBehaviorResult> EvaluateAsync(
            ConversationBehaviorRequest request,
            CancellationToken cancellationToken)
        {
            order.Add("behavior");

            return Task.FromResult(
                new ConversationBehaviorResult(
                    StrategyDecision: new ConversationTurnStrategyDecision(
                        Strategy: ConversationBehaviorStrategy.GeneralGuidance,
                        ModeCandidate: ConversationMode.Exploration,
                        Readiness: new ReadinessTransition(
                            From: ConversationReadinessLevel.R1_Vague,
                            To: ConversationReadinessLevel.R2_DirectionKnown),
                        Confidence: 0.82d,
                        FollowUpBindingType: FollowUpBindingType.None,
                        ClarificationQuestion: null,
                        SuggestedOptions: ["Ask for a shortlist"],
                        ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                        ReasonCodes: ["stub_behavior"]),
                    State: CreateDefaultState() with
                    {
                        ModeCandidate = ConversationMode.Exploration,
                        ReadinessLevel = ConversationReadinessLevel.R2_DirectionKnown
                    },
                    RouteToModeHandler: true,
                    StayInDirectMode: false,
                    TargetMode: ConversationMode.Exploration,
                    ExplorationSubtypeDecision: new ExplorationSubtypeDecision(
                        Subtype: ExplorationSubtype.Open,
                        Confidence: 0.81d,
                        ToolPathEligible: false,
                        PrimaryWhy: "Stub open exploration.",
                        MissingConstraints: [],
                        ReasonCodes: ["stub_open"]),
                    CompositionRequest: null,
                    PrimaryDecisionModelSelection: new ConversationModelSelectionPlan(
                        SelectionKind: ConversationModelSelectionKind.Fast,
                        ModelClass: AIModelClass.Fast,
                        SelectionReason: "stub_primary_fast",
                        EscalationJustification: null,
                        CouldAvoidEscalation: false,
                        ReasonCodes: ["stub_primary_fast"]),
                    ExplorationSubtypeModelSelection: new ConversationModelSelectionPlan(
                        SelectionKind: ConversationModelSelectionKind.Deterministic,
                        ModelClass: null,
                        SelectionReason: "stub_subtype_deterministic",
                        EscalationJustification: null,
                        CouldAvoidEscalation: false,
                        ReasonCodes: ["stub_subtype_deterministic"]),
                    DecisionModelCallCount: 1,
                    HeavyDecisionModelCallCount: 0,
                    FastDecisionModelCallCount: 1,
                    ReasonCodes: ["stub_behavior"],
                    Warnings: []));
        }
    }

    private sealed class TrackingModeRouter(ICollection<string> order) : IModeRouter
    {
        public Task<ConversationModeExecutionResult> RouteAsync(
            ConversationModeRequest request,
            CancellationToken cancellationToken)
        {
            order.Add("mode");

            return Task.FromResult(
                new ConversationModeExecutionResult(
                    CompositionRequest: new ResponseCompositionRequest(
                        ResponseType: ResponseCompositionType.Suggest,
                        ToneDirective: ResponseToneDirective.Supportive,
                        Strategy: ConversationBehaviorStrategy.GeneralGuidance,
                        Mode: ConversationMode.Exploration,
                        ReadinessLevel: ConversationReadinessLevel.R2_DirectionKnown,
                        UserMessage: request.Request.UserMessage,
                        GroundedData: new GroundedDataEnvelope([], [], []),
                        Constraints: request.State.Constraints,
                        MissingConstraints: [],
                        MaxLengthHint: 300,
                        ClarificationQuestion: null,
                        SuggestedOptions: ["Add an area"]),
                    DeterministicReplyText: null,
                    SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    State: request.State with
                    {
                        ActiveMode = ConversationMode.Exploration
                    },
                    ResultContext: null,
                    Warnings: [],
                    FollowUpIntentHints: [],
                    Succeeded: true));
        }
    }

    private sealed class StructuredResultSummaryModeRouter(ICollection<string> order) : IModeRouter
    {
        public Task<ConversationModeExecutionResult> RouteAsync(
            ConversationModeRequest request,
            CancellationToken cancellationToken)
        {
            order.Add("mode");

            return Task.FromResult(
                new ConversationModeExecutionResult(
                    CompositionRequest: new ResponseCompositionRequest(
                        ResponseType: ResponseCompositionType.ResultSummary,
                        ToneDirective: ResponseToneDirective.Neutral,
                        Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                        Mode: ConversationMode.Exploration,
                        ReadinessLevel: ConversationReadinessLevel.R4_ToolReady,
                        UserMessage: request.Request.UserMessage,
                        GroundedData: new GroundedDataEnvelope(
                            Entities:
                            [
                                new ConversationSuggestedEntity("cafe-1", "Bean Room", 1),
                                new ConversationSuggestedEntity("cafe-2", "Roast House", 2)
                            ],
                            SummaryFacts:
                            [
                                new GroundedDataPoint("Bean Room", "cafe, rating 4.6, open now"),
                                new GroundedDataPoint("Roast House", "cafe, rating 4.4, Dublin 2")
                            ],
                            Warnings: []),
                        Constraints: request.State.Constraints,
                        MissingConstraints: [],
                        MaxLengthHint: 400,
                        ClarificationQuestion: null,
                        SuggestedOptions: ["Refine the shortlist", "Compare options"]),
                    DeterministicReplyText: null,
                    SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    State: request.State with
                    {
                        ActiveMode = ConversationMode.Exploration,
                        ReadinessLevel = ConversationReadinessLevel.R4_ToolReady
                    },
                    ResultContext: null,
                    Warnings: [],
                    FollowUpIntentHints: [],
                    Succeeded: true));
        }
    }

    private sealed class StubResponseComposer(ICollection<string> order) : IResponseComposer
    {
        public Task<ResponseCompositionResult> ComposeAsync(
            ResponseCompositionRequest request,
            string correlationId,
            CancellationToken cancellationToken)
        {
            order.Add("compose");

            return Task.FromResult(
                new ResponseCompositionResult(
                    ReplyText: "Here is a guided next step.",
                    SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ModelUsed: "stub-model",
                    DeploymentUsed: "stub-deployment",
                    ReasoningClass: AIModelClass.Fast,
                    UsedModelInvocation: true,
                    UsedDeterministicPath: false,
                    FallbackUsed: false,
                    SelectionReason: "stub_response_composition",
                    RecoveryReason: null,
                    Warnings: [],
                    FollowUpIntentHints: []));
        }
    }

    private sealed class RecoveringResponseComposer(ICollection<string> order) : IResponseComposer
    {
        public Task<ResponseCompositionResult> ComposeAsync(
            ResponseCompositionRequest request,
            string correlationId,
            CancellationToken cancellationToken)
        {
            order.Add("compose");

            return Task.FromResult(
                new ResponseCompositionResult(
                    ReplyText: "Here are a few grounded options to start with:\n1. Example Place",
                    SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ModelUsed: "deterministic-response-composer",
                    DeploymentUsed: "deterministic-response-composer",
                    ReasoningClass: AIModelClass.Fast,
                    UsedModelInvocation: true,
                    UsedDeterministicPath: true,
                    FallbackUsed: true,
                    SelectionReason: "response_composition_safe_fallback",
                    RecoveryReason: "structured_parse_failed",
                    Warnings: ["structured_parse_failed", "response_composition_safe_fallback"],
                    FollowUpIntentHints: ["compare_options"]));
        }
    }

    private sealed class StaticResponseCompositionModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass desiredClass, string? complexityHint = null)
        {
            return new AIModelRoute(
                taskType,
                AIModelClass.Fast,
                "gpt-4.1",
                "gpt-4.1",
                false,
                "test_fast_route",
                []);
        }
    }

    private sealed class InvalidStructuredResponseAIClient : IAIClient
    {
        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            const string payload = """
                {
                  "warnings": ["model_internal_warning"],
                  "suggestedStructuredStateUpdates": { "mode": "exploration" }
                }
                """;

            return Task.FromResult(
                new AIResponse(
                    Content: payload,
                    StructuredPayloadJson: payload,
                    FinishReason: "stop",
                    Provider: "stub",
                    Model: route.Model,
                    Deployment: route.Deployment,
                    InputTokenEstimate: 12,
                    OutputTokenEstimate: 30,
                    LatencyMs: 20,
                    WasMocked: true,
                    RawDiagnostics: null,
                    Succeeded: true,
                    FailureReason: null));
        }
    }

    private sealed class TransportCancelledResponseCompositionAIClient : IAIClient
    {
        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            throw new TaskCanceledException("The operation was canceled.");
        }
    }
}
