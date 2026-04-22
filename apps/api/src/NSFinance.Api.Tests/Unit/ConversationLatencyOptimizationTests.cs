using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class ConversationLatencyOptimizationTests
{
    [Fact]
    public void ConversationModelRoutingPolicy_UsesDeterministicPrimary_ForObviousStructuredSearch()
    {
        var policy = new ConversationModelRoutingPolicy();

        var selection = policy.SelectPrimaryDecision(
            BuildBehaviorRequest("coffee shops near me", CreateDefaultState()),
            ConversationSignalAnalyzer.Analyze("coffee shops near me"),
            FollowUpBindingType.None,
            CreateDefaultState());

        Assert.Equal(ConversationModelSelectionKind.Deterministic, selection.SelectionKind);
        Assert.Equal("structured_search_deterministic", selection.SelectionReason);
        Assert.Null(selection.ModelClass);
    }

    [Fact]
    public void ConversationModelRoutingPolicy_UsesHeavyPrimary_WithJustification_ForOpenExploration()
    {
        var policy = new ConversationModelRoutingPolicy();

        var selection = policy.SelectPrimaryDecision(
            BuildBehaviorRequest("somewhere nice to go tonight", CreateDefaultState()),
            ConversationSignalAnalyzer.Analyze("somewhere nice to go tonight"),
            FollowUpBindingType.None,
            CreateDefaultState());

        Assert.Equal(ConversationModelSelectionKind.HeavyReasoning, selection.SelectionKind);
        Assert.Equal(AIModelClass.HeavyReasoning, selection.ModelClass);
        Assert.Equal("open_exploration_heavy", selection.SelectionReason);
        Assert.False(string.IsNullOrWhiteSpace(selection.EscalationJustification));
    }

    [Fact]
    public void ExplorationSubtypeDecisionPolicy_FastPathsObviousStructuredLocalSearch()
    {
        var policy = new ExplorationSubtypeDecisionPolicy();

        var plan = policy.DetermineResolution(
            "coffee shops near me",
            CreateDefaultState(),
            resultContext: null,
            bindingType: FollowUpBindingType.None,
            strategyDecision: CreateExplorationDecision());

        Assert.False(plan.RequiresModelReasoning);
        Assert.NotNull(plan.Decision);
        Assert.Equal(ExplorationSubtype.Structured, plan.Decision!.Subtype);
        Assert.Equal("structured_fast_path", plan.ResolutionSource);
    }

    [Fact]
    public void ExplorationSubtypeDecisionPolicy_FastPathsExperientialOpenPrompt()
    {
        var policy = new ExplorationSubtypeDecisionPolicy();

        var plan = policy.DetermineResolution(
            "somewhere with nice lighting",
            CreateDefaultState(),
            resultContext: null,
            bindingType: FollowUpBindingType.None,
            strategyDecision: CreateExplorationDecision());

        Assert.False(plan.RequiresModelReasoning);
        Assert.NotNull(plan.Decision);
        Assert.Equal(ExplorationSubtype.Open, plan.Decision!.Subtype);
        Assert.Equal("open_fast_path", plan.ResolutionSource);
    }

    [Fact]
    public void ExplorationSubtypeDecisionPolicy_FastPathsStructuredFollowUpBoundToResults()
    {
        var policy = new ExplorationSubtypeDecisionPolicy();

        var plan = policy.DetermineResolution(
            "Do they have parking?",
            CreateDefaultState(),
            CreateResultContextSnapshot(ExplorationSubtype.Structured),
            FollowUpBindingType.BindPrior,
            CreateExplorationDecision());

        Assert.False(plan.RequiresModelReasoning);
        Assert.NotNull(plan.Decision);
        Assert.Equal(ExplorationSubtype.Structured, plan.Decision!.Subtype);
        Assert.Equal("result_context_structured_fast_path", plan.ResolutionSource);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_SkipsSubtypeModel_ForObviousStructuredExploration()
    {
        var telemetry = new RecordingTelemetry();
        var decisionEngine = new CountingDecisionEngine(
            CreateExplorationDecision(),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Open,
                Confidence: 0.55d,
                ToolPathEligible: false,
                PrimaryWhy: "stub-open",
                MissingConstraints: [],
                ReasonCodes: ["stub_open"]));
        var engine = CreateBehaviorEngine(decisionEngine, telemetry);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest("coffee shops near me", CreateDefaultState()),
            CancellationToken.None);

        Assert.Equal(0, decisionEngine.PrimaryDecisionModelInvocationCount);
        Assert.Equal(0, decisionEngine.SubtypeDecisionCallCount);
        Assert.NotNull(result.ExplorationSubtypeDecision);
        Assert.Equal(ExplorationSubtype.Structured, result.ExplorationSubtypeDecision!.Subtype);
        Assert.Contains("behavior_guard_near_me_requires_location_evidence", result.StrategyDecision.ReasonCodes);
        Assert.Equal(ConversationModelSelectionKind.Deterministic, result.PrimaryDecisionModelSelection.SelectionKind);
        Assert.Equal(0, result.DecisionModelCallCount);
        Assert.Equal(0, result.HeavyDecisionModelCallCount);
        Assert.Equal(0, result.FastDecisionModelCallCount);

        var resolutionEvent = telemetry.Single("chat.turn.exploration_subtype_resolution");
        Assert.Equal("structured_fast_path", resolutionEvent["resolutionSource"]);
        Assert.Equal(false, resolutionEvent["usedModelReasoning"]);
        Assert.Equal(true, resolutionEvent["heavySubtypeCallSkipped"]);
        Assert.Equal(0, resolutionEvent["decisionModelCallCount"]);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_SkipsSubtypeModel_ForStructuredFollowUpTurn()
    {
        var telemetry = new RecordingTelemetry();
        var decisionEngine = new CountingDecisionEngine(
            CreateExplorationDecision(),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Open,
                Confidence: 0.55d,
                ToolPathEligible: false,
                PrimaryWhy: "stub-open",
                MissingConstraints: [],
                ReasonCodes: ["stub_open"]));
        var engine = CreateBehaviorEngine(decisionEngine, telemetry);
        var resultContext = CreateResultContextSnapshot(ExplorationSubtype.Structured);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest(
                "Do they have parking?",
                CreateDefaultState(),
                resultContext,
                ResultContextBindingClassification.None),
            CancellationToken.None);

        Assert.Equal(1, decisionEngine.PrimaryDecisionModelInvocationCount);
        Assert.Equal(0, decisionEngine.SubtypeDecisionCallCount);
        Assert.NotNull(result.ExplorationSubtypeDecision);
        Assert.Equal(ExplorationSubtype.Structured, result.ExplorationSubtypeDecision!.Subtype);
        Assert.Equal(ConversationModelSelectionKind.Fast, result.PrimaryDecisionModelSelection.SelectionKind);

        var resolutionEvent = telemetry.Single("chat.turn.exploration_subtype_resolution");
        Assert.Equal("result_context_structured_fast_path", resolutionEvent["resolutionSource"]);
        Assert.Equal(1, resolutionEvent["decisionModelCallCount"]);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_UsesSubtypeModel_ForAmbiguousExploration()
    {
        var telemetry = new RecordingTelemetry();
        var decisionEngine = new CountingDecisionEngine(
            CreateExplorationDecision(),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Open,
                Confidence: 0.79d,
                ToolPathEligible: false,
                PrimaryWhy: "model-open",
                MissingConstraints: ["location_or_area"],
                ReasonCodes: ["model_open"]));
        var engine = CreateBehaviorEngine(decisionEngine, telemetry);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest("where should I go in Dublin 2", CreateDefaultState()),
            CancellationToken.None);

        Assert.Equal(1, decisionEngine.PrimaryDecisionModelInvocationCount);
        Assert.Equal(1, decisionEngine.SubtypeDecisionCallCount);
        Assert.NotNull(result.ExplorationSubtypeDecision);
        Assert.Equal(ExplorationSubtype.Open, result.ExplorationSubtypeDecision!.Subtype);
        Assert.Equal(ConversationModelSelectionKind.HeavyReasoning, result.PrimaryDecisionModelSelection.SelectionKind);
        Assert.Equal(ConversationModelSelectionKind.Fast, result.ExplorationSubtypeModelSelection?.SelectionKind);
        Assert.Equal(2, result.DecisionModelCallCount);
        Assert.Equal(1, result.HeavyDecisionModelCallCount);
        Assert.Equal(1, result.FastDecisionModelCallCount);

        var resolutionEvent = telemetry.Single("chat.turn.exploration_subtype_resolution");
        Assert.Equal("model_fast", resolutionEvent["resolutionSource"]);
        Assert.Equal(true, resolutionEvent["usedModelReasoning"]);
        Assert.Equal(true, resolutionEvent["heavySubtypeCallSkipped"]);
        Assert.Equal(2, resolutionEvent["decisionModelCallCount"]);
    }

    [Fact]
    public async Task ConversationDecisionEngine_EmitsDistinctInvocationStages_ForPrimaryAndSubtypePasses()
    {
        var telemetry = new RecordingTelemetry();
        var engine = new ConversationDecisionEngine(
            new StubConversationDecisionPromptBuilder(),
            new StubExplorationSubtypePromptBuilder(),
            new StaticConversationDecisionParser(CreateExplorationDecision()),
            new StaticExplorationSubtypeDecisionParser(
                new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.84d,
                    ToolPathEligible: true,
                    PrimaryWhy: "stub-structured",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"])),
            new StaticModelRouter(),
            new StaticAIClient(),
            telemetry,
            NullLogger<ConversationDecisionEngine>.Instance);

        await engine.EvaluateAsync(
            BuildBehaviorRequest("coffee shops near me", CreateDefaultState()),
            new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "test_primary_fast",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: ["test_primary_fast"]),
            CancellationToken.None);
        await engine.DetermineExplorationSubtypeAsync(
            BuildModeRequest("coffee shops near me"),
            new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "test_subtype_fast",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: ["test_subtype_fast"]),
            CancellationToken.None);

        var invocationEvents = telemetry.ByName("chat.model.invocation").ToArray();
        Assert.Contains(invocationEvents, evt => Equals(evt["invocationStage"], "conversation_behavior_primary"));
        Assert.Contains(invocationEvents, evt => Equals(evt["invocationStage"], "exploration_subtype"));
    }

    [Fact]
    public async Task ConversationDecisionEngine_SkipsModelInvocation_ForDeterministicPrimarySelection()
    {
        var telemetry = new RecordingTelemetry();
        var aiClient = new StaticAIClient();
        var engine = new ConversationDecisionEngine(
            new StubConversationDecisionPromptBuilder(),
            new StubExplorationSubtypePromptBuilder(),
            new StaticConversationDecisionParser(CreateExplorationDecision()),
            new StaticExplorationSubtypeDecisionParser(
                new ExplorationSubtypeDecision(
                    Subtype: ExplorationSubtype.Structured,
                    Confidence: 0.84d,
                    ToolPathEligible: true,
                    PrimaryWhy: "stub-structured",
                    MissingConstraints: [],
                    ReasonCodes: ["stub_structured"])),
            new StaticModelRouter(),
            aiClient,
            telemetry,
            NullLogger<ConversationDecisionEngine>.Instance);

        var evaluation = await engine.EvaluateAsync(
            BuildBehaviorRequest("coffee shops near me", CreateDefaultState()),
            new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Deterministic,
                ModelClass: null,
                SelectionReason: "structured_search_deterministic",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: ["structured_search_deterministic"]),
            CancellationToken.None);

        Assert.False(evaluation.UsedModelInvocation);
        Assert.Empty(telemetry.ByName("chat.model.invocation"));
        Assert.Single(telemetry.ByName("chat.model.selection"));
    }

    private static ConversationBehaviorEngine CreateBehaviorEngine(
        CountingDecisionEngine decisionEngine,
        RecordingTelemetry telemetry)
    {
        return new ConversationBehaviorEngine(
            decisionEngine,
            new ConversationModelRoutingPolicy(),
            new ReadinessTransitionPolicy(),
            new FollowUpBindingPolicy(),
            new ContradictionResolutionPolicy(),
            new FinancialActivationPolicy(),
            new ExplorationSubtypeDecisionPolicy(),
            new ToolGuardWarningPolicy(),
            telemetry);
    }

    private static ConversationBehaviorRequest BuildBehaviorRequest(
        string userMessage,
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext = null,
        ResultContextBindingClassification bindingClassification = ResultContextBindingClassification.None,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var request = BuildChatRequest(userMessage, state, metadata);
        return new ConversationBehaviorRequest(
            Request: request,
            ContextMessages: [],
            ContextSummary: null,
            EffectiveState: state,
            ResultContext: resultContext,
            ResultContextReadResult: new ResultContextReadResult(
                ActiveResultContext: resultContext,
                BindingClassification: bindingClassification,
                UsedClientResultSetId: false,
                ExpiredBindingCleared: false,
                ReasonCodes: []),
            ClientMetadata: metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            FailureHistory: [],
            CancellationToken: CancellationToken.None);
    }

    private static ConversationModeRequest BuildModeRequest(string userMessage)
    {
        return new ConversationModeRequest(
            Request: BuildChatRequest(userMessage, CreateDefaultState()),
            ContextMessages: [],
            ContextSummary: null,
            State: CreateDefaultState(),
            ResultContext: null,
            StrategyDecision: CreateExplorationDecision(),
            ExplorationSubtypeDecision: null,
            ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static UserChatRequest BuildChatRequest(
        string userMessage,
        ConversationStateSnapshot state,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new UserChatRequest(
            UserMessage: userMessage,
            RecentTurns: [],
            State: state,
            CorrelationId: Guid.NewGuid().ToString("N"),
            Metadata: metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ClientRequestId: Guid.NewGuid().ToString("N"),
            UserId: null,
            ConversationThreadId: null,
            UsePersistentMemory: false,
            AllowTransientFallbackOnPersistentFailure: false);
    }

    private static ConversationTurnStrategyDecision CreateExplorationDecision()
    {
        return new ConversationTurnStrategyDecision(
            Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
            ModeCandidate: ConversationMode.Exploration,
            Readiness: new ReadinessTransition(
                From: ConversationReadinessLevel.R1_Vague,
                To: ConversationReadinessLevel.R2_DirectionKnown),
            Confidence: 0.82d,
            FollowUpBindingType: FollowUpBindingType.None,
            ClarificationQuestion: null,
            SuggestedOptions: [],
            ToolExecutionPermission: ToolExecutionPermission.Forbidden,
            ReasonCodes: ["stub_exploration"]);
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

    private static ResultContextSnapshot CreateResultContextSnapshot(ExplorationSubtype subtype)
    {
        var now = DateTime.UtcNow;
        var resultSetId = Guid.NewGuid();
        return new ResultContextSnapshot(
            ResultSetId: resultSetId,
            ParentResultSetId: null,
            BranchRootResultSetId: resultSetId,
            SourceMode: ConversationMode.Exploration,
            SourceSubtype: subtype,
            QueryFingerprint: "structured-query",
            NormalizedConstraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ConversationConstraintKeys.ExplorationSubtype] = subtype.ToString()
            },
            SuggestedEntities:
            [
                new ResultContextEntity(
                    EntityId: "place-1",
                    Label: "Coffee Spot",
                    Rank: 1,
                    StableReference: "https://maps.example/1")
            ],
            SelectedEntityId: null,
            ActiveUntilUtc: now.AddMinutes(10),
            ExpiresUtc: now.AddHours(1),
            IsExpired: false,
            IsActiveWindowExpired: false);
    }

    private sealed class CountingDecisionEngine(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision subtypeDecision) : IConversationDecisionEngine
    {
        public int PrimaryDecisionModelInvocationCount { get; private set; }
        public int SubtypeDecisionCallCount { get; private set; }

        public Task<ConversationDecisionEvaluationResult> EvaluateAsync(
            ConversationBehaviorRequest request,
            ConversationModelSelectionPlan modelSelection,
            CancellationToken cancellationToken)
        {
            if (modelSelection.SelectionKind != ConversationModelSelectionKind.Deterministic)
            {
                PrimaryDecisionModelInvocationCount++;
            }

            return Task.FromResult(
                new ConversationDecisionEvaluationResult(
                    Decision: decision,
                    ModelSelection: modelSelection,
                    Route: null,
                    UsedModelInvocation: modelSelection.SelectionKind != ConversationModelSelectionKind.Deterministic));
        }

        public Task<ExplorationSubtypeEvaluationResult> DetermineExplorationSubtypeAsync(
            ConversationModeRequest request,
            ConversationModelSelectionPlan modelSelection,
            CancellationToken cancellationToken)
        {
            if (modelSelection.SelectionKind != ConversationModelSelectionKind.Deterministic)
            {
                SubtypeDecisionCallCount++;
            }

            return Task.FromResult(
                new ExplorationSubtypeEvaluationResult(
                    Decision: subtypeDecision,
                    ModelSelection: modelSelection,
                    Route: null,
                    UsedModelInvocation: modelSelection.SelectionKind != ConversationModelSelectionKind.Deterministic));
        }
    }

    private sealed class RecordingTelemetry : IChatTelemetry
    {
        private readonly List<TelemetryEvent> events = [];

        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken)
        {
            events.Add(new TelemetryEvent(eventName, new Dictionary<string, object?>(properties)));
            return Task.CompletedTask;
        }

        public IReadOnlyDictionary<string, object?> Single(string eventName)
        {
            return events.Single(evt => string.Equals(evt.Name, eventName, StringComparison.Ordinal)).Properties;
        }

        public IEnumerable<IReadOnlyDictionary<string, object?>> ByName(string eventName)
        {
            return events
                .Where(evt => string.Equals(evt.Name, eventName, StringComparison.Ordinal))
                .Select(evt => (IReadOnlyDictionary<string, object?>)evt.Properties);
        }

        private sealed record TelemetryEvent(string Name, IReadOnlyDictionary<string, object?> Properties);
    }

    private sealed class StaticAIClient : IAIClient
    {
        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AIResponse(
                Content: "{}",
                StructuredPayloadJson: "{}",
                FinishReason: "stop",
                Provider: "stub",
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: 1,
                OutputTokenEstimate: 1,
                LatencyMs: 1,
                WasMocked: true,
                RawDiagnostics: null,
                Succeeded: true,
                FailureReason: null));
        }
    }

    private sealed class StaticModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return new AIModelRoute(
                TaskType: taskType,
                ModelClass: preferredModelClass == AIModelClass.Any ? AIModelClass.Fast : preferredModelClass,
                Model: preferredModelClass == AIModelClass.HeavyReasoning ? "gpt-5-chat" : "gpt-4.1",
                Deployment: preferredModelClass == AIModelClass.HeavyReasoning ? "gpt-5-chat" : "gpt-4.1",
                IsFallback: false,
                Reason: "stub_route",
                Notes: string.IsNullOrWhiteSpace(complexityHint) ? [] : [complexityHint]);
        }
    }

    private sealed class StubConversationDecisionPromptBuilder : IConversationDecisionPromptBuilder
    {
        public PromptBuildResult BuildPrompt(ConversationDecisionPromptInput input)
        {
            return new PromptBuildResult(
                SystemInstructions: null,
                Messages: [AIMessage.User("stub-decision")],
                StructuredSchemaName: "stub",
                ReasonCodes: ["stub_prompt"]);
        }
    }

    private sealed class StubExplorationSubtypePromptBuilder : IExplorationSubtypePromptBuilder
    {
        public PromptBuildResult BuildPrompt(ExplorationSubtypePromptInput input)
        {
            return new PromptBuildResult(
                SystemInstructions: null,
                Messages: [AIMessage.User("stub-subtype")],
                StructuredSchemaName: "stub",
                ReasonCodes: ["stub_prompt"]);
        }
    }

    private sealed class StaticConversationDecisionParser(
        ConversationTurnStrategyDecision decision) : IConversationDecisionParser
    {
        public bool TryParse(
            AIResponse response,
            AIModelRoute route,
            ConversationStateSnapshot currentState,
            out ConversationTurnStrategyDecision? decisionResult,
            out IReadOnlyList<string> reasonCodes,
            out string? failureReason)
        {
            decisionResult = decision;
            reasonCodes = [];
            failureReason = null;
            return true;
        }
    }

    private sealed class StaticExplorationSubtypeDecisionParser(
        ExplorationSubtypeDecision decision) : IExplorationSubtypeDecisionParser
    {
        public bool TryParse(
            AIResponse response,
            AIModelRoute route,
            out ExplorationSubtypeDecision? decisionResult,
            out IReadOnlyList<string> reasonCodes,
            out string? failureReason)
        {
            decisionResult = decision;
            reasonCodes = [];
            failureReason = null;
            return true;
        }
    }
}
