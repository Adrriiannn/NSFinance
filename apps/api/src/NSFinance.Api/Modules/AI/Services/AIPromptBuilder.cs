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
            Never return prose outside JSON.
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

        sb.AppendLine("Return JSON with this exact top-level shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"overallConfidence\": number(0..1),");
        sb.AppendLine("  \"ambiguityLevel\": number(0..1),");
        sb.AppendLine("  \"recommendation\": one of [\"accept_candidate\",\"accept_cautiously\",\"unresolved\",\"insufficient_evidence\",\"conflicting_candidates\"],");
        sb.AppendLine("  \"summary\": non-empty string,");
        sb.AppendLine("  \"candidates\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"canonicalName\": required string,");
        sb.AppendLine("      \"displayName\": optional string,");
        sb.AppendLine("      \"merchantType\": required enum string,");
        sb.AppendLine("      \"merchantUsageType\": required enum string,");
        sb.AppendLine("      \"confidence\": number(0..1),");
        sb.AppendLine("      \"descriptorMatchStrength\": number(0..1),");
        sb.AppendLine("      \"entityMatchStrength\": number(0..1),");
        sb.AppendLine("      \"mixedUseRisk\": bool,");
        sb.AppendLine("      \"whyItMayMatch\": required string,");
        sb.AppendLine("      \"whyItMayBeWrong\": required string,");
        sb.AppendLine("      \"likelyOfficialWebsite\": optional string,");
        sb.AppendLine("      \"parentBrand\": optional string,");
        sb.AppendLine("      \"businessSummary\": optional string,");
        sb.AppendLine("      \"supportsSubscriptions\": optional bool,");
        sb.AppendLine("      \"supportsRecurringPayments\": optional bool,");
        sb.AppendLine("      \"supportsOneTimePurchases\": optional bool,");
        sb.AppendLine("      \"supportsMarketplacePayments\": optional bool,");
        sb.AppendLine("      \"supportsInAppPurchases\": optional bool,");
        sb.AppendLine("      \"likelyCategoryFamilies\": optional string[],");
        sb.AppendLine("      \"aliasCandidates\": optional string[],");
        sb.AppendLine("      \"aliasSuggestions\": optional [{\"aliasText\": string, \"aliasType\": string, \"confidence\": number(0..1), \"notes\": optional string, \"isPreferred\": optional bool}],");
        sb.AppendLine("      \"evidenceItems\": optional [{\"evidenceType\": enum string, \"sourceClass\": string, \"summary\": string, \"confidence\": number(0..1), \"relevance\": number(0..1), \"sourceReference\": optional string}]");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"aliasSuggestions\": [{\"aliasText\": string, \"aliasType\": string, \"confidence\": number(0..1), \"notes\": optional string, \"isPreferred\": optional bool}],");
        sb.AppendLine("  \"evidence\": [{\"evidenceType\": enum string, \"sourceClass\": string, \"summary\": string, \"confidence\": number(0..1), \"relevance\": number(0..1), \"sourceReference\": optional string}]");
        sb.AppendLine("}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Output must be strict valid JSON only.");
        sb.AppendLine("- Do not include fields outside this schema.");
        sb.AppendLine("- Do not use broad dangerous aliases such as single-token amazon/google/apple/microsoft/paypal.");
        sb.AppendLine("- If uncertain, return recommendation=insufficient_evidence or unresolved.");

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
