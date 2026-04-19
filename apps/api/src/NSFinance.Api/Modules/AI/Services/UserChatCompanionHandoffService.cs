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
    ILocalDiscoveryConstraintExtractor localDiscoveryConstraintExtractor,
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
        var localDiscovery = localDiscoveryConstraintExtractor.Extract(request.UserMessage);
        if (!ShouldHandoffToCompanion(routing, localDiscovery))
        {
            logger.LogInformation(
                "User chat companion handoff skipped intent={Intent} localDiscoveryCandidate={IsCandidate} confidence={Confidence}",
                routing.IntentFamily,
                localDiscovery.IsLocalDiscoveryCandidate,
                localDiscovery.Confidence);
            return null;
        }

        var grounding = CompanionLocationGroundingParser.Parse(request.Metadata, request.State);
        var requiresCurrentLocation = CompanionLocationGroundingParser.RequiresCurrentLocation(request.UserMessage);
        var requiresGrounding = RequiresGrounding(localDiscovery, requiresCurrentLocation);
        logger.LogInformation(
            "User chat companion handoff evaluated intent={Intent} confidence={Confidence} hasCoordinates={HasCoordinates} hasTypedArea={HasTypedArea} hasExplicitLocality={HasExplicitLocality} requiresCurrentLocation={RequiresCurrentLocation} requiresGrounding={RequiresGrounding}",
            routing.IntentFamily,
            localDiscovery.Confidence,
            grounding.HasCoordinates,
            grounding.HasTypedArea,
            localDiscovery.HasExplicitLocality,
            requiresCurrentLocation,
            requiresGrounding);

        if (requiresGrounding
            && !grounding.HasCoordinates
            && !grounding.HasTypedArea
            && !localDiscovery.HasExplicitLocality)
        {
            return BuildMissingLocationGroundingResponse(route);
        }

        var companionQuery = BuildCompanionQuery(request.UserMessage, grounding, localDiscovery);
        var companionMetadata = BuildCompanionMetadata(
            request.Metadata,
            grounding,
            routing,
            localDiscovery);
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
            ResolveGroundingSourceWarning(grounding, localDiscovery),
            routing.PrimaryIntent == FinancialCompanionIntent.LocalPlacesOutings
                || routing.IntentFamily == FinancialCompanionIntent.LocalPlacesOutings
                ? "local_discovery_handoff_source:router"
                : "local_discovery_handoff_source:extraction"
        };
        warningSet.Add($"local_discovery_confidence:{localDiscovery.Confidence:0.##}");

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

    private static bool ShouldHandoffToCompanion(
        CompanionIntentRoutingResult routing,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        return routing.PrimaryIntent == FinancialCompanionIntent.LocalPlacesOutings
               || routing.IntentFamily == FinancialCompanionIntent.LocalPlacesOutings
               || localDiscovery.IsLocalDiscoveryCandidate;
    }

    private static bool RequiresGrounding(
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        bool requiresCurrentLocation)
    {
        if (requiresCurrentLocation || localDiscovery.HasNearMeLanguage)
        {
            return true;
        }

        if (localDiscovery.HasExplicitLocality)
        {
            return false;
        }

        return localDiscovery.IsLocalDiscoveryCandidate;
    }

    private static string BuildCompanionQuery(
        string userMessage,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        if (grounding.HasCoordinates)
        {
            return userMessage.Trim();
        }

        if (grounding.HasTypedArea)
        {
            return CompanionLocationGroundingParser.ApplyTypedAreaToQuery(userMessage, grounding.TypedArea);
        }

        if (localDiscovery.HasExplicitLocality && !string.IsNullOrWhiteSpace(localDiscovery.LocalityHint))
        {
            return CompanionLocationGroundingParser.ApplyTypedAreaToQuery(userMessage, localDiscovery.LocalityHint);
        }

        return userMessage.Trim();
    }

    private static IReadOnlyDictionary<string, string> BuildCompanionMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        CompanionLocationGrounding grounding,
        CompanionIntentRoutingResult routing,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
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
        else if (localDiscovery.HasExplicitLocality)
        {
            result[CompanionLocationMetadataKeys.Source] = "query_locality";
            if (!string.IsNullOrWhiteSpace(localDiscovery.LocalityHint))
            {
                result[CompanionLocationMetadataKeys.TypedArea] = localDiscovery.LocalityHint;
            }
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

        result["local_discovery_confidence"] =
            localDiscovery.Confidence.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        if (localDiscovery.ReasonCodes.Count > 0)
        {
            result["local_discovery_reason_codes"] = string.Join(',', localDiscovery.ReasonCodes);
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

    private static string ResolveGroundingSourceWarning(
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        if (grounding.HasCoordinates)
        {
            return "nearby_grounding_source:gps";
        }

        if (grounding.HasTypedArea)
        {
            return "nearby_grounding_source:typed_area";
        }

        if (localDiscovery.HasExplicitLocality)
        {
            return "nearby_grounding_source:query_locality";
        }

        return "nearby_grounding_source:missing";
    }
}
