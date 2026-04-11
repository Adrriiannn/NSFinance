using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record MerchantInvestigationPromptInput(
    string RawDescriptor,
    string NormalizedDescriptor,
    string TriggerSource,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record UserChatPromptInput(
    UserChatRequest ChatRequest,
    ConversationContextBuildResult Context,
    UserChatComplexityEvaluation ComplexityEvaluation);

public sealed record PromptBuildResult(
    string? SystemInstructions,
    IReadOnlyList<AIMessage> Messages,
    string StructuredSchemaName,
    IReadOnlyList<string> ReasonCodes);

public interface IPromptBuilder
{
    PromptBuildResult BuildMerchantInvestigationPrompt(MerchantInvestigationPromptInput input);
    PromptBuildResult BuildUserChatPrompt(UserChatPromptInput input);
}
