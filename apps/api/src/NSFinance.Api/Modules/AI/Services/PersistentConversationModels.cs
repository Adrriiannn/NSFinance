using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record ConversationMessageAppendRequest(
    ConversationMessageRole Role,
    string Content,
    Guid? ConversationTurnId = null,
    string? Topic = null,
    bool IsResolved = false,
    bool WasTrimEligible = true,
    bool WasSummaryDerived = false,
    string? ModelUsed = null,
    string? TaskType = null,
    string? CorrelationId = null);

public sealed record ConversationTurnStartResult(
    ConversationTurn Turn,
    bool IsDuplicateRequest,
    bool IsNewTurn);

public sealed record ConversationTurnTransitionResult(
    ConversationTurn Turn,
    ConversationTurnStatus PreviousStatus,
    ConversationTurnStatus CurrentStatus);

public sealed record PersistedConversationState(
    ConversationStateSnapshot State,
    int StateVersion,
    DateTime CreatedUtc);

public sealed record ConversationSummaryRefreshResult(
    bool Refreshed,
    int MessageCount,
    int MessagesSinceLastSummary,
    int EstimatedTokenCount,
    ConversationSummary? LatestSummary,
    IReadOnlyList<string> ReasonCodes);

public sealed record PersistentConversationContextBuildRequest(
    Guid UserId,
    Guid ConversationThreadId,
    AITaskType TaskType,
    AIModelClass ModelClass,
    string CorrelationId,
    Guid? ConversationTurnId,
    string? CurrentUserMessage,
    bool IncludeCurrentUserMessage = false,
    int? MaxPromptTokensOverride = null);

public sealed record PersistentConversationContextBuildResult(
    IReadOnlyList<AIMessage> ContextMessages,
    IReadOnlyList<Guid> IncludedMessageIds,
    IReadOnlyList<Guid> ExcludedMessageIds,
    string? ContextSummary,
    int? IncludedSummaryVersion,
    int? IncludedStateVersion,
    IReadOnlyDictionary<string, string> StructuredState,
    int EstimatedPromptTokenCount,
    string? TrimReason,
    IReadOnlyList<string> ReasonCodes);

public interface IConversationThreadService
{
    Task<ConversationThread> CreateThreadAsync(Guid userId, string? title, CancellationToken cancellationToken);
    Task<ConversationThread?> GetThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationThread>> GetRecentThreadsAsync(Guid userId, int limit, CancellationToken cancellationToken);
    Task ArchiveThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken);
    Task CloseThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken);
    Task TouchThreadAsync(Guid userId, Guid threadId, DateTime timestampUtc, CancellationToken cancellationToken);
}

public interface IConversationTurnService
{
    Task<ConversationTurnStartResult> StartOrGetAsync(
        Guid userId,
        Guid conversationThreadId,
        string clientRequestId,
        string correlationId,
        AITaskType taskType,
        AIModelClass modelClass,
        CancellationToken cancellationToken);

    Task<ConversationTurn?> GetTurnAsync(Guid userId, Guid conversationThreadId, Guid turnId, CancellationToken cancellationToken);
    Task<ConversationTurnTransitionResult> MarkPersistedUserTurnAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        Guid userMessageId,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkContextBuiltAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string contextSource,
        int estimatedPromptTokens,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkAIInProgressAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        AIModelRoute route,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> ApplyResolvedRouteAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        AIModelRoute route,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkAICompletedAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        long responseLatencyMs,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkPersistedAssistantTurnAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        Guid assistantMessageId,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkCompletedAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkFailedAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkTimedOutAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken);

    Task<ConversationTurnTransitionResult> MarkCancelledAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken);
}

public interface IConversationMessageService
{
    Task<ConversationMessage> AppendMessageAsync(
        Guid userId,
        Guid conversationThreadId,
        ConversationMessageAppendRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid userId,
        Guid conversationThreadId,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationMessage>> GetMessagesRangeAsync(
        Guid userId,
        Guid conversationThreadId,
        int startOrder,
        int endOrder,
        CancellationToken cancellationToken);

    Task<ConversationMessage?> GetMessageByIdAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid messageId,
        CancellationToken cancellationToken);
}

public interface IConversationStateService
{
    Task<PersistedConversationState?> GetLatestStateAsync(Guid userId, Guid conversationThreadId, CancellationToken cancellationToken);
    Task<PersistedConversationState> SaveSnapshotAsync(
        Guid userId,
        Guid conversationThreadId,
        ConversationStateSnapshot state,
        ConversationStateSnapshotReason reason,
        CancellationToken cancellationToken);

    Task<PersistedConversationState> MergeStateUpdatesAsync(
        Guid userId,
        Guid conversationThreadId,
        IReadOnlyDictionary<string, string> updates,
        ConversationStateSnapshotReason reason,
        CancellationToken cancellationToken);
}

public interface IConversationSummaryGenerator
{
    string GenerateSummary(
        IReadOnlyList<ConversationMessage> messages,
        string? previousSummary,
        int maxSummaryLengthChars);
}

public interface IConversationSummaryService
{
    Task<ConversationSummary?> GetLatestSummaryAsync(Guid userId, Guid conversationThreadId, CancellationToken cancellationToken);
    Task<ConversationSummaryRefreshResult> RefreshSummaryIfNeededAsync(
        Guid userId,
        Guid conversationThreadId,
        AITaskType taskType,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface IPersistentConversationContextService
{
    Task<PersistentConversationContextBuildResult> BuildContextAsync(
        PersistentConversationContextBuildRequest request,
        CancellationToken cancellationToken);
}
