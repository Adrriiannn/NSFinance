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
    ILocalDiscoveryConstraintExtractor localDiscoveryConstraintExtractor,
    IRealWorldIntentInterpreter intentInterpreter,
    IRealWorldExecutionModePlanner executionModePlanner,
    IRealWorldPlacesExecutionService placesExecutionService,
    IRealWorldFailureMessageBuilder failureMessageBuilder,
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

        var localDiscovery = localDiscoveryConstraintExtractor.Extract(request.UserMessage);
        var grounding = CompanionLocationGroundingParser.Parse(request.Metadata, request.State);
        var interpretation = await intentInterpreter.InterpretAsync(
            request,
            grounding,
            localDiscovery,
            cancellationToken);
        var plan = executionModePlanner.Plan(
            request.UserMessage,
            interpretation,
            grounding,
            localDiscovery);

        logger.LogInformation(
            "User chat real-world planner intentFamily={IntentFamily} mode={ExecutionMode} confidence={Confidence} placesApplicable={PlacesApplicable} directPlaces={DirectPlaces} hasCoordinates={HasCoordinates} hasTypedArea={HasTypedArea} explicitLocality={HasExplicitLocality}",
            interpretation.IntentFamily,
            plan.Mode,
            interpretation.Confidence,
            interpretation.PlacesApplicable,
            plan.UseDirectPlacesExecution,
            grounding.HasCoordinates,
            grounding.HasTypedArea,
            localDiscovery.HasExplicitLocality);

        if (plan.Mode == RealWorldExecutionMode.FinancialGuidanceOnly)
        {
            return null;
        }

        if (plan.Mode == RealWorldExecutionMode.ClarifyLight)
        {
            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                localDiscovery,
                RealWorldFailureScenario.ClarificationNeeded,
                exploratory: false,
                clarificationPrompt: plan.ClarificationPrompt);
        }

        if (plan.Mode == RealWorldExecutionMode.MissingLocationGuard)
        {
            var scenario = IsOpenSettingsRequired(request.Metadata)
                ? RealWorldFailureScenario.LocationDeniedOpenSettings
                : RealWorldFailureScenario.MissingLocation;
            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                localDiscovery,
                scenario,
                exploratory: false,
                clarificationPrompt: null);
        }

        if (plan.UseDirectPlacesExecution
            || plan.Mode is RealWorldExecutionMode.ExploratoryMultiDomainSearch or RealWorldExecutionMode.FocusedThemeSearch)
        {
            return await ExecuteDirectPlacesModeAsync(
                request,
                route,
                plan,
                interpretation,
                grounding,
                localDiscovery,
                cancellationToken);
        }

        return await ExecuteCompanionModeAsync(
            request,
            route,
            sessionId,
            plan,
            interpretation,
            grounding,
            localDiscovery,
            cancellationToken);
    }

    private async Task<UserChatResponse> ExecuteDirectPlacesModeAsync(
        UserChatRequest request,
        AIModelRoute route,
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CancellationToken cancellationToken)
    {
        var locationContext = BuildPlaceSearchLocationContext(grounding, localDiscovery);
        var countryCode = ResolveCountryCode(request.Metadata);
        var execution = await placesExecutionService.ExecuteAsync(
            new RealWorldPlacesExecutionRequest(
                UserQuery: request.UserMessage,
                CountryCode: countryCode,
                LocationContext: locationContext,
                Domains: plan.SelectedDomains,
                MaxDomains: plan.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch ? 4 : 2,
                MaxItemsPerDomain: 2,
                MaxTotalItems: 8,
                Mode: plan.Mode),
            cancellationToken);

        var warningSet = BuildBaseWarnings(plan, interpretation, localDiscovery, grounding);
        warningSet.UnionWith(execution.Warnings);
        warningSet.UnionWith(execution.ReasonCodes);

        if (!execution.Succeeded || !execution.HasAnyResults)
        {
            var failureScenario = execution.FailureScenario ?? RealWorldFailureScenario.ProviderUnavailable;
            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                localDiscovery,
                failureScenario,
                exploratory: plan.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
                clarificationPrompt: null,
                additionalWarnings: warningSet);
        }

        var replyText = BuildGroupedPlacesReply(execution.Groups, plan.Mode);
        var followUpHints = new[] { "refine_place_preferences", "retry_local_search" };

        if (execution.IsPartial)
        {
            var partialMessage = failureMessageBuilder.Build(
                RealWorldFailureScenario.ExploratoryPartialResults,
                exploratory: true,
                clarificationPrompt: null);
            warningSet.UnionWith(partialMessage.Warnings);
            replyText = $"{partialMessage.ReplyText}\n\n{replyText}";
        }

        return new UserChatResponse(
            ReplyText: replyText,
            ModelUsed: "real_world_places_execution",
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(grounding),
            ReferencedContextSummary: null,
            Warnings: warningSet.ToArray(),
            FollowUpIntentHints: followUpHints,
            Succeeded: true,
            FailureReason: null);
    }

    private async Task<UserChatResponse> ExecuteCompanionModeAsync(
        UserChatRequest request,
        AIModelRoute route,
        string sessionId,
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CancellationToken cancellationToken)
    {
        var companionQuery = BuildCompanionQuery(request.UserMessage, plan, grounding, localDiscovery);
        var companionMetadata = BuildCompanionMetadata(
            request.Metadata,
            grounding,
            interpretation,
            plan,
            localDiscovery);
        var companionRequest = new FinancialCompanionRequest(
            UserId: request.UserId!.Value,
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
                "Companion execution failed unexpectedly userId={UserId} correlationId={CorrelationId}",
                request.UserId,
                request.CorrelationId);

            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                localDiscovery,
                RealWorldFailureScenario.ProviderUnavailable,
                exploratory: false,
                clarificationPrompt: null,
                additionalWarnings: ["real_world_companion_execution_exception"]);
        }

        var warningSet = BuildBaseWarnings(plan, interpretation, localDiscovery, grounding);
        warningSet.UnionWith(companionResponse.Warnings);

        var succeeded = companionResponse.Succeeded || !string.IsNullOrWhiteSpace(companionResponse.ReplyText);
        if (!succeeded || companionResponse.HasInsufficientData)
        {
            var scenario = ClassifyCompanionFailureScenario(companionResponse);
            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                localDiscovery,
                scenario,
                exploratory: plan.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
                clarificationPrompt: null,
                additionalWarnings: warningSet);
        }

        return new UserChatResponse(
            ReplyText: companionResponse.ReplyText,
            ModelUsed: string.IsNullOrWhiteSpace(companionResponse.ModelUsed)
                ? "financial_companion"
                : companionResponse.ModelUsed,
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(grounding),
            ReferencedContextSummary: null,
            Warnings: warningSet.ToArray(),
            FollowUpIntentHints: ["refine_place_preferences"],
            Succeeded: true,
            FailureReason: null);
    }

    private UserChatResponse BuildFailureResponse(
        AIModelRoute route,
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        RealWorldFailureScenario scenario,
        bool exploratory,
        string? clarificationPrompt,
        IEnumerable<string>? additionalWarnings = null)
    {
        var fallback = failureMessageBuilder.Build(scenario, exploratory, clarificationPrompt);
        var warningSet = BuildBaseWarnings(
            plan,
            interpretation,
            localDiscovery,
            grounding: new CompanionLocationGrounding(null, null, null, null, null, null, null, null));
        warningSet.UnionWith(fallback.Warnings);
        if (additionalWarnings is not null)
        {
            warningSet.UnionWith(additionalWarnings);
        }

        warningSet.Add($"real_world_failure_scenario:{scenario.ToString().ToLowerInvariant()}");

        return new UserChatResponse(
            ReplyText: fallback.ReplyText,
            ModelUsed: "real_world_failure_fallback",
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ReferencedContextSummary: null,
            Warnings: warningSet.ToArray(),
            FollowUpIntentHints: fallback.FollowUpIntentHints,
            Succeeded: true,
            FailureReason: null);
    }

    private static string BuildGroupedPlacesReply(
        IReadOnlyList<RealWorldDomainPlacesGroup> groups,
        RealWorldExecutionMode mode)
    {
        var heading = mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch
            ? "Here are a few nearby options across different categories:"
            : "Here are some grounded nearby options:";

        var sections = new List<string>(groups.Count + 1)
        {
            heading
        };

        foreach (var group in groups)
        {
            sections.Add($"\n{group.Label}:");
            var index = 1;
            foreach (var item in group.Items)
            {
                var details = new List<string>(4);
                if (item.Rating.HasValue)
                {
                    details.Add($"rating {item.Rating.Value:0.0}");
                }

                if (!string.IsNullOrWhiteSpace(item.ShortFormattedAddress))
                {
                    details.Add(item.ShortFormattedAddress!);
                }
                else if (!string.IsNullOrWhiteSpace(item.FormattedAddress))
                {
                    details.Add(item.FormattedAddress!);
                }

                if (item.OpeningHours?.OpenNow is true)
                {
                    details.Add("open now");
                }

                sections.Add(
                    $"{index}. {item.Name}{(details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty)}");
                index += 1;
            }
        }

        return string.Join("\n", sections);
    }

    private static PlaceSearchLocationContext? BuildPlaceSearchLocationContext(
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        if (!grounding.HasCoordinates && !grounding.HasTypedArea && !localDiscovery.HasExplicitLocality)
        {
            return null;
        }

        return new PlaceSearchLocationContext(
            Source: grounding.Source,
            Latitude: grounding.Latitude,
            Longitude: grounding.Longitude,
            RadiusMeters: grounding.RadiusMeters,
            TypedArea: grounding.TypedArea ?? localDiscovery.LocalityHint,
            LocalityLabel: grounding.LocalityLabel ?? localDiscovery.LocalityHint,
            AccuracyBucket: grounding.AccuracyBucket,
            CapturedAtUtc: grounding.CapturedAtUtc);
    }

    private static string ResolveCountryCode(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return "IE";
        }

        var keys = new[] { "country", "countryCode", "chat_country", "chat_country_code" };
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim().ToUpperInvariant();
            if (normalized.Length == 2)
            {
                return normalized;
            }
        }

        return "IE";
    }

    private static string BuildCompanionQuery(
        string userMessage,
        RealWorldExecutionPlan plan,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var query = userMessage.Trim();
        var domain = plan.SelectedDomains.FirstOrDefault();
        if (domain != default)
        {
            var phrase = domain.ToQueryPhrase();
            if (!query.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                query = $"{query} {phrase}".Trim();
            }
        }

        if (grounding.HasTypedArea)
        {
            return CompanionLocationGroundingParser.ApplyTypedAreaToQuery(query, grounding.TypedArea);
        }

        if (localDiscovery.HasExplicitLocality && !string.IsNullOrWhiteSpace(localDiscovery.LocalityHint))
        {
            return CompanionLocationGroundingParser.ApplyTypedAreaToQuery(query, localDiscovery.LocalityHint);
        }

        return query;
    }

    private static IReadOnlyDictionary<string, string> BuildCompanionMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        CompanionLocationGrounding grounding,
        RealWorldIntentInterpretation interpretation,
        RealWorldExecutionPlan plan,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var result = metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        result["chat_path"] = "companion_real_world";
        result["real_world_intent_family"] = interpretation.IntentFamily.ToString();
        result["real_world_execution_mode"] = plan.Mode.ToString();
        result["real_world_confidence"] = interpretation.Confidence.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        result["real_world_candidate_domains"] = string.Join(',', plan.SelectedDomains.Select(x => x.ToString()));

        result["companion_intent_family"] = FinancialCompanionIntent.LocalPlacesOutings.ToString();
        result["companion_primary_intent"] = FinancialCompanionIntent.LocalPlacesOutings.ToString();

        if (grounding.HasCoordinates)
        {
            result[CompanionLocationMetadataKeys.Source] = "gps";
            result[CompanionLocationMetadataKeys.Latitude] =
                grounding.Latitude!.Value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
            result[CompanionLocationMetadataKeys.Longitude] =
                grounding.Longitude!.Value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (grounding.HasTypedArea)
        {
            result[CompanionLocationMetadataKeys.Source] = "typed_area";
            result[CompanionLocationMetadataKeys.TypedArea] = grounding.TypedArea!;
        }
        else if (localDiscovery.HasExplicitLocality)
        {
            result[CompanionLocationMetadataKeys.Source] = "query_locality";
            if (!string.IsNullOrWhiteSpace(localDiscovery.LocalityHint))
            {
                result[CompanionLocationMetadataKeys.TypedArea] = localDiscovery.LocalityHint;
            }
        }

        if (grounding.RadiusMeters.HasValue)
        {
            result[CompanionLocationMetadataKeys.RadiusMeters] = grounding.RadiusMeters.Value.ToString();
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

    private static HashSet<string> BuildBaseWarnings(
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CompanionLocationGrounding grounding)
    {
        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chat_path_companion_real_world",
            $"real_world_intent_family:{interpretation.IntentFamily.ToString().ToLowerInvariant()}",
            $"real_world_execution_mode:{plan.Mode.ToString().ToLowerInvariant()}",
            $"real_world_confidence:{interpretation.Confidence:0.##}",
            $"real_world_interpreter_source:{interpretation.InterpretationSource.ToString().ToLowerInvariant()}",
            ResolveGroundingSourceWarning(grounding, localDiscovery)
        };

        if (plan.ShouldUsePlaces)
        {
            warnings.Add("chat_path_companion_local_places");
        }

        foreach (var reasonCode in interpretation.ReasonCodes)
        {
            warnings.Add($"real_world_reason:{reasonCode}");
        }

        foreach (var reasonCode in plan.ReasonCodes)
        {
            warnings.Add($"real_world_plan_reason:{reasonCode}");
        }

        return warnings;
    }

    private static RealWorldFailureScenario ClassifyCompanionFailureScenario(FinancialCompanionResponse response)
    {
        var reasons = (response.InsufficientDataReasons ?? [])
            .Concat(response.Warnings)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (reasons.Any(reason => reason.Contains("missing_places_grounding", StringComparison.OrdinalIgnoreCase)))
        {
            return RealWorldFailureScenario.MissingLocation;
        }

        if (reasons.Any(reason => reason.Contains("invalid_argument", StringComparison.OrdinalIgnoreCase))
            || reasons.Any(reason => reason.Contains("request_construction", StringComparison.OrdinalIgnoreCase)))
        {
            return RealWorldFailureScenario.ProviderRequestFailure;
        }

        if (reasons.Any(reason => reason.Contains("required_tool_failed:places_search", StringComparison.OrdinalIgnoreCase))
            || reasons.Any(reason => reason.Contains("provider_unavailable:places_search", StringComparison.OrdinalIgnoreCase))
            || reasons.Any(reason => reason.Contains("timeout_or_cancellation:places_search", StringComparison.OrdinalIgnoreCase)))
        {
            return RealWorldFailureScenario.ProviderUnavailable;
        }

        if (reasons.Any(reason => reason.Contains("required_tool_returned_no_data:places_search", StringComparison.OrdinalIgnoreCase))
            || reasons.Any(reason => reason.Contains("missing_required_places_search", StringComparison.OrdinalIgnoreCase)))
        {
            return RealWorldFailureScenario.NoMatchesFound;
        }

        return RealWorldFailureScenario.ProviderUnavailable;
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

    private static bool IsOpenSettingsRequired(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        var candidates = new[]
        {
            "chat_location_permission_state",
            "location_permission_state",
            "chat_location_source"
        };

        foreach (var key in candidates)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("open_settings", StringComparison.Ordinal)
                || normalized.Contains("denied_open_settings", StringComparison.Ordinal)
                || normalized.Contains("blocked", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

