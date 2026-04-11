using System.Text;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIPromptBuilder : IPromptBuilder
{
    public PromptBuildResult BuildMerchantInvestigationPrompt(MerchantInvestigationPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var systemInstructions = """
            You are a merchant investigation assistant for a financial system.
            Return only strict JSON matching the requested schema.
            Be conservative, avoid unsupported certainty, and clearly surface ambiguity.
            Do not assign final transaction categories; provide merchant intelligence only.
            """;

        var sb = new StringBuilder();
        sb.AppendLine("Investigate this merchant descriptor and produce structured merchant intelligence.");
        sb.AppendLine($"CorrelationId: {input.CorrelationId}");
        sb.AppendLine($"TriggerSource: {input.TriggerSource}");
        sb.AppendLine($"RawDescriptor: {input.RawDescriptor}");
        sb.AppendLine($"NormalizedDescriptor: {input.NormalizedDescriptor}");
        if (input.Metadata is { Count: > 0 })
        {
            foreach (var kvp in input.Metadata.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Metadata.{kvp.Key}: {kvp.Value}");
            }
        }

        sb.AppendLine("Provide candidate merchants, confidence, ambiguity, mixed-use risk, and evidence summaries.");
        sb.AppendLine("If evidence is weak, return insufficient evidence recommendation.");

        return new PromptBuildResult(
            SystemInstructions: systemInstructions,
            Messages: [AIMessage.User(sb.ToString())],
            StructuredSchemaName: "merchant_investigation_v1",
            ReasonCodes: ["merchant_prompt_built"]);
    }

    public PromptBuildResult BuildUserChatPrompt(UserChatPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var systemInstructions = """
            You are an in-app financial assistant.
            Be precise, practical, and safe.
            Do not fabricate external facts.
            Keep recommendations actionable and bounded by provided context.
            Return strict JSON matching the requested schema.
            """;

        var messages = new List<AIMessage>(input.Context.ContextMessages.Count + 2)
        {
            AIMessage.Developer($"Chat complexity: {input.ComplexityEvaluation.Complexity}; reasonCodes={string.Join(',', input.ComplexityEvaluation.ReasonCodes)}")
        };
        messages.AddRange(input.Context.ContextMessages);

        return new PromptBuildResult(
            SystemInstructions: systemInstructions,
            Messages: messages,
            StructuredSchemaName: "user_chat_response_v1",
            ReasonCodes: ["user_chat_prompt_built", $"complexity_{input.ComplexityEvaluation.Complexity.ToString().ToLowerInvariant()}"]);
    }
}
