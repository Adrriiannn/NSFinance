using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record ConversationMessageAppendRequest(
    ConversationMessageRole Role,
    string Content,
    string? Topic = null,
    bool IsResolved = false,
    bool WasTrimEligible = true,
    bool WasSummaryDerived = false,
    string? ModelUsed = null,
    string? TaskType = null,
    string? CorrelationId = null);

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
