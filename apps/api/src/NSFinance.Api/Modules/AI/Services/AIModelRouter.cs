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
        var selectedProvider = config.UseMockProvider ? AIProviderKind.Mock : config.ProviderKind;

        var requestedClass = preferredModelClass == AIModelClass.Any
            ? ResolveDefaultModelClass(taskType)
            : preferredModelClass;

        var wantsHeavy = requestedClass == AIModelClass.HeavyReasoning;
        var heavyRouteConfigured =
            !string.IsNullOrWhiteSpace(routing.HeavyModelName)
            && !string.IsNullOrWhiteSpace(routing.HeavyDeploymentName);
        var heavyEnabled = selectedProvider == AIProviderKind.Mock
            || (routing.HeavyModelEnabled ?? heavyRouteConfigured);

        if (wantsHeavy && !heavyEnabled)
        {
            if (routing.HeavyModelFallbackPolicy == HeavyModelFallbackPolicy.FailFast)
            {
                logger.LogWarning(
                    "AI model routing failed fast for task={TaskType} requestedClass={RequestedClass} because heavy model is disabled",
                    taskType,
                    requestedClass);

                var heavyNames = ResolveRouteNames(config, taskType, AIModelClass.HeavyReasoning);

                return new AIModelRoute(
                    taskType,
                    AIModelClass.HeavyReasoning,
                    heavyNames.Model,
                    heavyNames.Deployment,
                    IsFallback: false,
                    Reason: "heavy_model_disabled_fail_fast",
                    Notes: ["heavy_model_disabled"]);
            }

            logger.LogInformation(
                "AI model routing fallback task={TaskType} requestedClass={RequestedClass} fallbackClass={FallbackClass} policy={Policy}",
                taskType,
                requestedClass,
                AIModelClass.Fast,
                config.Routing.HeavyModelFallbackPolicy);

            var fastNames = ResolveRouteNames(config, taskType, AIModelClass.Fast);

            return new AIModelRoute(
                taskType,
                AIModelClass.Fast,
                fastNames.Model,
                fastNames.Deployment,
                IsFallback: true,
                Reason: "heavy_model_disabled_fallback_to_fast",
                Notes: BuildNotes("heavy_model_disabled", "fallback_to_fast", complexityHint));
        }

        if (wantsHeavy)
        {
            var heavyNames = ResolveRouteNames(config, taskType, AIModelClass.HeavyReasoning);
            return new AIModelRoute(
                taskType,
                AIModelClass.HeavyReasoning,
                heavyNames.Model,
                heavyNames.Deployment,
                IsFallback: false,
                Reason: "heavy_reasoning_route",
                Notes: BuildNotes(complexityHint));
        }

        var fastRoute = ResolveRouteNames(config, taskType, AIModelClass.Fast);
        return new AIModelRoute(
            taskType,
            AIModelClass.Fast,
            fastRoute.Model,
            fastRoute.Deployment,
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
            AITaskType.ConversationDecision => AIModelClass.Fast,
            AITaskType.ResponseComposition => AIModelClass.Fast,
            _ => AIModelClass.Fast
        };
    }

    private static (string Model, string Deployment) ResolveRouteNames(
        AIIntegrationOptions config,
        AITaskType taskType,
        AIModelClass modelClass)
    {
        if (taskType == AITaskType.ConversationDecision && modelClass == AIModelClass.Fast)
        {
            return (
                config.Architecture.Tiers.L1DecisionModelName,
                config.Architecture.Tiers.L1DecisionDeploymentName);
        }

        if (taskType == AITaskType.ResponseComposition && modelClass == AIModelClass.Fast)
        {
            return (
                config.Architecture.Tiers.L2CompositionModelName,
                config.Architecture.Tiers.L2CompositionDeploymentName);
        }

        return modelClass == AIModelClass.HeavyReasoning
            ? (config.Routing.HeavyModelName, config.Routing.HeavyDeploymentName)
            : (config.Routing.FastModelName, config.Routing.FastDeploymentName);
    }

    private static IReadOnlyList<string> BuildNotes(params string?[] values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
    }
}
