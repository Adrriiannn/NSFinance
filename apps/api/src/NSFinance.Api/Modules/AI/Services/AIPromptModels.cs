using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record MerchantInvestigationPromptInput(
    string RawDescriptor,
    string NormalizedDescriptor,
    string TriggerSource,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ConversationDecisionPromptInput(
    UserChatRequest ChatRequest,
    IReadOnlyList<AIMessage> ContextMessages,
    string? ContextSummary,
    ConversationStateSnapshot State,
    ResultContextSnapshot? ResultContext,
    IReadOnlyDictionary<string, string> ClientMetadata,
    IReadOnlyList<string> FailureHistory);

public sealed record ResponseCompositionPromptInput(
    ResponseCompositionRequest Request,
    string CorrelationId);

public sealed record ExplorationSubtypePromptInput(
    UserChatRequest ChatRequest,
    ConversationStateSnapshot State,
    ResultContextSnapshot? ResultContext,
    IReadOnlyDictionary<string, string> ClientMetadata);

public sealed record PromptBuildResult(
    string? SystemInstructions,
    IReadOnlyList<AIMessage> Messages,
    string StructuredSchemaName,
    IReadOnlyList<string> ReasonCodes);

public interface IMerchantInvestigationPromptBuilder
{
    PromptBuildResult BuildPrompt(MerchantInvestigationPromptInput input);
}

public interface IConversationDecisionPromptBuilder
{
    PromptBuildResult BuildPrompt(ConversationDecisionPromptInput input);
}

public interface IResponseCompositionPromptBuilder
{
    PromptBuildResult BuildPrompt(ResponseCompositionPromptInput input);
}

public interface IExplorationSubtypePromptBuilder
{
    PromptBuildResult BuildPrompt(ExplorationSubtypePromptInput input);
}
