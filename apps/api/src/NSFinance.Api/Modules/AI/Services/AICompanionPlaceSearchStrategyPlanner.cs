using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AICompanionPlaceSearchStrategyPlanner(
    ICompanionPlaceSearchStrategyPromptBuilder promptBuilder,
    ICompanionPlaceSearchStrategyJsonParser parser,
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IDeterministicCompanionPlaceSearchStrategyFallback fallback,
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry,
    ILogger<AICompanionPlaceSearchStrategyPlanner> logger) : ICompanionPlaceSearchStrategyPlanner
{
    public async Task<CompanionPlaceSearchStrategy> PlanAsync(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CancellationToken cancellationToken)
    {
        var architecture = options.Value.Architecture;
        if (!architecture.PlacesStrategyPlannerV2Enabled)
        {
            return await UseFallbackAsync(request, intent, "places_strategy_planner_v2_disabled", cancellationToken);
        }

        if (!architecture.PlacesStrategyPlannerModelBacked)
        {
            return await UseFallbackAsync(request, intent, "places_strategy_planner_model_disabled", cancellationToken);
        }

        var prompt = promptBuilder.BuildPrompt(request, intent);
        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            AIModelClass.Fast,
            complexityHint: "places_search_strategy_v2");
        await telemetry.TrackAsync(
            "places.search_strategy.ai_invocation_started",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["model"] = route.Model,
                ["deployment"] = route.Deployment,
                ["placeQuery"] = intent.PlaceQuery,
                ["brandOrEntity"] = intent.BrandOrEntity
            },
            cancellationToken);

        AIResponse response;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutMs = Math.Clamp(architecture.PlacesStrategyPlannerTimeoutMs <= 0 ? 2500 : architecture.PlacesStrategyPlannerTimeoutMs, 250, 15_000);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
            response = await aiClient.SendAsync(
                AIRequest.Create(
                    taskType: AITaskType.ConversationDecision,
                    preferredModelClass: AIModelClass.Fast,
                    messages: prompt.Messages,
                    correlationId: request.CorrelationId,
                    systemInstructions: prompt.SystemInstructions,
                    structuredOutputSchemaName: prompt.StructuredSchemaName,
                    temperature: 0.05d,
                    maxOutputTokens: 850,
                    metadata: request.Metadata),
                route,
                timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Places search strategy planner timed out correlationId={CorrelationId}", request.CorrelationId);
            return await UseFallbackAsync(request, intent, "places_search_strategy_ai_timeout", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Places search strategy planner failed correlationId={CorrelationId}", request.CorrelationId);
            return await UseFallbackAsync(request, intent, "places_search_strategy_ai_failed", cancellationToken);
        }

        if (!parser.TryParse(response, request, intent, out var strategy, out var reasonCodes, out var failureReason) || strategy is null)
        {
            await telemetry.TrackAsync(
                "places.search_strategy.ai_parse_failed",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = request.CorrelationId,
                    ["failureReason"] = failureReason,
                    ["reasonCodes"] = reasonCodes.ToArray()
                },
                cancellationToken);
            return await UseFallbackAsync(request, intent, failureReason ?? "places_search_strategy_ai_parse_failed", cancellationToken);
        }

        if (strategy.Confidence < 0.35d)
        {
            await telemetry.TrackAsync(
                "places.search_strategy.ai_validation_failed",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = request.CorrelationId,
                    ["failureReason"] = "places_search_strategy_low_confidence",
                    ["confidence"] = strategy.Confidence
                },
                cancellationToken);
            return await UseFallbackAsync(request, intent, "places_search_strategy_low_confidence", cancellationToken);
        }

        await telemetry.TrackAsync(
            "places.search_strategy.ai_invocation_completed",
            BuildFinalTelemetry(request, strategy, "ai", fallbackReason: null),
            cancellationToken);
        await telemetry.TrackAsync(
            "places.search_strategy.finalized",
            BuildFinalTelemetry(request, strategy, "ai", fallbackReason: null),
            cancellationToken);
        return strategy;
    }

    private async Task<CompanionPlaceSearchStrategy> UseFallbackAsync(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Architecture.PlacesStrategyPlannerFallbackEnabled)
        {
            await telemetry.TrackAsync(
                "places.search_strategy.ai_validation_failed",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = request.CorrelationId,
                    ["failureReason"] = reason,
                    ["fallbackEnabled"] = false
                },
                cancellationToken);
            return new CompanionPlaceSearchStrategy(
                request.UserMessage,
                intent.PlaceQuery,
                null,
                intent.Role,
                [],
                intent.HardFilters,
                intent.NegativeFilters,
                intent.SoftPreferences,
                intent.NonSearchablePreferences,
                intent.Location,
                intent.RankingGoal,
                50,
                Math.Clamp(intent.RequestedMaxResults ?? 10, 1, 10),
                Math.Min(intent.Confidence, 0.35d),
                [reason, "places_search_strategy_no_fallback"]);
        }

        var strategy = fallback.Plan(request, intent, reason);
        await telemetry.TrackAsync(
            "places.search_strategy.finalized",
            BuildFinalTelemetry(request, strategy, "fallback", reason),
            cancellationToken);
        return strategy;
    }

    private static IReadOnlyDictionary<string, object?> BuildFinalTelemetry(
        UserChatRequest request,
        CompanionPlaceSearchStrategy strategy,
        string source,
        string? fallbackReason)
    {
        return new Dictionary<string, object?>
        {
            ["correlationId"] = request.CorrelationId,
            ["source"] = source,
            ["canonicalQuery"] = strategy.CanonicalQuery,
            ["entity"] = strategy.Entity?.CanonicalName,
            ["requestedRole"] = strategy.Role.RequestedRole,
            ["variantCount"] = strategy.SearchVariants.Count,
            ["confidence"] = strategy.Confidence,
            ["fallbackReason"] = fallbackReason
        };
    }
}
