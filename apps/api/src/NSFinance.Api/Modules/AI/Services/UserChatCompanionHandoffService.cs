namespace NSFinance.Api.Modules.AI.Services;

public interface IUserChatCompanionHandoffService
{
    Task<UserChatResponse?> TryExecuteAsync(
        UserChatRequest request,
        AIModelRoute route,
        string sessionId,
        CancellationToken cancellationToken);
}

public sealed class UserChatCompanionHandoffService(
    ICompanionIntentRouter companionIntentRouter,
    IFinancialCompanionService financialCompanionService,
    ILogger<UserChatCompanionHandoffService> logger) : IUserChatCompanionHandoffService
{
    public async Task<UserChatResponse?> TryExecuteAsync(
        UserChatRequest request,
        AIModelRoute route,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!request.UserId.HasValue)
        {
            return null;
        }

        var routing = companionIntentRouter.Route(request.UserMessage);
        if (!ShouldHandoffToCompanion(routing))
        {
            return null;
        }

        var grounding = CompanionLocationGroundingParser.Parse(request.Metadata, request.State);
        var requiresCurrentLocation = CompanionLocationGroundingParser.RequiresCurrentLocation(request.UserMessage);
        logger.LogInformation(
            "User chat companion handoff evaluated intent={Intent} hasCoordinates={HasCoordinates} hasTypedArea={HasTypedArea} requiresCurrentLocation={RequiresCurrentLocation}",
            routing.IntentFamily,
            grounding.HasCoordinates,
            grounding.HasTypedArea,
            requiresCurrentLocation);

        if (requiresCurrentLocation && !grounding.HasCoordinates && !grounding.HasTypedArea)
        {
            return BuildMissingLocationGroundingResponse(route);
        }

        var companionQuery = grounding.HasCoordinates
            ? request.UserMessage.Trim()
            : CompanionLocationGroundingParser.ApplyTypedAreaToQuery(request.UserMessage, grounding.TypedArea);
        var companionMetadata = BuildCompanionMetadata(request.Metadata, grounding, routing);
        var companionRequest = new FinancialCompanionRequest(
            UserId: request.UserId.Value,
            SessionId: string.IsNullOrWhiteSpace(sessionId) ? request.CorrelationId : sessionId,
            UserQuery: companionQuery,
            Metadata: companionMetadata,
            CorrelationId: request.CorrelationId);

        FinancialCompanionResponse companionResponse;
        try
        {
            companionResponse = await financialCompanionService.ExecuteAsync(
                companionRequest,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Companion handoff failed unexpectedly userId={UserId} correlationId={CorrelationId}",
                request.UserId,
                request.CorrelationId);
            return new UserChatResponse(
                ReplyText:
                "I couldn't access grounded place data right now. "
                + "Please try again shortly or provide a specific area.",
                ModelUsed: "companion_handoff_failure_fallback",
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(grounding),
                ReferencedContextSummary: null,
                Warnings:
                [
                    "chat_path_companion_local_places",
                    "companion_handoff_failed"
                ],
                FollowUpIntentHints:
                [
                    "retry_local_places",
                    "provide_typed_location"
                ],
                Succeeded: true,
                FailureReason: null);
        }

        var succeeded = companionResponse.Succeeded || !string.IsNullOrWhiteSpace(companionResponse.ReplyText);
        var warningSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chat_path_companion_local_places",
            grounding.HasCoordinates ? "nearby_grounding_source:gps" : "nearby_grounding_source:typed_or_query"
        };

        foreach (var warning in companionResponse.Warnings)
        {
            warningSet.Add(warning);
        }

        if (companionResponse.HasInsufficientData)
        {
            warningSet.Add("companion_places_insufficient_data");
        }

        return new UserChatResponse(
            ReplyText: string.IsNullOrWhiteSpace(companionResponse.ReplyText)
                ? "I couldn't build a grounded nearby response for that request."
                : companionResponse.ReplyText,
            ModelUsed: string.IsNullOrWhiteSpace(companionResponse.ModelUsed)
                ? "financial_companion"
                : companionResponse.ModelUsed,
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(grounding),
            ReferencedContextSummary: null,
            Warnings: warningSet.ToArray(),
            FollowUpIntentHints:
                companionResponse.HasInsufficientData
                    ? ["provide_typed_location", "retry_local_places"]
                    : ["refine_place_preferences"],
            Succeeded: succeeded,
            FailureReason: succeeded
                ? null
                : companionResponse.FailureReason ?? "companion_handoff_failed");
    }

    private static bool ShouldHandoffToCompanion(CompanionIntentRoutingResult routing)
    {
        return routing.PrimaryIntent == FinancialCompanionIntent.LocalPlacesOutings
               || routing.IntentFamily == FinancialCompanionIntent.LocalPlacesOutings;
    }

    private static IReadOnlyDictionary<string, string> BuildCompanionMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        CompanionLocationGrounding grounding,
        CompanionIntentRoutingResult routing)
    {
        var result = metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        result["chat_path"] = "companion_local_places";
        result["companion_intent_family"] = routing.IntentFamily.ToString();
        result["companion_primary_intent"] = routing.PrimaryIntent.ToString();

        if (grounding.HasCoordinates)
        {
            result[CompanionLocationMetadataKeys.Source] = "gps";
        }
        else if (grounding.HasTypedArea)
        {
            result[CompanionLocationMetadataKeys.Source] = "typed_area";
        }

        if (grounding.Latitude.HasValue)
        {
            result[CompanionLocationMetadataKeys.Latitude] =
                grounding.Latitude.Value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (grounding.Longitude.HasValue)
        {
            result[CompanionLocationMetadataKeys.Longitude] =
                grounding.Longitude.Value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (grounding.RadiusMeters.HasValue)
        {
            result[CompanionLocationMetadataKeys.RadiusMeters] = grounding.RadiusMeters.Value.ToString();
        }

        if (grounding.HasTypedArea)
        {
            result[CompanionLocationMetadataKeys.TypedArea] = grounding.TypedArea!;
        }

        if (!string.IsNullOrWhiteSpace(grounding.LocalityLabel))
        {
            result[CompanionLocationMetadataKeys.LocalityLabel] = grounding.LocalityLabel!;
        }

        if (!string.IsNullOrWhiteSpace(grounding.AccuracyBucket))
        {
            result[CompanionLocationMetadataKeys.AccuracyBucket] = grounding.AccuracyBucket!;
        }

        if (grounding.CapturedAtUtc.HasValue)
        {
            result[CompanionLocationMetadataKeys.CapturedAtUtc] =
                grounding.CapturedAtUtc.Value.UtcDateTime.ToString("O");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildSuggestedStateUpdates(
        CompanionLocationGrounding grounding)
    {
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (grounding.HasTypedArea)
        {
            updates["location_preference"] = grounding.TypedArea!;
        }
        else if (!string.IsNullOrWhiteSpace(grounding.LocalityLabel))
        {
            updates["location_preference"] = grounding.LocalityLabel!;
        }

        return updates;
    }

    private static UserChatResponse BuildMissingLocationGroundingResponse(AIModelRoute route)
    {
        return new UserChatResponse(
            ReplyText:
            "I can help with nearby place suggestions, but I need either your location permission "
            + "or a typed area like a suburb, city centre, postcode, or landmark.",
            ModelUsed: "companion_location_guard",
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ReferencedContextSummary: null,
            Warnings:
            [
                "chat_path_companion_local_places",
                "local_places_intent_missing_places_grounding",
                "nearby_location_missing"
            ],
            FollowUpIntentHints:
            [
                "allow_location_permission",
                "provide_typed_location"
            ],
            Succeeded: true,
            FailureReason: null);
    }
}
