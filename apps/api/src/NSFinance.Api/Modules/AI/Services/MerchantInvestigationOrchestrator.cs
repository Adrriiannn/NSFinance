using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class MerchantInvestigationOrchestrator(
    MerchantDescriptorNormalizer merchantDescriptorNormalizer,
    IAIModelRouter modelRouter,
    IMerchantInvestigationPromptBuilder promptBuilder,
    IAIClient aiClient,
    IMerchantInvestigationResponseParser responseParser,
    IMerchantInvestigationResultCache resultCache,
    IOptions<AIIntegrationOptions> options,
    IOperationalFailureRecorder failureRecorder,
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
        if (string.IsNullOrWhiteSpace(normalizedDescriptor))
        {
            normalizedDescriptor = merchantDescriptorNormalizer.Normalize(request.RawDescriptor);
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var nowUtc = DateTime.UtcNow;

        if (resultCache.TryGet(normalizedDescriptor, nowUtc, out var cachedResult))
        {
            logger.LogInformation(
                "Merchant investigation cache hit normalizedDescriptor={NormalizedDescriptor} correlationId={CorrelationId} recommendation={Recommendation} succeeded={Succeeded}",
                normalizedDescriptor,
                correlationId,
                cachedResult.Recommendation,
                cachedResult.Succeeded);
            return cachedResult;
        }

        var prompt = promptBuilder.BuildPrompt(
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
            await failureRecorder.RecordAsync(
                new OperationalFailureRecordInput(
                    OperationalFailureArea.MerchantInvestigation,
                    OperationalFailureSeverity.Warning,
                    "routing_heavy_model_unavailable",
                    $"routing_heavy_model_unavailable:{normalizedDescriptor}",
                    correlationId,
                    normalizedDescriptor,
                    "Heavy reasoning model required but unavailable.",
                    "{\"reason\":\"heavy_model_disabled_fail_fast\"}"),
                cancellationToken);

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
            // Reasoning-class models spend completion tokens on hidden
            // reasoning before any JSON appears; 1000 starved the output to
            // empty content in production.
            maxOutputTokens: 4000,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["triggerSource"] = request.TriggerSource,
                ["normalizedDescriptor"] = normalizedDescriptor,
                ["rawDescriptor"] = sanitizedDescriptor
            });

        var response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        var parsed = responseParser.Parse(response);
        var parsedResult = parsed.InvestigationResult;

        if (parsed.ParsedSuccessfully && parsed.SemanticallyValid)
        {
            resultCache.Set(normalizedDescriptor, parsedResult, nowUtc, options.Value.Execution);

            logger.LogInformation(
                "Merchant investigation succeeded correlationId={CorrelationId} task={TaskType} modelClass={ModelClass} routeModel={Model} routeDeployment={Deployment} mock={WasMocked} parserLowTrust={ParserLowTrust} recommendation={Recommendation} candidates={CandidateCount} confidence={OverallConfidence} ambiguity={Ambiguity} parseReasonCodes={ReasonCodes}",
                correlationId,
                AITaskType.MerchantInvestigation,
                route.ModelClass,
                route.Model,
                route.Deployment,
                response.WasMocked,
                parsed.IsLowTrustValid,
                parsedResult.Recommendation,
                parsedResult.Candidates.Count,
                parsedResult.OverallConfidence,
                parsedResult.AmbiguityLevel,
                string.Join(',', parsed.ReasonCodes));

            return parsedResult;
        }

        resultCache.Set(normalizedDescriptor, parsedResult, nowUtc, options.Value.Execution);
        await failureRecorder.RecordAsync(
            new OperationalFailureRecordInput(
                OperationalFailureArea.MerchantInvestigation,
                OperationalFailureSeverity.Warning,
                "investigation_parse_failure",
                $"investigation_parse_failure:{parsed.FailureCode ?? "unknown"}:{normalizedDescriptor}",
                correlationId,
                normalizedDescriptor,
                parsed.FailureReason ?? parsedResult.FailureReason,
                $"{{\"failureCode\":\"{parsed.FailureCode}\",\"provider\":\"{response.Provider}\",\"model\":\"{response.Model}\"}}"),
            cancellationToken);

        logger.LogWarning(
            "Merchant investigation parse failure correlationId={CorrelationId} task={TaskType} modelClass={ModelClass} routeModel={Model} routeDeployment={Deployment} mock={WasMocked} parseFailureCode={FailureCode} parseReasonCodes={ReasonCodes} failureReason={FailureReason}",
            correlationId,
            AITaskType.MerchantInvestigation,
            route.ModelClass,
            route.Model,
            route.Deployment,
            response.WasMocked,
            parsed.FailureCode ?? "unknown",
            string.Join(',', parsed.ReasonCodes),
            parsed.FailureReason ?? parsedResult.FailureReason);

        return parsedResult;
    }
}
