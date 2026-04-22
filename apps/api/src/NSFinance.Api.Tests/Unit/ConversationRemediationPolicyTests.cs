using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class ConversationRemediationPolicyTests
{
    [Fact]
    public async Task ConversationBehaviorEngine_ActivatesFinancialMode_AfterExplicitConfirmationInValidatedThread()
    {
        var state = CreateDefaultState(
            semanticFamily: ConversationSemanticFamilies.Financial,
            financialFocus: "subscriptions") with
        {
            TransitionIntent = ConversationTransitionIntents.FinancialValidationPending,
            InferredIntent = ConversationSemanticFamilies.Financial,
            ReadinessLevel = ConversationReadinessLevel.R2_DirectionKnown
        };

        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                ModeCandidate: ConversationMode.Financial,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R2_DirectionKnown,
                    To: ConversationReadinessLevel.R2_DirectionKnown),
                Confidence: 0.88d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes: ["stub_financial"]),
            null);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest("yes please review subscriptions", state),
            CancellationToken.None);

        Assert.True(result.RouteToModeHandler);
        Assert.False(result.StayInDirectMode);
        Assert.Equal(ConversationMode.Financial, result.TargetMode);
        Assert.Equal(ConversationBehaviorStrategy.ConfirmAndTransition, result.StrategyDecision.Strategy);
        Assert.Contains("financial_activation_validated", result.StrategyDecision.ReasonCodes);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_ActivatesFinancialMode_FromStrongEngagedFinancialFocusSelection()
    {
        var state = CreateDefaultState(
            semanticFamily: ConversationSemanticFamilies.Financial,
            financialFocus: "subscriptions") with
        {
            TransitionIntent = ConversationTransitionIntents.FinancialValidationPending,
            InferredIntent = ConversationSemanticFamilies.Financial,
            ReadinessLevel = ConversationReadinessLevel.R2_DirectionKnown
        };

        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                ModeCandidate: ConversationMode.Financial,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R2_DirectionKnown,
                    To: ConversationReadinessLevel.R2_DirectionKnown),
                Confidence: 0.84d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes: ["stub_financial"]),
            null);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest("subscriptions", state),
            CancellationToken.None);

        Assert.True(result.RouteToModeHandler);
        Assert.Equal(ConversationMode.Financial, result.TargetMode);
        Assert.Equal(ConversationBehaviorStrategy.ConfirmAndTransition, result.StrategyDecision.Strategy);
        Assert.Contains("financial_activation_validated", result.StrategyDecision.ReasonCodes);
    }

    [Fact]
    public void ContradictionResolutionPolicy_OverwritesCorrectedLocationAndClearsSelection()
    {
        var state = CreateDefaultState(
            semanticFamily: ConversationSemanticFamilies.Exploration,
            explorationSubtype: ExplorationSubtype.Structured.ToString(),
            explorationArea: "near_me",
            explorationPlaceTypes: "park",
            explorationPreferences: "quiet") with
        {
            ReadinessLevel = ConversationReadinessLevel.R4_ToolReady,
            SelectedEntityId = "park-1",
            LastExecutionFingerprint = "fingerprint",
            ResultContextRef = CreateResultContextReference(),
            LastSuggestedEntities =
            [
                new ConversationSuggestedEntity("park-1", "Phoenix Park", 1)
            ]
        };

        var policy = new ContradictionResolutionPolicy();
        var result = policy.Apply(
            state,
            "actually around Dublin 2",
            FollowUpBindingType.Refine,
            ConversationSignalAnalyzer.Analyze("actually around Dublin 2"));

        Assert.Equal(FollowUpBindingType.Refine, result.BindingTypeOverride);
        Assert.Equal("dublin 2", result.State.Constraints[ConversationConstraintKeys.ExplorationArea]);
        Assert.Equal("park", result.State.Constraints[ConversationConstraintKeys.ExplorationPlaceTypes]);
        Assert.Null(result.State.SelectedEntityId);
        Assert.Null(result.State.LastExecutionFingerprint);
        Assert.Contains("contradiction_exploration_area_rewritten", result.ReasonCodes);
    }

    [Fact]
    public void ContradictionResolutionPolicy_RewritesPlaceTypeIntoNewBranch()
    {
        var state = CreateDefaultState(
            semanticFamily: ConversationSemanticFamilies.Exploration,
            explorationSubtype: ExplorationSubtype.Structured.ToString(),
            explorationArea: "Dublin 2",
            explorationPlaceTypes: "cafe") with
        {
            SelectedEntityId = "cafe-1",
            LastExecutionFingerprint = "cafes-d2",
            ResultContextRef = CreateResultContextReference(),
            LastSuggestedEntities =
            [
                new ConversationSuggestedEntity("cafe-1", "Cafe One", 1)
            ]
        };

        var policy = new ContradictionResolutionPolicy();
        var result = policy.Apply(
            state,
            "actually pubs",
            FollowUpBindingType.BindPrior,
            ConversationSignalAnalyzer.Analyze("actually pubs"));

        Assert.Equal(FollowUpBindingType.NewBranch, result.BindingTypeOverride);
        Assert.Equal("bar", result.State.Constraints[ConversationConstraintKeys.ExplorationPlaceTypes]);
        Assert.Null(result.State.SelectedEntityId);
        Assert.Empty(result.State.LastSuggestedEntities ?? []);
        Assert.Contains("contradiction_exploration_place_types_rewritten", result.ReasonCodes);
    }

    [Fact]
    public void ContradictionResolutionPolicy_RewritesIncompatiblePreference()
    {
        var state = CreateDefaultState(
            semanticFamily: ConversationSemanticFamilies.Exploration,
            explorationSubtype: ExplorationSubtype.Open.ToString(),
            explorationPreferences: "quiet");

        var policy = new ContradictionResolutionPolicy();
        var result = policy.Apply(
            state,
            "actually lively",
            FollowUpBindingType.Refine,
            ConversationSignalAnalyzer.Analyze("actually lively"));

        Assert.Equal(FollowUpBindingType.Refine, result.BindingTypeOverride);
        Assert.Equal("lively", result.State.Constraints[ConversationConstraintKeys.ExplorationPreferences]);
        Assert.DoesNotContain("quiet", result.State.Constraints[ConversationConstraintKeys.ExplorationPreferences], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContradictionResolutionPolicy_ClearsStructuredPlaceTypeWhenSwitchingToOpenBranch()
    {
        var state = CreateDefaultState(
            semanticFamily: ConversationSemanticFamilies.Exploration,
            explorationSubtype: ExplorationSubtype.Structured.ToString(),
            explorationArea: "Dublin 2",
            explorationPlaceTypes: "cafe") with
        {
            ResultContextRef = CreateResultContextReference(),
            SelectedEntityId = "cafe-1",
            LastExecutionFingerprint = "cafes-d2"
        };

        var policy = new ContradictionResolutionPolicy();
        var result = policy.Apply(
            state,
            "forget cafes, beach walk instead",
            FollowUpBindingType.BindPrior,
            ConversationSignalAnalyzer.Analyze("forget cafes, beach walk instead"));

        Assert.Equal(FollowUpBindingType.NewBranch, result.BindingTypeOverride);
        Assert.Equal(ExplorationSubtype.Open.ToString(), result.State.Constraints[ConversationConstraintKeys.ExplorationSubtype]);
        Assert.False(result.State.Constraints.ContainsKey(ConversationConstraintKeys.ExplorationPlaceTypes));
        Assert.Null(result.State.ResultContextRef);
    }

    [Fact]
    public void ExplorationSubtypeDecisionPolicy_KeepsVibeFirstWalkOpen()
    {
        var policy = new ExplorationSubtypeDecisionPolicy();
        var result = policy.Normalize(
            "I want a quiet place for a walk",
            CreateDefaultState(),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: 0.72d,
                ToolPathEligible: true,
                PrimaryWhy: "Stub structured classification.",
                MissingConstraints: [],
                ReasonCodes: ["stub_structured"]));

        Assert.Equal(ExplorationSubtype.Open, result.Subtype);
        Assert.False(result.ToolPathEligible);
        Assert.Contains("exploration_subtype_vibe_first_open", result.ReasonCodes);
    }

    [Fact]
    public async Task ConversationDecisionEngine_FallbackSubtypeLeavesAtmosphericPromptOpen()
    {
        var engine = CreateDecisionEngineWithFallbackParsers();

        var result = await engine.DetermineExplorationSubtypeAsync(
            BuildModeRequest("somewhere with nice lighting"),
            new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "test_fast_subtype",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: ["test_fast_subtype"]),
            CancellationToken.None);

        Assert.Equal(ExplorationSubtype.Open, result.Decision.Subtype);
        Assert.False(result.Decision.ToolPathEligible);
    }

    [Fact]
    public async Task ConversationDecisionEngine_FallbackSubtypeClassifiesOperationalPromptStructured()
    {
        var engine = CreateDecisionEngineWithFallbackParsers();

        var result = await engine.DetermineExplorationSubtypeAsync(
            BuildModeRequest("parks near me open now"),
            new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "test_fast_subtype",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: ["test_fast_subtype"]),
            CancellationToken.None);

        Assert.Equal(ExplorationSubtype.Structured, result.Decision.Subtype);
        Assert.True(result.Decision.ToolPathEligible);
    }

    [Fact]
    public void FollowUpBindingPolicy_BindsPluralFollowUpToActiveResults()
    {
        var snapshot = CreateResultContextSnapshot(activeWindowExpired: false);
        var policy = new FollowUpBindingPolicy();

        var result = policy.Determine(
            BuildChatRequest("Do they have parking?", CreateDefaultState()),
            CreateDefaultState(),
            new ResultContextReadResult(
                ActiveResultContext: snapshot,
                BindingClassification: ResultContextBindingClassification.None,
                UsedClientResultSetId: false,
                ExpiredBindingCleared: false,
                ReasonCodes: []));

        Assert.Equal(FollowUpBindingType.BindPrior, result.BindingType);
        Assert.Equal(snapshot.ResultSetId, result.ActiveResultSetId);
    }

    [Fact]
    public void FollowUpBindingPolicy_DoesNotBindExpiredActiveWindowWithoutEvidence()
    {
        var snapshot = CreateResultContextSnapshot(activeWindowExpired: true);
        var policy = new FollowUpBindingPolicy();

        var result = policy.Determine(
            BuildChatRequest("What about Dublin 2 instead?", CreateDefaultState()),
            CreateDefaultState(),
            new ResultContextReadResult(
                ActiveResultContext: snapshot,
                BindingClassification: ResultContextBindingClassification.NewBranch,
                UsedClientResultSetId: false,
                ExpiredBindingCleared: false,
                ReasonCodes: []));

        Assert.Equal(FollowUpBindingType.None, result.BindingType);
        Assert.Null(result.ActiveResultSetId);
    }

    [Fact]
    public void FollowUpBindingPolicy_UsesLineageForComparisonRequests()
    {
        var snapshot = CreateResultContextSnapshot(
            activeWindowExpired: false,
            parentResultSetId: Guid.NewGuid(),
            branchRootResultSetId: Guid.NewGuid());
        var policy = new FollowUpBindingPolicy();

        var result = policy.Determine(
            BuildChatRequest("Compare this with the first list", CreateDefaultState()),
            CreateDefaultState(),
            new ResultContextReadResult(
                ActiveResultContext: snapshot,
                BindingClassification: ResultContextBindingClassification.None,
                UsedClientResultSetId: false,
                ExpiredBindingCleared: false,
                ReasonCodes: []));

        Assert.Equal(FollowUpBindingType.Refine, result.BindingType);
        Assert.Equal(snapshot.ResultSetId, result.ActiveResultSetId);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_DoesNotEmitGuardWarning_ForOrdinaryDirectConversationTurn()
    {
        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                ModeCandidate: ConversationMode.Conversation,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R0_Unknown,
                    To: ConversationReadinessLevel.R1_Vague),
                Confidence: 0.70d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: "What do you want to focus on first?",
                SuggestedOptions: ["Clarify the goal"],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes: ["stub_conversation"]),
            null);

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest("I'm not sure where to start", CreateDefaultState()),
            CancellationToken.None);

        Assert.DoesNotContain("chat.tool.guard_blocked", result.Warnings);
    }

    [Fact]
    public async Task ConversationBehaviorEngine_EmitsGuardWarning_ForBlockedStructuredToolTransition()
    {
        var engine = CreateBehaviorEngine(
            new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                ModeCandidate: ConversationMode.Exploration,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R2_DirectionKnown,
                    To: ConversationReadinessLevel.R4_ToolReady),
                Confidence: 0.92d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                ReasonCodes: ["stub_tool_ready"]),
            new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: 0.88d,
                ToolPathEligible: true,
                PrimaryWhy: "Stub structured decision.",
                MissingConstraints: [],
                ReasonCodes: ["stub_structured"]));

        var result = await engine.EvaluateAsync(
            BuildBehaviorRequest(
                "restaurants near me",
                CreateDefaultState(),
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        Assert.Contains("chat.tool.guard_blocked", result.Warnings);
    }

    private static ConversationDecisionEngine CreateDecisionEngineWithFallbackParsers()
    {
        return new ConversationDecisionEngine(
            new StubConversationDecisionPromptBuilder(),
            new StubExplorationSubtypePromptBuilder(),
            new FailingConversationDecisionParser(),
            new FailingExplorationSubtypeDecisionParser(),
            new DeterministicConversationDecisionBuilder(),
            new StubModelRouter(),
            new StubAIClient(),
            new NoOpChatTelemetry(),
            NullLogger<ConversationDecisionEngine>.Instance);
    }

    private static ConversationModeRequest BuildModeRequest(string userMessage)
    {
        return new ConversationModeRequest(
            Request: BuildChatRequest(userMessage, CreateDefaultState()),
            ContextMessages: [],
            ContextSummary: null,
            State: CreateDefaultState(),
            ResultContext: null,
            StrategyDecision: new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                ModeCandidate: ConversationMode.Exploration,
                Readiness: new ReadinessTransition(
                    From: ConversationReadinessLevel.R1_Vague,
                    To: ConversationReadinessLevel.R2_DirectionKnown),
                Confidence: 0.7d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes: ["stub_mode_request"]),
            ExplorationSubtypeDecision: null,
            ClientMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
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

    private static ConversationBehaviorRequest BuildBehaviorRequest(
        string userMessage,
        ConversationStateSnapshot state,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var request = BuildChatRequest(userMessage, state, metadata);
        return new ConversationBehaviorRequest(
            Request: request,
            ContextMessages: [],
            ContextSummary: null,
            EffectiveState: state,
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

    private static ConversationStateSnapshot CreateDefaultState(
        string? semanticFamily = null,
        string? explorationSubtype = null,
        string? explorationArea = null,
        string? explorationPlaceTypes = null,
        string? explorationPreferences = null,
        string? financialFocus = null)
    {
        var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(semanticFamily))
        {
            constraints[ConversationConstraintKeys.SemanticFamily] = semanticFamily;
        }

        if (!string.IsNullOrWhiteSpace(explorationSubtype))
        {
            constraints[ConversationConstraintKeys.ExplorationSubtype] = explorationSubtype;
        }

        if (!string.IsNullOrWhiteSpace(explorationArea))
        {
            constraints[ConversationConstraintKeys.ExplorationArea] = explorationArea;
        }

        if (!string.IsNullOrWhiteSpace(explorationPlaceTypes))
        {
            constraints[ConversationConstraintKeys.ExplorationPlaceTypes] = explorationPlaceTypes;
        }

        if (!string.IsNullOrWhiteSpace(explorationPreferences))
        {
            constraints[ConversationConstraintKeys.ExplorationPreferences] = explorationPreferences;
        }

        if (!string.IsNullOrWhiteSpace(financialFocus))
        {
            constraints[ConversationConstraintKeys.FinancialFocus] = financialFocus;
        }

        return new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: constraints,
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: null,
            MerchantInvestigationSubject: null,
            RecentConclusions: []);
    }

    private static ResultContextSnapshot CreateResultContextSnapshot(
        bool activeWindowExpired,
        Guid? parentResultSetId = null,
        Guid? branchRootResultSetId = null)
    {
        var resultSetId = Guid.NewGuid();
        return new ResultContextSnapshot(
            ResultSetId: resultSetId,
            ParentResultSetId: parentResultSetId,
            BranchRootResultSetId: branchRootResultSetId ?? resultSetId,
            SourceMode: ConversationMode.Exploration,
            SourceSubtype: ExplorationSubtype.Structured,
            QueryFingerprint: "query-fingerprint",
            NormalizedConstraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ConversationConstraintKeys.ExplorationPlaceTypes] = "restaurant",
                [ConversationConstraintKeys.ExplorationArea] = "Dublin 2"
            },
            SuggestedEntities:
            [
                new ResultContextEntity("place-1", "Place One", 1),
                new ResultContextEntity("place-2", "Place Two", 2)
            ],
            SelectedEntityId: null,
            ActiveUntilUtc: activeWindowExpired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(20),
            ExpiresUtc: DateTime.UtcNow.AddHours(6),
            IsExpired: false,
            IsActiveWindowExpired: activeWindowExpired);
    }

    private static ConversationResultContextReference CreateResultContextReference()
    {
        return new ConversationResultContextReference(
            ActiveResultSetId: Guid.NewGuid(),
            BranchRootResultSetId: Guid.NewGuid(),
            ActiveUntilUtc: DateTime.UtcNow.AddMinutes(20),
            ExpiresUtc: DateTime.UtcNow.AddHours(6));
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
                                  Confidence: 0.5d,
                                  ToolPathEligible: false,
                                  PrimaryWhy: "Stub open subtype.",
                                  MissingConstraints: [],
                                  ReasonCodes: ["stub_open"]),
                    ModelSelection: modelSelection,
                    Route: null,
                    UsedModelInvocation: false));
        }
    }

    private sealed class StubConversationDecisionPromptBuilder : IConversationDecisionPromptBuilder
    {
        public PromptBuildResult BuildPrompt(ConversationDecisionPromptInput input)
        {
            return new PromptBuildResult(
                SystemInstructions: null,
                Messages: [AIMessage.User("stub")],
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
                Messages: [AIMessage.User("stub")],
                StructuredSchemaName: "stub",
                ReasonCodes: ["stub_prompt"]);
        }
    }

    private sealed class FailingConversationDecisionParser : IConversationDecisionParser
    {
        public bool TryParse(
            AIResponse response,
            AIModelRoute route,
            ConversationStateSnapshot currentState,
            out ConversationTurnStrategyDecision? decision,
            out IReadOnlyList<string> reasonCodes,
            out string? failureReason)
        {
            decision = null;
            reasonCodes = ["stub_parse_failed"];
            failureReason = "stub_parse_failed";
            return false;
        }
    }

    private sealed class FailingExplorationSubtypeDecisionParser : IExplorationSubtypeDecisionParser
    {
        public bool TryParse(
            AIResponse response,
            AIModelRoute route,
            out ExplorationSubtypeDecision? decision,
            out IReadOnlyList<string> reasonCodes,
            out string? failureReason)
        {
            decision = null;
            reasonCodes = ["stub_parse_failed"];
            failureReason = "stub_parse_failed";
            return false;
        }
    }

    private sealed class StubAIClient : IAIClient
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
}
