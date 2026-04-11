using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIModelRouter(
    IOptions<AIIntegrationOptions> options,
    ILogger<AIModelRouter> logger) : IAIModelRouter
{
    public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
    {
        var config = options.Value;
        var routing = config.Routing;

        var requestedClass = preferredModelClass == AIModelClass.Any
            ? ResolveDefaultModelClass(taskType)
            : preferredModelClass;

        var wantsHeavy = requestedClass == AIModelClass.HeavyReasoning;
        var heavyEnabled = routing.HeavyModelEnabled || config.UseMockProvider || config.ProviderKind == AIProviderKind.Mock;

        if (wantsHeavy && !heavyEnabled)
        {
            if (routing.HeavyModelFallbackPolicy == HeavyModelFallbackPolicy.FailFast)
            {
                logger.LogWarning(
                    "AI model routing failed fast for task={TaskType} requestedClass={RequestedClass} because heavy model is disabled",
                    taskType,
                    requestedClass);

                return new AIModelRoute(
                    taskType,
                    AIModelClass.HeavyReasoning,
                    routing.HeavyModelName,
                    routing.HeavyDeploymentName,
                    IsFallback: false,
                    Reason: "heavy_model_disabled_fail_fast",
                    Notes: ["heavy_model_disabled"]);
            }

            logger.LogInformation(
                "AI model routing fallback task={TaskType} requestedClass={RequestedClass} fallbackClass={FallbackClass} policy={Policy}",
                taskType,
                requestedClass,
                AIModelClass.Fast,
                routing.HeavyModelFallbackPolicy);

            return new AIModelRoute(
                taskType,
                AIModelClass.Fast,
                routing.FastModelName,
                routing.FastDeploymentName,
                IsFallback: true,
                Reason: "heavy_model_disabled_fallback_to_fast",
                Notes: BuildNotes("heavy_model_disabled", "fallback_to_fast", complexityHint));
        }

        if (wantsHeavy)
        {
            return new AIModelRoute(
                taskType,
                AIModelClass.HeavyReasoning,
                routing.HeavyModelName,
                routing.HeavyDeploymentName,
                IsFallback: false,
                Reason: "heavy_reasoning_route",
                Notes: BuildNotes(complexityHint));
        }

        return new AIModelRoute(
            taskType,
            AIModelClass.Fast,
            routing.FastModelName,
            routing.FastDeploymentName,
            IsFallback: false,
            Reason: "fast_route",
            Notes: BuildNotes(complexityHint));
    }

    private static AIModelClass ResolveDefaultModelClass(AITaskType taskType)
    {
        return taskType switch
        {
            AITaskType.MerchantInvestigation => AIModelClass.HeavyReasoning,
            AITaskType.UserChatComplex => AIModelClass.HeavyReasoning,
            AITaskType.FinancialReasoning => AIModelClass.HeavyReasoning,
            _ => AIModelClass.Fast
        };
    }

    private static IReadOnlyList<string> BuildNotes(params string?[] values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
    }
}
