using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record UserChatTurn(
    AIMessageRole Role,
    string Content,
    DateTime TimestampUtc,
    string? Topic = null,
    bool IsResolved = false);

public sealed record ConversationStateSnapshot(
    string? ActiveTopic,
    string? UserIntent,
    IReadOnlyDictionary<string, string> Constraints,
    IReadOnlyList<string> Summaries,
    string? BudgetPreference,
    string? LocationPreference,
    string? MerchantInvestigationSubject,
    IReadOnlyList<string> RecentConclusions,
    int SchemaVersion = 2,
    ConversationMode? ActiveMode = ConversationMode.Conversation,
    ConversationMode? ModeCandidate = ConversationMode.Conversation,
    ConversationReadinessLevel ReadinessLevel = ConversationReadinessLevel.R0_Unknown,
    string? InferredIntent = null,
    IReadOnlyList<string>? MissingConstraints = null,
    ConversationSignals? ConversationSignals = null,
    string? LastClarificationPrompt = null,
    IReadOnlyList<string>? LastSuggestedOptions = null,
    IReadOnlyList<ConversationSuggestedEntity>? LastSuggestedEntities = null,
    string? SelectedEntityId = null,
    string? LastExecutionFingerprint = null,
    string? TopicStatus = null,
    string? TransitionIntent = null,
    double? Confidence = null,
    bool NeedsFollowUp = false,
    FollowUpBindingType FollowUpBindingType = FollowUpBindingType.None,
    ConversationResultContextReference? ResultContextRef = null,
    ConversationLoopGuards? LoopGuards = null);

public sealed record UserChatRequest(
    string UserMessage,
    IReadOnlyList<UserChatTurn> RecentTurns,
    ConversationStateSnapshot? State,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? ClientRequestId = null,
    Guid? UserId = null,
    Guid? ConversationThreadId = null,
    bool UsePersistentMemory = false,
    bool AllowTransientFallbackOnPersistentFailure = false);

public sealed record UserChatResponse(
    string ReplyText,
    string ModelUsed,
    AIModelClass ReasoningClass,
    IReadOnlyDictionary<string, string> SuggestedStructuredStateUpdates,
    string? ReferencedContextSummary,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints,
    bool Succeeded,
    string? FailureReason,
    Guid? ConversationThreadId = null,
    Guid? ConversationTurnId = null,
    ConversationTurnStatus? TurnStatus = null,
    bool IsDuplicateRequest = false,
    bool IsTurnInProgress = false);
