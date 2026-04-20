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
    IRealWorldConversationSearchContextService conversationSearchContextService,
    IRealWorldSearchScopeResolver searchScopeResolver,
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

        var requestLocalDiscovery = localDiscoveryConstraintExtractor.Extract(request.UserMessage);
        var requestGrounding = CompanionLocationGroundingParser.Parse(request.Metadata, request.State);
        var contextReadResult = conversationSearchContextService.Read(sessionId);
        var scopeResolution = searchScopeResolver.Resolve(
            request.UserMessage,
            requestGrounding,
            requestLocalDiscovery,
            contextReadResult);
        var localDiscovery = scopeResolution.EffectiveLocalDiscovery;
        var grounding = scopeResolution.EffectiveGrounding;
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
        var contextWriteGrounding = BuildContextWriteGrounding(scopeResolution);
        if (plan.Mode != RealWorldExecutionMode.FinancialGuidanceOnly)
        {
            conversationSearchContextService.Write(
                sessionId,
                new RealWorldConversationSearchContextWriteInput(
                    Grounding: contextWriteGrounding,
                    LocalDiscovery: localDiscovery,
                    Interpretation: interpretation,
                    Plan: plan));
        }

        logger.LogInformation(
            "User chat real-world planner intentFamily={IntentFamily} mode={ExecutionMode} confidence={Confidence} placesApplicable={PlacesApplicable} directPlaces={DirectPlaces} routeAuthoritative={RouteAuthoritative} hasCoordinates={HasCoordinates} hasTypedArea={HasTypedArea} explicitLocality={HasExplicitLocality} scope={SearchScope} explicitAreaOverride={ExplicitAreaOverride} contextReused={ContextReused} permissionState={PermissionState} refreshAttempted={RefreshAttempted} refreshOutcome={RefreshOutcome}",
            interpretation.IntentFamily,
            plan.Mode,
            interpretation.Confidence,
            interpretation.PlacesApplicable,
            plan.UseDirectPlacesExecution,
            plan.ShouldUsePlaces && plan.UseDirectPlacesExecution,
            grounding.HasCoordinates,
            grounding.HasTypedArea,
            localDiscovery.HasExplicitLocality,
            scopeResolution.SearchScope,
            scopeResolution.ExplicitAreaOverrodeDeviceLocation,
            contextReadResult.ContextReused,
            ReadMetadataValue(request.Metadata, CompanionLocationMetadataKeys.PermissionState) ?? "none",
            MetadataFlagTrue(request.Metadata, CompanionLocationMetadataKeys.RefreshAttempted),
            ReadMetadataValue(request.Metadata, CompanionLocationMetadataKeys.RefreshOutcome) ?? "none");

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
                grounding,
                localDiscovery,
                RealWorldFailureScenario.ClarificationNeeded,
                exploratory: false,
                clarificationPrompt: plan.ClarificationPrompt,
                requestMetadata: request.Metadata,
                additionalWarnings: scopeResolution.ReasonCodes);
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
                grounding,
                localDiscovery,
                scenario,
                exploratory: false,
                clarificationPrompt: null,
                requestMetadata: request.Metadata,
                additionalWarnings: scopeResolution.ReasonCodes);
        }

        if (plan.UseDirectPlacesExecution)
        {
            return await ExecuteDirectPlacesModeAsync(
                request,
                route,
                plan,
                interpretation,
                grounding,
                localDiscovery,
                scopeResolution,
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
            scopeResolution,
            cancellationToken);
    }

    private async Task<UserChatResponse> ExecuteDirectPlacesModeAsync(
        UserChatRequest request,
        AIModelRoute route,
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        RealWorldSearchScopeResolution scopeResolution,
        CancellationToken cancellationToken)
    {
        var maxTotalItems = 8;
        var maxDomains = plan.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch ? 4 : plan.Mode == RealWorldExecutionMode.FocusedThemeSearch ? 2 : 1;
        var maxItemsPerDomain = plan.Mode == RealWorldExecutionMode.FocusedPlaceSearch
            ? maxTotalItems
            : plan.Mode == RealWorldExecutionMode.FocusedThemeSearch
                ? 4
                : 2;
        var retrievalPlan = new RealWorldPlaceRetrievalPlan(
            Authoritative: plan.UseDirectPlacesExecution,
            HasNearMeSemantic: interpretation.HasNearMeLanguage,
            ExecutionMode: plan.Mode,
            SelectedDomains: plan.SelectedDomains,
            CanonicalConcepts: interpretation.CandidateConcepts,
            RequestedShortlistSize: maxTotalItems);
        var locationContext = BuildPlaceSearchLocationContext(
            scopeResolution,
            plan,
            interpretation,
            maxTotalItems);
        var countryCode = ResolveCountryCode(request.Metadata);
        var execution = await placesExecutionService.ExecuteAsync(
            new RealWorldPlacesExecutionRequest(
                UserQuery: request.UserMessage,
                CountryCode: countryCode,
                LocationContext: locationContext,
                Domains: plan.SelectedDomains,
                MaxDomains: maxDomains,
                MaxItemsPerDomain: maxItemsPerDomain,
                MaxTotalItems: maxTotalItems,
                Mode: plan.Mode,
                RetrievalPlan: retrievalPlan),
            cancellationToken);

        var warningSet = BuildBaseWarnings(plan, interpretation, localDiscovery, grounding, request.Metadata);
        warningSet.UnionWith(scopeResolution.ReasonCodes);
        warningSet.UnionWith(execution.Warnings);
        warningSet.UnionWith(execution.ReasonCodes);

        if (!execution.Succeeded || !execution.HasAnyResults)
        {
            var failureScenario = execution.FailureScenario ?? RealWorldFailureScenario.ProviderRequestFailure;
            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                grounding,
                localDiscovery,
                failureScenario,
                exploratory: plan.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
                clarificationPrompt: null,
                requestMetadata: request.Metadata,
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
        RealWorldSearchScopeResolution scopeResolution,
        CancellationToken cancellationToken)
    {
        var companionQuery = BuildCompanionQuery(request.UserMessage, plan, grounding, localDiscovery);
        var companionMetadata = BuildCompanionMetadata(
            request.Metadata,
            grounding,
            scopeResolution,
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
                grounding,
                localDiscovery,
                RealWorldFailureScenario.InternalRoutingConflict,
                exploratory: false,
                clarificationPrompt: null,
                requestMetadata: request.Metadata,
                additionalWarnings: ["real_world_companion_execution_exception"]);
        }

        var warningSet = BuildBaseWarnings(plan, interpretation, localDiscovery, grounding, request.Metadata);
        warningSet.UnionWith(scopeResolution.ReasonCodes);
        warningSet.UnionWith(companionResponse.Warnings);

        var succeeded = companionResponse.Succeeded || !string.IsNullOrWhiteSpace(companionResponse.ReplyText);
        if (!succeeded || companionResponse.HasInsufficientData)
        {
            var scenario = ClassifyCompanionFailureScenario(companionResponse, plan);
            if (scenario == RealWorldFailureScenario.InternalRoutingConflict)
            {
                warningSet.Add("real_world_route_conflict_detected");
            }

            return BuildFailureResponse(
                route,
                plan,
                interpretation,
                grounding,
                localDiscovery,
                scenario,
                exploratory: plan.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
                clarificationPrompt: null,
                requestMetadata: request.Metadata,
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
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        RealWorldFailureScenario scenario,
        bool exploratory,
        string? clarificationPrompt,
        IReadOnlyDictionary<string, string>? requestMetadata,
        IEnumerable<string>? additionalWarnings = null)
    {
        var fallback = failureMessageBuilder.Build(scenario, exploratory, clarificationPrompt);
        var warningSet = BuildBaseWarnings(
            plan,
            interpretation,
            localDiscovery,
            grounding,
            requestMetadata);
        warningSet.UnionWith(fallback.Warnings);
        if (additionalWarnings is not null)
        {
            warningSet.UnionWith(additionalWarnings);
        }

        NormalizeTerminalWarnings(warningSet, plan, scenario, fallback.Warnings);
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
            : "Here are some nearby options:";

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

    private static CompanionLocationGrounding BuildContextWriteGrounding(
        RealWorldSearchScopeResolution scopeResolution)
    {
        var effective = scopeResolution.EffectiveGrounding;
        var secondaryDevice = scopeResolution.SecondaryDeviceGrounding;
        if (!secondaryDevice.HasCoordinates || string.IsNullOrWhiteSpace(scopeResolution.ExplicitArea))
        {
            return effective;
        }

        return secondaryDevice with
        {
            Source = "explicit_area_over_device",
            TypedArea = scopeResolution.ExplicitArea
        };
    }

    private static PlaceSearchLocationContext? BuildPlaceSearchLocationContext(
        RealWorldSearchScopeResolution scopeResolution,
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        int maxShortlist)
    {
        var grounding = scopeResolution.EffectiveGrounding;
        var localDiscovery = scopeResolution.EffectiveLocalDiscovery;
        if (!scopeResolution.HasUsableScope)
        {
            return null;
        }

        var effectiveTypedArea = CompanionLocationGroundingParser.IsValidAreaHint(scopeResolution.ExplicitArea)
            ? scopeResolution.ExplicitArea
            : CompanionLocationGroundingParser.IsValidAreaHint(grounding.TypedArea)
                ? grounding.TypedArea
                : CompanionLocationGroundingParser.IsValidAreaHint(localDiscovery.LocalityHint)
                    ? localDiscovery.LocalityHint
                    : null;
        var usePrimaryCoordinates = scopeResolution.SearchScope == RealWorldSearchScopeKind.DeviceLocation;
        var deviceGrounding = scopeResolution.SecondaryDeviceGrounding;

        return new PlaceSearchLocationContext(
            Source: scopeResolution.SearchScope == RealWorldSearchScopeKind.ExplicitArea
                ? "explicit_area"
                : grounding.Source,
            Latitude: usePrimaryCoordinates ? grounding.Latitude : null,
            Longitude: usePrimaryCoordinates ? grounding.Longitude : null,
            RadiusMeters: usePrimaryCoordinates ? grounding.RadiusMeters : null,
            TypedArea: effectiveTypedArea,
            LocalityLabel: localDiscovery.LocalityHint
                           ?? grounding.LocalityLabel
                           ?? scopeResolution.ExplicitArea,
            AccuracyBucket: usePrimaryCoordinates ? grounding.AccuracyBucket : null,
            CapturedAtUtc: usePrimaryCoordinates ? grounding.CapturedAtUtc : null,
            PlannerSelectedDomain: plan.SelectedDomains.FirstOrDefault(),
            PlannerSelectedConcept: interpretation.CandidateConcepts.FirstOrDefault(),
            PlannerAuthoritative: plan.UseDirectPlacesExecution,
            HasNearMeSemantic: interpretation.HasNearMeLanguage,
            PlannerExecutionMode: plan.Mode,
            PlannerMaxShortlist: maxShortlist,
            SearchScope: scopeResolution.SearchScope switch
            {
                RealWorldSearchScopeKind.ExplicitArea => "explicit_area",
                RealWorldSearchScopeKind.DeviceLocation => "device_location",
                _ => "none"
            },
            DeviceLatitude: deviceGrounding.HasCoordinates ? deviceGrounding.Latitude : null,
            DeviceLongitude: deviceGrounding.HasCoordinates ? deviceGrounding.Longitude : null,
            DeviceRadiusMeters: deviceGrounding.HasCoordinates ? deviceGrounding.RadiusMeters : null,
            DeviceLocalityLabel: deviceGrounding.LocalityLabel,
            DeviceSource: deviceGrounding.HasCoordinates ? deviceGrounding.Source : null);
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

        if (grounding.HasCoordinates)
        {
            return query;
        }

        if (CompanionLocationGroundingParser.IsValidAreaHint(grounding.TypedArea))
        {
            return CompanionLocationGroundingParser.ApplyTypedAreaToQuery(query, grounding.TypedArea);
        }

        if (localDiscovery.HasExplicitLocality
            && CompanionLocationGroundingParser.IsValidAreaHint(localDiscovery.LocalityHint))
        {
            return CompanionLocationGroundingParser.ApplyTypedAreaToQuery(query, localDiscovery.LocalityHint);
        }

        return query;
    }

    private static IReadOnlyDictionary<string, string> BuildCompanionMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        CompanionLocationGrounding grounding,
        RealWorldSearchScopeResolution scopeResolution,
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
        result["real_world_search_scope"] = scopeResolution.SearchScope switch
        {
            RealWorldSearchScopeKind.ExplicitArea => "explicit_area",
            RealWorldSearchScopeKind.DeviceLocation => "device_location",
            _ => "none"
        };

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

        if (scopeResolution.SecondaryDeviceGrounding.HasCoordinates)
        {
            result["real_world_device_latitude"] =
                scopeResolution.SecondaryDeviceGrounding.Latitude!.Value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
            result["real_world_device_longitude"] =
                scopeResolution.SecondaryDeviceGrounding.Longitude!.Value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(scopeResolution.SecondaryDeviceGrounding.LocalityLabel))
            {
                result["real_world_device_locality_label"] = scopeResolution.SecondaryDeviceGrounding.LocalityLabel!;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildSuggestedStateUpdates(
        CompanionLocationGrounding grounding)
    {
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var source = grounding.Source?.Trim();
        var isUserProvidedAreaSource = string.Equals(source, "typed_area", StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(source, "query_locality", StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(source, "conversation_explicit_area", StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(source, "explicit_area", StringComparison.OrdinalIgnoreCase);
        if (grounding.HasTypedArea && isUserProvidedAreaSource)
        {
            updates["location_preference"] = grounding.TypedArea!;
        }

        return updates;
    }

    private static HashSet<string> BuildBaseWarnings(
        RealWorldExecutionPlan plan,
        RealWorldIntentInterpretation interpretation,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CompanionLocationGrounding grounding,
        IReadOnlyDictionary<string, string>? requestMetadata)
    {
        var groundingSource = ResolveGroundingSourceWarning(grounding, localDiscovery);
        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chat_path_companion_real_world",
            $"real_world_intent_family:{interpretation.IntentFamily.ToString().ToLowerInvariant()}",
            $"real_world_execution_mode:{plan.Mode.ToString().ToLowerInvariant()}",
            $"real_world_confidence:{interpretation.Confidence:0.##}",
            $"real_world_interpreter_source:{interpretation.InterpretationSource.ToString().ToLowerInvariant()}",
            groundingSource
        };

        var hasGroundingMetadata = HasAnyGroundingMetadata(requestMetadata);
        if (hasGroundingMetadata)
        {
            warnings.Add("real_world_grounding_payload_received");
            if (!string.IsNullOrWhiteSpace(ReadMetadataValue(requestMetadata, CompanionLocationMetadataKeys.Latitude))
                && !string.IsNullOrWhiteSpace(ReadMetadataValue(requestMetadata, CompanionLocationMetadataKeys.Longitude)))
            {
                warnings.Add("real_world_ephemeral_location_attached");
            }
        }

        warnings.Add(grounding.HasCoordinates
            ? "real_world_grounding_coordinates_present"
            : "real_world_grounding_coordinates_missing");

        if (grounding.HasTypedArea)
        {
            warnings.Add("real_world_grounding_typed_area_present");
        }

        if (localDiscovery.HasExplicitLocality || !string.IsNullOrWhiteSpace(grounding.LocalityLabel))
        {
            warnings.Add("real_world_grounding_explicit_locality_present");
        }

        var normalizedGroundingSource = groundingSource.Replace("nearby_", "real_world_", StringComparison.Ordinal);
        warnings.Add(normalizedGroundingSource);

        var hasPermissionContext =
            !string.IsNullOrWhiteSpace(ReadMetadataValue(requestMetadata, CompanionLocationMetadataKeys.PermissionState));
        if (hasPermissionContext
            && !grounding.HasCoordinates
            && !grounding.HasTypedArea
            && !localDiscovery.HasExplicitLocality)
        {
            warnings.Add("real_world_grounding_missing_despite_permission_context");
        }

        if (MetadataFlagTrue(requestMetadata, CompanionLocationMetadataKeys.RefreshAttempted)
            && !grounding.HasCoordinates)
        {
            warnings.Add("real_world_grounding_send_without_coordinates_after_refresh_attempt");
        }

        if (plan.ShouldUsePlaces)
        {
            warnings.Add("chat_path_companion_local_places");
            if (plan.UseDirectPlacesExecution)
            {
                warnings.Add("real_world_route_authoritative");
                warnings.Add("real_world_route_legacy_router_bypassed");
            }
            else
            {
                warnings.Add("real_world_route_legacy_router_allowed");
            }
        }

        foreach (var reasonCode in interpretation.ReasonCodes)
        {
            warnings.Add($"real_world_reason:{reasonCode}");
        }

        foreach (var reasonCode in plan.ReasonCodes)
        {
            warnings.Add($"real_world_plan_reason:{reasonCode}");
            if (string.Equals(reasonCode, "real_world_exploratory_execution_enabled_by_context", StringComparison.Ordinal))
            {
                warnings.Add("real_world_exploratory_execution_enabled_by_context");
            }

            if (string.Equals(reasonCode, "real_world_clarify_preserved_due_to_missing_scope", StringComparison.Ordinal))
            {
                warnings.Add("real_world_clarify_preserved_due_to_missing_scope");
            }
        }

        return warnings;
    }

    private static RealWorldFailureScenario ClassifyCompanionFailureScenario(
        FinancialCompanionResponse response,
        RealWorldExecutionPlan plan)
    {
        var reasons = (response.InsufficientDataReasons ?? [])
            .Concat(response.Warnings)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (plan.ShouldUsePlaces
            && (response.Intent == FinancialCompanionIntent.Unsupported
                || reasons.Any(reason =>
                    reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("outside_supported_companion_scope", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("no_supported_financial_intent_detected", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("insufficient_unsupported_query_scope", StringComparison.OrdinalIgnoreCase))))
        {
            return RealWorldFailureScenario.InternalRoutingConflict;
        }

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

        return plan.ShouldUsePlaces
            ? RealWorldFailureScenario.InternalRoutingConflict
            : RealWorldFailureScenario.ProviderUnavailable;
    }

    private static readonly string[] LegacyUnsupportedWarningFragments =
    [
        "route_outside_supported_companion_scope",
        "outside_supported_companion_scope",
        "intent_unsupported",
        "unsupported_query_scope",
        "insufficient_unsupported_query_scope",
        "no_supported_financial_intent_detected"
    ];

    private static void NormalizeTerminalWarnings(
        HashSet<string> warningSet,
        RealWorldExecutionPlan plan,
        RealWorldFailureScenario scenario,
        IReadOnlyList<string> fallbackWarnings)
    {
        var allowedFallbackWarnings = new HashSet<string>(fallbackWarnings, StringComparer.OrdinalIgnoreCase);
        warningSet.RemoveWhere(warning =>
            warning.StartsWith("fallback_", StringComparison.OrdinalIgnoreCase)
            && !allowedFallbackWarnings.Contains(warning));

        if (scenario != RealWorldFailureScenario.ProviderUnavailable)
        {
            warningSet.Remove("fallback_provider_unavailable");
        }

        warningSet.RemoveWhere(warning =>
            warning.StartsWith("real_world_failure_scenario:", StringComparison.OrdinalIgnoreCase));

        if (plan.ShouldUsePlaces && plan.UseDirectPlacesExecution)
        {
            warningSet.RemoveWhere(warning =>
                LegacyUnsupportedWarningFragments.Any(fragment =>
                    warning.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        }
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
            if (string.Equals(grounding.Source, "query_locality", StringComparison.OrdinalIgnoreCase))
            {
                return "nearby_grounding_source:query_locality";
            }

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
        var candidates = new[]
        {
            "chat_location_permission_state",
            "location_permission_state",
            "chat_location_source"
        };

        foreach (var key in candidates)
        {
            var value = ReadMetadataValue(metadata, key);
            if (string.IsNullOrWhiteSpace(value))
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

    private static bool HasAnyGroundingMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return false;
        }

        var keys =
            new[]
            {
                CompanionLocationMetadataKeys.Source,
                CompanionLocationMetadataKeys.Latitude,
                CompanionLocationMetadataKeys.Longitude,
                CompanionLocationMetadataKeys.TypedArea,
                CompanionLocationMetadataKeys.LocalityLabel,
                CompanionLocationMetadataKeys.PermissionState,
                CompanionLocationMetadataKeys.RefreshAttempted,
                CompanionLocationMetadataKeys.RefreshOutcome
            };

        return keys.Any(key => !string.IsNullOrWhiteSpace(ReadMetadataValue(metadata, key)));
    }

    private static bool MetadataFlagTrue(
        IReadOnlyDictionary<string, string>? metadata,
        string key)
    {
        var value = ReadMetadataValue(metadata, key);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadMetadataValue(
        IReadOnlyDictionary<string, string>? metadata,
        string key)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        if (metadata.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
