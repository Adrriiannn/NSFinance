namespace NSFinance.Api.Modules.AI.Services;

public sealed record ConversationContextBuildRequest(
    AITaskType TaskType,
    IReadOnlyList<UserChatTurn> RecentTurns,
    ConversationStateSnapshot? State,
    string? CurrentUserMessage,
    string CorrelationId);

public sealed record ConversationContextBuildResult(
    IReadOnlyList<AIMessage> ContextMessages,
    IReadOnlyList<UserChatTurn> IncludedTurns,
    IReadOnlyList<UserChatTurn> ExcludedTurns,
    string? ContextSummary,
    IReadOnlyDictionary<string, string> StructuredState,
    IReadOnlyList<string> ReasonCodes);

public interface IConversationContextService
{
    ConversationContextBuildResult BuildContext(ConversationContextBuildRequest request);
}
