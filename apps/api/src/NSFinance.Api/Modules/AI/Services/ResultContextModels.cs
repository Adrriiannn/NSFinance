namespace NSFinance.Api.Modules.AI.Services;

public sealed record ResultContextEntity(
    string EntityId,
    string Label,
    int Rank,
    string? StableReference = null,
    string? Category = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record ResultContextSnapshot(
    Guid ResultSetId,
    Guid? ParentResultSetId,
    Guid BranchRootResultSetId,
    ConversationMode SourceMode,
    ExplorationSubtype SourceSubtype,
    string QueryFingerprint,
    IReadOnlyDictionary<string, string> NormalizedConstraints,
    IReadOnlyList<ResultContextEntity> SuggestedEntities,
    string? SelectedEntityId,
    DateTime ActiveUntilUtc,
    DateTime ExpiresUtc,
    bool IsExpired,
    bool IsActiveWindowExpired);

public sealed record ResultContextReadRequest(
    Guid UserId,
    Guid ConversationThreadId,
    string UserMessage,
    ConversationStateSnapshot State,
    IReadOnlyDictionary<string, string> ClientMetadata);

public sealed record ResultContextReadResult(
    ResultContextSnapshot? ActiveResultContext,
    ResultContextBindingClassification BindingClassification,
    bool UsedClientResultSetId,
    bool ExpiredBindingCleared,
    IReadOnlyList<string> ReasonCodes);

public sealed record ResultContextWriteRequest(
    Guid UserId,
    Guid ConversationThreadId,
    ConversationMode SourceMode,
    ExplorationSubtype SourceSubtype,
    string QueryFingerprint,
    IReadOnlyDictionary<string, string> NormalizedConstraints,
    IReadOnlyList<ResultContextEntity> SuggestedEntities,
    string? SelectedEntityId,
    Guid? ParentResultSetId,
    Guid? BranchRootResultSetId,
    DateTime CreatedUtc);

public sealed record ResultContextWriteResult(
    ResultContextSnapshot Snapshot,
    ConversationResultContextReference Reference,
    IReadOnlyList<string> ReasonCodes);

public interface IResultContextService
{
    Task<ResultContextReadResult> ReadAsync(
        ResultContextReadRequest request,
        CancellationToken cancellationToken);

    Task<ResultContextWriteResult> WriteAsync(
        ResultContextWriteRequest request,
        CancellationToken cancellationToken);

    Task<ResultContextWriteResult?> TrySelectEntityAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid resultSetId,
        string selectedEntityId,
        CancellationToken cancellationToken);

    Task ClearExpiredBindingsAsync(
        Guid conversationThreadId,
        CancellationToken cancellationToken);
}
