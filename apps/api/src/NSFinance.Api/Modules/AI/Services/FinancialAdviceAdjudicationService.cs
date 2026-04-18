using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceAdjudicationExecutionRequest(
    string UserQuery,
    FinancialCompanionIntent Intent,
    UserFinancialContextSnapshot Profile,
    FinancialAdviceAdjudicationPlan Plan,
    IReadOnlyList<FinancialAdvicePolicyReviewedFinding> PolicyReviewedFindings,
    IReadOnlyList<string> EvidenceSummary,
    AIModelClass PreferredModelClass,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IFinancialAdviceAdjudicationService
{
    Task<FinancialAdviceAdjudicationResult> AdjudicateAsync(
        FinancialAdviceAdjudicationExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed class FinancialAdviceAdjudicationService(
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IAdjudicationPromptBuilder promptBuilder,
    IAdjudicationInputSanitizer inputSanitizer,
    IAdjudicationResultParser resultParser,
    IAdjudicationResultValidator resultValidator,
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceAdjudicationService
{
    private readonly CompanionAdviceOptions adviceOptions = options.Value;

    public async Task<FinancialAdviceAdjudicationResult> AdjudicateAsync(
        FinancialAdviceAdjudicationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Plan.Mode == FinancialAdviceAdjudicationMode.Skipped
            || request.Plan.TargetFindingIds.Count == 0)
        {
            return BuildSkippedResult(request.Plan.ReasonCodes);
        }

        var targetFindings = SelectTargetFindings(request);
        if (targetFindings.Count == 0)
        {
            return BuildSkippedResult(["adjudication_skipped_no_eligible_findings"]);
        }

        var inputPacket = promptBuilder.BuildInputPacket(request, targetFindings);
        var sanitizedInput = inputSanitizer.Sanitize(inputPacket, adviceOptions.MaxAdjudicationInputChars);

        var route = modelRouter.Resolve(
            taskType: AITaskType.FinancialReasoning,
            preferredModelClass: request.PreferredModelClass,
            complexityHint: $"adjudication:{request.Intent}:{request.Plan.Mode}");
        var aiRequest = AIRequest.Create(
            taskType: AITaskType.FinancialReasoning,
            preferredModelClass: route.ModelClass,
            messages:
            [
                AIMessage.User(promptBuilder.BuildUserPrompt(sanitizedInput.PacketJson))
            ],
            correlationId: request.CorrelationId,
            systemInstructions: promptBuilder.BuildSystemInstructions(),
            structuredOutputSchemaName: "financial_advice_adjudication_v1",
            temperature: 0.1d,
            maxOutputTokens: Math.Clamp(adviceOptions.MaxAdjudicationOutputTokens, 120, 800),
            metadata: request.Metadata);

        var aiResponse = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        if (!aiResponse.Succeeded)
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: true,
                Succeeded: false,
                Mode: request.Plan.Mode,
                ModelUsed: route.Model,
                InputTokens: aiResponse.InputTokenEstimate ?? 0,
                OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: ["adjudication_ai_request_failed", aiResponse.FailureReason ?? "unknown_ai_failure"]);
        }

        var payload = aiResponse.StructuredPayloadJson ?? aiResponse.Content;
        var parsed = resultParser.Parse(payload ?? string.Empty);
        if (parsed is null)
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: true,
                Succeeded: false,
                Mode: request.Plan.Mode,
                ModelUsed: route.Model,
                InputTokens: aiResponse.InputTokenEstimate ?? 0,
                OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: string.IsNullOrWhiteSpace(payload)
                    ? ["adjudication_empty_payload"]
                    : ["adjudication_parse_failed"]);
        }

        var validation = resultValidator.Validate(
            new FinancialAdviceAdjudicationValidationRequest(
                Response: parsed,
                TargetFindings: targetFindings,
                EvidenceSummary: request.EvidenceSummary,
                PlanReasonCodes: request.Plan.ReasonCodes));

        return new FinancialAdviceAdjudicationResult(
            UsedAi: true,
            Succeeded: true,
            Mode: request.Plan.Mode,
            ModelUsed: route.Model,
            InputTokens: aiResponse.InputTokenEstimate ?? 0,
            OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
            ResponseSummary: validation.ResponseSummary,
            FindingOutcomes: validation.FindingOutcomes,
            Warnings: validation.Warnings);
    }

    private IReadOnlyList<FinancialAdviceFinding> SelectTargetFindings(
        FinancialAdviceAdjudicationExecutionRequest request)
    {
        return request.PolicyReviewedFindings
            .Where(item => request.Plan.TargetFindingIds.Contains(item.Finding.FindingId, StringComparer.Ordinal))
            .Where(item => item.Decision != FinancialAdvicePolicyDecision.Rejected
                           && item.Finding.AiAdjudicationAllowed)
            .Select(item => item.Finding)
            .Take(Math.Clamp(adviceOptions.MaxAdjudicatedFindings, 1, 6))
            .ToArray();
    }

    private static FinancialAdviceAdjudicationResult BuildSkippedResult(IReadOnlyList<string> reasonCodes)
    {
        return new FinancialAdviceAdjudicationResult(
            UsedAi: false,
            Succeeded: true,
            Mode: FinancialAdviceAdjudicationMode.Skipped,
            ModelUsed: "deterministic_only",
            InputTokens: 0,
            OutputTokens: 0,
            ResponseSummary: null,
            FindingOutcomes: [],
            Warnings: reasonCodes);
    }
}
