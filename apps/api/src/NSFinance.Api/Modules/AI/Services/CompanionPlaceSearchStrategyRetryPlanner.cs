using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceSearchStrategyRetryPlanner(
    ICompanionPlaceSearchStrategyJsonParser parser,
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry,
    ILogger<CompanionPlaceSearchStrategyRetryPlanner> logger) : ICompanionPlaceSearchStrategyRetryPlanner
{
    public async Task<CompanionPlaceSearchStrategyRetryResult> TryPlanAsync(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        string retryReason,
        CancellationToken cancellationToken)
    {
        var architecture = options.Value.Architecture;
        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            AIModelClass.Fast,
            complexityHint: "places_search_strategy_retry");

        await telemetry.TrackAsync(
            "places.search_strategy.retry_started",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["retryReason"] = retryReason,
                ["placeQuery"] = intent.PlaceQuery,
                ["brandOrEntity"] = intent.BrandOrEntity
            },
            cancellationToken);

        AIResponse response;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutMs = Math.Clamp(architecture.PlacesStrategyPlannerRetryTimeoutMs <= 0 ? 2000 : architecture.PlacesStrategyPlannerRetryTimeoutMs, 250, 5_000);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
            response = await aiClient.SendAsync(
                AIRequest.Create(
                    taskType: AITaskType.ConversationDecision,
                    preferredModelClass: AIModelClass.Fast,
                    messages: [AIMessage.User(BuildUserPayload(request, intent))],
                    correlationId: request.CorrelationId,
                    systemInstructions: BuildSystemInstructions(),
                    structuredOutputSchemaName: "companion_place_search_strategy_v1",
                    temperature: 0d,
                    maxOutputTokens: 520,
                    metadata: request.Metadata),
                route,
                timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Places search strategy retry timed out correlationId={CorrelationId}", request.CorrelationId);
            await TrackFailedAsync(request, "places_search_strategy_retry_timeout", cancellationToken);
            return new CompanionPlaceSearchStrategyRetryResult(false, null, "places_search_strategy_retry_timeout");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Places search strategy retry failed correlationId={CorrelationId}", request.CorrelationId);
            await TrackFailedAsync(request, "places_search_strategy_retry_failed", cancellationToken);
            return new CompanionPlaceSearchStrategyRetryResult(false, null, "places_search_strategy_retry_failed");
        }

        if (!parser.TryParse(response, request, intent, out var strategy, out var reasonCodes, out var failureReason) || strategy is null)
        {
            await telemetry.TrackAsync(
                "places.search_strategy.retry_failed",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = request.CorrelationId,
                    ["failureReason"] = failureReason,
                    ["reasonCodes"] = reasonCodes.ToArray()
                },
                cancellationToken);
            return new CompanionPlaceSearchStrategyRetryResult(false, null, failureReason ?? "places_search_strategy_retry_parse_failed");
        }

        if (strategy.Confidence < 0.35d)
        {
            await TrackFailedAsync(request, "places_search_strategy_retry_low_confidence", cancellationToken);
            return new CompanionPlaceSearchStrategyRetryResult(false, null, "places_search_strategy_retry_low_confidence");
        }

        await telemetry.TrackAsync(
            "places.search_strategy.retry_completed",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["canonicalQuery"] = strategy.CanonicalQuery,
                ["entity"] = strategy.Entity?.CanonicalName,
                ["requestedRole"] = strategy.Role.RequestedRole,
                ["variantCount"] = strategy.SearchVariants.Count,
                ["confidence"] = strategy.Confidence
            },
            cancellationToken);
        return new CompanionPlaceSearchStrategyRetryResult(true, strategy, null);
    }

    private async Task TrackFailedAsync(UserChatRequest request, string failureReason, CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "places.search_strategy.retry_failed",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["failureReason"] = failureReason
            },
            cancellationToken);
    }

    private static string BuildSystemInstructions()
    {
        return """
Return strict JSON only for a Google Places search strategy.
Extract canonicalQuery, optional entity, role/category, modifiers, exclusions, 1-3 searchVariants, rankingGoal, confidence.
Do not turn generic categories into brands. Do not add unrelated variants. Do not add coffee/cafe unless the request is coffee/cafe.
For unknown categories, preserve the user's place phrase as canonicalQuery with entity null and loose role.
""";
    }

    private static string BuildUserPayload(UserChatRequest request, CompanionSemanticIntent intent)
    {
        return $$"""
{"userMessage":{{System.Text.Json.JsonSerializer.Serialize(request.UserMessage)}},"placeQuery":{{System.Text.Json.JsonSerializer.Serialize(intent.PlaceQuery)}},"brandOrEntity":{{System.Text.Json.JsonSerializer.Serialize(intent.BrandOrEntity)}},"locationMode":{{System.Text.Json.JsonSerializer.Serialize(intent.Location.Mode)}},"areaText":{{System.Text.Json.JsonSerializer.Serialize(intent.Location.AreaText)}}}
""";
    }
}
