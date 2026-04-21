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
    bool HasExplicitLocation = false);

public sealed record ConversationLoopGuards(
    int SameClarificationIntentCount = 0,
    int StrategicQuestionCountThisTurn = 0,
    int ConsecutiveNoProgressTurns = 0,
    string? LastClarificationFingerprint = null);

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
    CancellationToken CancellationToken);

public sealed record ConversationBehaviorResult(
    ConversationTurnStrategyDecision StrategyDecision,
    ConversationStateSnapshot State,
    bool RouteToModeHandler,
    bool StayInDirectMode,
    ConversationMode TargetMode,
    ExplorationSubtypeDecision? ExplorationSubtypeDecision,
    ResponseCompositionRequest? CompositionRequest,
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
    IReadOnlyDictionary<string, string> ClientMetadata);

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
    IReadOnlyList<string>? SuggestedOptions = null);

public sealed record ResponseCompositionResult(
    string ReplyText,
    IReadOnlyDictionary<string, string> SuggestedStructuredStateUpdates,
    string ModelUsed,
    string DeploymentUsed,
    AIModelClass ReasoningClass,
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
