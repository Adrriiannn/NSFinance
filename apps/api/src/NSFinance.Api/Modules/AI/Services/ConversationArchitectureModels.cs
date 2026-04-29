namespace NSFinance.Api.Modules.AI.Services;

public enum ConversationMode
{
    Conversation = 0,
    Exploration = 1,
    Financial = 2,
    GeneralKnowledge = 3
}

public enum ExplorationSubtype
{
    None = 0,
    Structured = 1,
    Open = 2
}

public enum ConversationReadinessLevel
{
    R0_Unknown = 0,
    R1_Vague = 1,
    R2_DirectionKnown = 2,
    R3_StructuredIncomplete = 3,
    R4_ToolReady = 4
}

public enum ConversationBehaviorStrategy
{
    DirectAnswer = 0,
    SuggestAndClarify = 1,
    ClarifyOnly = 2,
    AcknowledgeAndGuide = 3,
    ContinueRefinementThread = 4,
    ConfirmAndTransition = 5,
    RefinePriorResultSet = 6,
    ToolReadyHandoff = 7,
    FinancialPlaceholderTransition = 8,
    GeneralGuidance = 9
}

public enum ToolExecutionPermission
{
    Forbidden = 0,
    EligibleIfGuardPasses = 1
}

public enum FollowUpBindingType
{
    None = 0,
    BindPrior = 1,
    Refine = 2,
    NewBranch = 3,
    NewTopic = 4
}

public enum ClarificationSlot
{
    None = 0,
    ExplorationLocation = 1,
    ExplorationPlaceType = 2,
    ExplorationRefinement = 3,
    FinancialFocus = 4
}

public enum ResponseCompositionType
{
    Direct = 0,
    Clarify = 1,
    Suggest = 2,
    ResultSummary = 3,
    Comparison = 4,
    Fallback = 5,
    Placeholder = 6
}

public enum ResponseToneDirective
{
    Neutral = 0,
    Supportive = 1,
    Concise = 2
}

public enum ResultContextBindingClassification
{
    None = 0,
    BindPrior = 1,
    Refine = 2,
    NewBranch = 3,
    NewTopic = 4
}

public static class ConversationConstraintKeys
{
    public const string SemanticFamily = "semantic_family";
    public const string ExplorationSubtype = "exploration_subtype";
    public const string ExplorationArea = "exploration_area";
    public const string ExplorationAreaLastUsedUtc = "exploration_area_last_used_utc";
    public const string ExplorationAreaExpiresUtc = "exploration_area_expires_utc";
    public const string ExplorationPlaceTypes = "exploration_place_types";
    public const string ExplorationExcludeTypes = "exploration_exclude_types";
    public const string ExplorationPreferences = "exploration_preferences";
    public const string ExplorationTime = "exploration_time";
    public const string ExplorationAudience = "exploration_audience";
    public const string ExplorationBrandTerm = "exploration_brand_term";
    public const string ExplorationCanonicalConcept = "exploration_canonical_concept";
    public const string ExplorationContextLastUsedUtc = "exploration_context_last_used_utc";
    public const string FinancialFocus = "financial_focus";
}

public static class ConversationSemanticFamilies
{
    public const string Conversation = "conversation";
    public const string Exploration = "exploration";
    public const string Financial = "financial";
    public const string GeneralKnowledge = "general_knowledge";
}

public static class ConversationTransitionIntents
{
    public const string DirectMode = "direct_mode";
    public const string ToolReadyHandoff = "tool_ready_handoff";
    public const string ConfirmAndTransition = "confirm_and_transition";
    public const string FinancialTransition = "financial_transition";
    public const string FinancialValidationPending = "financial_validation_pending";
    public const string RefinePriorResults = "refine_prior_results";
    public const string RefineCurrentBranch = "refine_current_branch";
    public const string NewBranch = "new_branch";
    public const string TopicSwitch = "topic_switch";
    public const string Correction = "correction";
}

public sealed record ReadinessTransition(
    ConversationReadinessLevel From,
    ConversationReadinessLevel To);

public sealed record ConversationTurnStrategyDecision(
    ConversationBehaviorStrategy Strategy,
    ConversationMode ModeCandidate,
    ReadinessTransition Readiness,
    double Confidence,
    FollowUpBindingType FollowUpBindingType,
    string? ClarificationQuestion,
    IReadOnlyList<string> SuggestedOptions,
    ToolExecutionPermission ToolExecutionPermission,
    IReadOnlyList<string> ReasonCodes);

public sealed record ExplorationSubtypeDecision(
    ExplorationSubtype Subtype,
    double Confidence,
    bool ToolPathEligible,
    string PrimaryWhy,
    IReadOnlyList<string> MissingConstraints,
    IReadOnlyList<string> ReasonCodes);

public sealed record ExplorationSubtypeResolutionPlan(
    bool RequiresModelReasoning,
    ExplorationSubtypeDecision? Decision,
    string ResolutionSource,
    IReadOnlyList<string> ReasonCodes);

public enum ConversationModelSelectionKind
{
    Deterministic = 0,
    Fast = 1,
    HeavyReasoning = 2
}

public sealed record ConversationModelSelectionPlan(
    ConversationModelSelectionKind SelectionKind,
    AIModelClass? ModelClass,
    string SelectionReason,
    string? EscalationJustification,
    bool CouldAvoidEscalation,
    IReadOnlyList<string> ReasonCodes);

public sealed record ConversationDecisionEvaluationResult(
    ConversationTurnStrategyDecision Decision,
    ConversationModelSelectionPlan ModelSelection,
    AIModelRoute? Route,
    bool UsedModelInvocation);

public sealed record ExplorationSubtypeEvaluationResult(
    ExplorationSubtypeDecision Decision,
    ConversationModelSelectionPlan ModelSelection,
    AIModelRoute? Route,
    bool UsedModelInvocation);

public sealed record ConversationSignals(
    bool HasEmotionalFraming,
    bool HasSubjectiveLanguage,
    bool HasCorrectionSignal,
    bool HasTopicSwitchSignal,
    bool HasFinancialSignal,
    bool HasExplorationSignal,
    bool HasFactualQuestion,
    bool HasCompleteQuestion,
    bool HasConcretePlaceSignal = false,
    bool HasExplicitLocation = false,
    bool HasExplicitConfirmation = false,
    bool HasFinancialFocusSelection = false,
    bool HasAtmosphericExplorationIntent = false,
    bool HasSafetyExplorationIntent = false,
    bool HasStructuredExplorationIntent = false,
    bool HasResultReferenceSignal = false,
    bool HasBranchingSignal = false,
    bool HasComparisonSignal = false);

public sealed record ConversationLoopGuards(
    int SameClarificationIntentCount = 0,
    int StrategicQuestionCountThisTurn = 0,
    int ConsecutiveNoProgressTurns = 0,
    string? LastClarificationFingerprint = null);

public sealed record PendingClarificationState(
    ClarificationSlot Slot,
    string? PromptIntent = null,
    string? KnownPlaceTypes = null,
    string? KnownArea = null,
    string? KnownTime = null,
    DateTimeOffset? CreatedAtUtc = null);

public sealed record ConversationSuggestedEntity(
    string EntityId,
    string Label,
    int Rank,
    string? StableReference = null);

public sealed record ConversationResultContextReference(
    Guid? ActiveResultSetId,
    Guid? BranchRootResultSetId,
    DateTime? ActiveUntilUtc,
    DateTime? ExpiresUtc);

public sealed record ConversationBehaviorRequest(
    UserChatRequest Request,
    IReadOnlyList<AIMessage> ContextMessages,
    string? ContextSummary,
    ConversationStateSnapshot EffectiveState,
    ResultContextSnapshot? ResultContext,
    ResultContextReadResult? ResultContextReadResult,
    IReadOnlyDictionary<string, string> ClientMetadata,
    IReadOnlyList<string> FailureHistory,
    CancellationToken CancellationToken,
    ConversationIntelligenceResult? ConversationIntelligence = null);

public sealed record ConversationBehaviorResult(
    ConversationTurnStrategyDecision StrategyDecision,
    ConversationStateSnapshot State,
    bool RouteToModeHandler,
    bool StayInDirectMode,
    ConversationMode TargetMode,
    ExplorationSubtypeDecision? ExplorationSubtypeDecision,
    ResponseCompositionRequest? CompositionRequest,
    ConversationModelSelectionPlan PrimaryDecisionModelSelection,
    ConversationModelSelectionPlan? ExplorationSubtypeModelSelection,
    int DecisionModelCallCount,
    int HeavyDecisionModelCallCount,
    int FastDecisionModelCallCount,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings);

public sealed record ConversationModeRequest(
    UserChatRequest Request,
    IReadOnlyList<AIMessage> ContextMessages,
    string? ContextSummary,
    ConversationStateSnapshot State,
    ResultContextSnapshot? ResultContext,
    ConversationTurnStrategyDecision StrategyDecision,
    ExplorationSubtypeDecision? ExplorationSubtypeDecision,
    IReadOnlyDictionary<string, string> ClientMetadata,
    ConversationIntelligenceResult? ConversationIntelligence = null);

public sealed record GroundedDataPoint(
    string Label,
    string Value);

public sealed record GroundedDataEnvelope(
    IReadOnlyList<ConversationSuggestedEntity> Entities,
    IReadOnlyList<GroundedDataPoint> SummaryFacts,
    IReadOnlyList<string> Warnings);

public sealed record ResponseCompositionRequest(
    ResponseCompositionType ResponseType,
    ResponseToneDirective ToneDirective,
    ConversationBehaviorStrategy Strategy,
    ConversationMode Mode,
    ConversationReadinessLevel ReadinessLevel,
    string UserMessage,
    GroundedDataEnvelope GroundedData,
    IReadOnlyDictionary<string, string> Constraints,
    IReadOnlyList<string> MissingConstraints,
    int MaxLengthHint,
    string? ClarificationQuestion = null,
    IReadOnlyList<string>? SuggestedOptions = null,
    ConversationIntelligenceResult? ConversationIntelligence = null,
    TurnInterpretationV2? TurnInterpretation = null,
    PlaceRetrievalPlanV1? RetrievalPlan = null,
    ResultContextSnapshot? ResultContext = null);

public sealed record ConversationIntelligenceResult(
    string ConversationPhase,
    string UserEmotionalState,
    double UserIntentConfidence,
    bool ShouldContinueTask,
    bool ShouldClarify,
    bool ShouldExecuteTool,
    bool ShouldAcknowledgeIssue,
    ConversationResponseStyle ResponseStyle,
    ConversationTaskState TaskState,
    ConversationNextAction NextAction,
    IReadOnlyList<string> ReasonCodes);

public sealed record ConversationResponseStyle(
    string Tone,
    string Verbosity,
    bool AvoidRepetition);

public sealed record ConversationTaskState(
    bool IsNewTask,
    bool IsFollowUp,
    bool IsRefinement,
    bool IsUserCorrection,
    bool TargetPreviousResults);

public sealed record ConversationNextAction(
    string Type,
    string Reason,
    string? Target = null,
    string? Requirement = null);

public sealed record ConversationIntelligenceEvaluationResult(
    ConversationIntelligenceResult Intelligence,
    AIModelRoute? Route,
    bool UsedModelInvocation,
    bool FallbackUsed,
    IReadOnlyList<string> Warnings);

public sealed record ResponseCompositionResult(
    string ReplyText,
    IReadOnlyDictionary<string, string> SuggestedStructuredStateUpdates,
    string ModelUsed,
    string DeploymentUsed,
    AIModelClass ReasoningClass,
    bool UsedModelInvocation,
    bool UsedDeterministicPath,
    bool FallbackUsed,
    string SelectionReason,
    string? RecoveryReason,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints);

public sealed record ConversationModeExecutionResult(
    ResponseCompositionRequest? CompositionRequest,
    string? DeterministicReplyText,
    IReadOnlyDictionary<string, string> SuggestedStructuredStateUpdates,
    ConversationStateSnapshot State,
    ResultContextSnapshot? ResultContext,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints,
    bool Succeeded,
    string? FailureReason = null);

public sealed record ConversationDirectResponse(
    string ReplyText,
    IReadOnlyDictionary<string, string> SuggestedStructuredStateUpdates,
    ConversationStateSnapshot State,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints);
