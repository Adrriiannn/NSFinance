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
    IReadOnlyList<string> RecentConclusions);

public sealed record UserChatRequest(
    string UserMessage,
    IReadOnlyList<UserChatTurn> RecentTurns,
    ConversationStateSnapshot? State,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record UserChatResponse(
    string ReplyText,
    string ModelUsed,
    AIModelClass ReasoningClass,
    IReadOnlyDictionary<string, string> SuggestedStructuredStateUpdates,
    string? ReferencedContextSummary,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints,
    bool Succeeded,
    string? FailureReason);
