using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class ConversationArchitectureGuardTests
{
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
                    UsedDeterministicPath: false,
                    FallbackUsed: false,
                    SelectionReason: "stub_response_composition",
                    RecoveryReason: null,
                    Warnings: [],
                    FollowUpIntentHints: []));
        }
    }
}
