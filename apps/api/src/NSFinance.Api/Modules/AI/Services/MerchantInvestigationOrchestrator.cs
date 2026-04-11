using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class MerchantInvestigationOrchestrator(
    MerchantDescriptorNormalizer merchantDescriptorNormalizer,
    IAIModelRouter modelRouter,
    IPromptBuilder promptBuilder,
    IAIClient aiClient,
    IMerchantInvestigationResponseParser responseParser,
    ILogger<MerchantInvestigationOrchestrator> logger) : IMerchantInvestigationOrchestrator
{
    public async Task<MerchantInvestigationResult> InvestigateAsync(
        MerchantInvestigationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sanitizedDescriptor = merchantDescriptorNormalizer.SanitizeForStorage(request.RawDescriptor);
        var normalizedDescriptor = merchantDescriptorNormalizer.Normalize(request.NormalizedDescriptor);
        var correlationId = Guid.NewGuid().ToString("N");

        var prompt = promptBuilder.BuildMerchantInvestigationPrompt(
            new MerchantInvestigationPromptInput(
                RawDescriptor: sanitizedDescriptor,
                NormalizedDescriptor: normalizedDescriptor,
                TriggerSource: request.TriggerSource,
                CorrelationId: correlationId,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["triggerSource"] = request.TriggerSource,
                    ["normalizedDescriptor"] = normalizedDescriptor
                }));

        var route = modelRouter.Resolve(
            AITaskType.MerchantInvestigation,
            AIModelClass.HeavyReasoning,
            complexityHint: "merchant_investigation");

        if (route.Reason == "heavy_model_disabled_fail_fast")
        {
            logger.LogWarning(
                "Merchant investigation routing blocked by heavy model policy correlationId={CorrelationId}",
                correlationId);

            return new MerchantInvestigationResult(
                Succeeded: false,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: "Heavy reasoning model required but unavailable.");
        }

        var aiRequest = AIRequest.Create(
            taskType: AITaskType.MerchantInvestigation,
            preferredModelClass: AIModelClass.HeavyReasoning,
            messages: prompt.Messages,
            correlationId: correlationId,
            systemInstructions: prompt.SystemInstructions,
            structuredOutputSchemaName: prompt.StructuredSchemaName,
            temperature: 0.1d,
            maxOutputTokens: 1000,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["triggerSource"] = request.TriggerSource,
                ["normalizedDescriptor"] = normalizedDescriptor
            });

        var response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        if (responseParser.TryParse(response, out var parsedResult, out var parseReasonCodes))
        {
            logger.LogInformation(
                "Merchant investigation succeeded correlationId={CorrelationId} routeModel={Model} routeDeployment={Deployment} candidates={CandidateCount} insufficientEvidence={InsufficientEvidence} parseReasonCodes={ReasonCodes}",
                correlationId,
                route.Model,
                route.Deployment,
                parsedResult.Candidates.Count,
                parsedResult.InsufficientEvidence,
                string.Join(',', parseReasonCodes));

            return parsedResult;
        }

        logger.LogWarning(
            "Merchant investigation parse failure correlationId={CorrelationId} routeModel={Model} routeDeployment={Deployment} parseReasonCodes={ReasonCodes} failureReason={FailureReason}",
            correlationId,
            route.Model,
            route.Deployment,
            string.Join(',', parseReasonCodes),
            parsedResult.FailureReason);

        return parsedResult;
    }
}
