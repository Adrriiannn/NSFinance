using System.Security.Cryptography;
using System.Text;

namespace NSFinance.Api.Modules.AI.Services;

public interface IModeRouter
{
    Task<ConversationModeExecutionResult> RouteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken);
}

public interface IConversationModeHandler
{
    bool CanHandle(ConversationMode mode, ExplorationSubtype? explorationSubtype);

    Task<ConversationModeExecutionResult> ExecuteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken);
}

public sealed class ModeRouter(
    IEnumerable<IConversationModeHandler> handlers) : IModeRouter
{
    private readonly IReadOnlyList<IConversationModeHandler> handlerList = handlers.ToArray();

    public Task<ConversationModeExecutionResult> RouteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = handlerList.FirstOrDefault(candidate =>
            candidate.CanHandle(request.StrategyDecision.ModeCandidate, request.ExplorationSubtypeDecision?.Subtype));
        if (handler is null)
        {
            throw new InvalidOperationException(
                $"No mode handler is registered for mode {request.StrategyDecision.ModeCandidate} and subtype {request.ExplorationSubtypeDecision?.Subtype}.");
        }

        return handler.ExecuteAsync(request, cancellationToken);
    }
}

public sealed class StructuredExplorationHandler(
    ILocalDiscoveryConstraintExtractor constraintExtractor,
    ILocalDiscoveryQueryShaper queryShaper,
    IPlacesSearchService placesSearchService,
    IResultContextService resultContextService) : IConversationModeHandler
{
    public bool CanHandle(ConversationMode mode, ExplorationSubtype? explorationSubtype)
    {
        return mode == ConversationMode.Exploration
               && explorationSubtype == ExplorationSubtype.Structured;
    }

    public async Task<ConversationModeExecutionResult> ExecuteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extraction = constraintExtractor.Extract(request.Request.UserMessage);
        var grounding = CompanionLocationGroundingParser.Parse(request.ClientMetadata, request.State);
        var mergedConstraints = MergeExplorationConstraintState(
            request.State.Constraints,
            extraction,
            grounding);
        var typedArea = extraction.LocalityHint ?? grounding.TypedArea;
        var locationContext = new PlaceSearchLocationContext(
            Source: grounding.HasCoordinates ? grounding.Source : !string.IsNullOrWhiteSpace(typedArea) ? "typed_area" : null,
            Latitude: grounding.HasCoordinates ? grounding.Latitude : null,
            Longitude: grounding.HasCoordinates ? grounding.Longitude : null,
            RadiusMeters: grounding.HasCoordinates ? grounding.RadiusMeters : null,
            TypedArea: typedArea,
            LocalityLabel: grounding.LocalityLabel ?? typedArea,
            AccuracyBucket: grounding.AccuracyBucket,
            CapturedAtUtc: grounding.CapturedAtUtc);

        var missingConstraints = BuildMissingConstraints(extraction, grounding, request.State);
        var toolReady = request.StrategyDecision.Readiness.To == ConversationReadinessLevel.R4_ToolReady
                        && request.StrategyDecision.ToolExecutionPermission == ToolExecutionPermission.EligibleIfGuardPasses;
        if (!toolReady || missingConstraints.Count > 0)
        {
            warnings.Add("chat.tool.guard_blocked");
            var clarification = BuildStructuredClarificationPrompt(
                missingConstraints,
                extraction,
                grounding,
                request.ClientMetadata,
                request.State);
            var blockedState = request.State with
            {
                ActiveMode = ConversationMode.Conversation,
                ModeCandidate = ConversationMode.Exploration,
                ReadinessLevel = ConversationReadinessLevel.R3_StructuredIncomplete,
                Constraints = mergedConstraints,
                MissingConstraints = missingConstraints,
                LastClarificationPrompt = clarification.Question,
                LastSuggestedOptions = clarification.SuggestedOptions,
                PendingClarification = new PendingClarificationState(
                    Slot: clarification.Slot,
                    PromptIntent: clarification.PromptIntent,
                    KnownPlaceTypes: mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationPlaceTypes, out var placeTypes) ? placeTypes : null,
                    KnownArea: mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var area) ? area : null,
                    KnownTime: mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationTime, out var time) ? time : null,
                    CreatedAtUtc: DateTimeOffset.UtcNow),
                NeedsFollowUp = true
            };

            return new ConversationModeExecutionResult(
                CompositionRequest: new ResponseCompositionRequest(
                    ResponseType: ResponseCompositionType.Clarify,
                    ToneDirective: ResponseToneDirective.Neutral,
                    Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                    Mode: ConversationMode.Conversation,
                    ReadinessLevel: blockedState.ReadinessLevel,
                    UserMessage: request.Request.UserMessage,
                    GroundedData: new GroundedDataEnvelope([], [], warnings.ToArray()),
                    Constraints: blockedState.Constraints,
                    MissingConstraints: blockedState.MissingConstraints ?? [],
                    MaxLengthHint: 450,
                    ClarificationQuestion: blockedState.LastClarificationPrompt,
                    SuggestedOptions: blockedState.LastSuggestedOptions),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: blockedState,
                ResultContext: request.ResultContext,
                Warnings: warnings.ToArray(),
                FollowUpIntentHints: ["clarify_intent", clarification.PromptIntent],
                Succeeded: true);
        }

        var shaped = queryShaper.Shape(request.Request.UserMessage, locationContext, extraction);
        warnings.UnionWith(shaped.ReasonCodes);

        var countryCode = ResolveCountryCode(request.ClientMetadata, request.State);
        var searchResult = await placesSearchService.SearchAsync(
            shaped.Query,
            countryCode,
            locationContext,
            cancellationToken);
        warnings.UnionWith(searchResult.Warnings ?? []);

        if (searchResult.Items.Count == 0)
        {
            warnings.Add("structured_exploration_no_results");
            return BuildStructuredNoResultResponse(request, mergedConstraints, warnings);
        }

        var requestedPlaceTypes = ResolveRequestedPlaceTypes(mergedConstraints, extraction);
        var filteredItems = FilterItemsByRequestedPlaceTypes(searchResult.Items, requestedPlaceTypes);
        if (requestedPlaceTypes.Count > 0)
        {
            warnings.Add(filteredItems.Count > 0
                ? "structured_exploration_place_type_filter_applied"
                : "structured_exploration_place_type_filter_no_match");
        }

        if (requestedPlaceTypes.Count > 0 && filteredItems.Count == 0)
        {
            warnings.Add("structured_exploration_no_results_for_requested_place_type");
            return BuildStructuredNoResultResponse(request, mergedConstraints, warnings);
        }

        var shortlistSource = requestedPlaceTypes.Count > 0
            ? filteredItems
            : searchResult.Items;
        var selectedItems = SelectStructuredShortlist(shortlistSource, 8);
        var normalizedConstraints = BuildNormalizedConstraints(
            mergedConstraints,
            extraction,
            grounding);
        var selectedEntities = selectedItems
            .Select((item, index) => new ConversationSuggestedEntity(item.PlaceId, item.Name, index + 1, item.GoogleMapsUri))
            .ToArray();
        ResultContextSnapshot? persistedResultContext = request.ResultContext;
        ConversationStateSnapshot nextState = request.State with
        {
            ActiveMode = ConversationMode.Exploration,
            ModeCandidate = ConversationMode.Exploration,
            ReadinessLevel = ConversationReadinessLevel.R4_ToolReady,
            Constraints = mergedConstraints,
            MissingConstraints = [],
            LastSuggestedEntities = selectedEntities,
            PendingClarification = null,
            NeedsFollowUp = true
        };
        if (request.Request.UserId.HasValue && request.Request.ConversationThreadId.HasValue)
        {
            var write = await resultContextService.WriteAsync(
                new ResultContextWriteRequest(
                    UserId: request.Request.UserId.Value,
                    ConversationThreadId: request.Request.ConversationThreadId.Value,
                    SourceMode: ConversationMode.Exploration,
                    SourceSubtype: ExplorationSubtype.Structured,
                    QueryFingerprint: BuildQueryFingerprint(shaped.Query, extraction),
                    NormalizedConstraints: normalizedConstraints,
                    SuggestedEntities: selectedItems
                        .Select((item, index) => new ResultContextEntity(
                            EntityId: item.PlaceId,
                            Label: item.Name,
                            Rank: index + 1,
                            StableReference: item.GoogleMapsUri,
                            Category: item.Category))
                        .ToArray(),
                    SelectedEntityId: null,
                    ParentResultSetId: request.ResultContext?.ResultSetId,
                    BranchRootResultSetId: request.ResultContext?.BranchRootResultSetId,
                    CreatedUtc: DateTime.UtcNow),
                cancellationToken);
            persistedResultContext = write.Snapshot;
            nextState = nextState with
            {
                LastSuggestedEntities = write.Snapshot.SuggestedEntities
                    .Select(item => new ConversationSuggestedEntity(item.EntityId, item.Label, item.Rank, item.StableReference))
                    .ToArray(),
                ResultContextRef = write.Reference,
                LastExecutionFingerprint = write.Snapshot.QueryFingerprint,
                NeedsFollowUp = true
            };
        }

        var groundedData = new GroundedDataEnvelope(
            Entities: selectedEntities,
            SummaryFacts: BuildStructuredFacts(selectedItems),
            Warnings: warnings.ToArray());

        return new ConversationModeExecutionResult(
            CompositionRequest: new ResponseCompositionRequest(
                ResponseType: ResponseCompositionType.ResultSummary,
                ToneDirective: ResponseToneDirective.Neutral,
                Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                Mode: ConversationMode.Exploration,
                ReadinessLevel: ConversationReadinessLevel.R4_ToolReady,
                UserMessage: request.Request.UserMessage,
                GroundedData: groundedData,
                Constraints: nextState.Constraints,
                MissingConstraints: [],
                MaxLengthHint: 700,
                ClarificationQuestion: null,
                SuggestedOptions: ["Refine the shortlist", "Compare options"]),
            DeterministicReplyText: null,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            State: nextState,
            ResultContext: persistedResultContext,
            Warnings: warnings.ToArray(),
            FollowUpIntentHints: ["refine_place_preferences", "compare_options"],
            Succeeded: true);
    }

    private static IReadOnlyList<string> BuildMissingConstraints(
        LocalDiscoveryConstraintExtractionResult extraction,
        CompanionLocationGrounding grounding,
        ConversationStateSnapshot state)
    {
        var missing = new List<string>();
        var hasKnownPlaceTypes = extraction.PlaceTypeHints.Count > 0
                                 || state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationPlaceTypes, out var existingPlaceTypes)
                                 && !string.IsNullOrWhiteSpace(existingPlaceTypes);
        if (!hasKnownPlaceTypes)
        {
            missing.Add("place_type");
        }

        var hasPersistentArea = state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var existingArea)
                                && !string.IsNullOrWhiteSpace(existingArea)
                                && !string.Equals(existingArea, "near_me", StringComparison.OrdinalIgnoreCase);
        var hasKnownArea = grounding.HasCoordinates
                           || grounding.HasTypedArea
                           || extraction.HasExplicitLocality
                           || hasPersistentArea;
        if (!hasKnownArea)
        {
            missing.Add("area_or_location");
        }

        return missing;
    }

    private static ConversationModeExecutionResult BuildStructuredNoResultResponse(
        ConversationModeRequest request,
        IReadOnlyDictionary<string, string> mergedConstraints,
        ISet<string> warnings)
    {
        return new ConversationModeExecutionResult(
            CompositionRequest: new ResponseCompositionRequest(
                ResponseType: ResponseCompositionType.Fallback,
                ToneDirective: ResponseToneDirective.Neutral,
                Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                Mode: ConversationMode.Exploration,
                ReadinessLevel: request.State.ReadinessLevel,
                UserMessage: request.Request.UserMessage,
                GroundedData: new GroundedDataEnvelope([], [], warnings.ToArray()),
                Constraints: mergedConstraints,
                MissingConstraints: [],
                MaxLengthHint: 420,
                ClarificationQuestion: "I didn't get strong matches yet. Want to tighten the place type, area, or vibe?",
                SuggestedOptions: ["Tighter place type", "Different area", "Different vibe"]),
            DeterministicReplyText: null,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            State: request.State with
            {
                ActiveMode = ConversationMode.Exploration,
                ModeCandidate = ConversationMode.Exploration,
                ReadinessLevel = ConversationReadinessLevel.R3_StructuredIncomplete,
                Constraints = mergedConstraints,
                PendingClarification = null,
                NeedsFollowUp = true
            },
            ResultContext: request.ResultContext,
            Warnings: warnings.ToArray(),
            FollowUpIntentHints: ["refine_place_preferences"],
            Succeeded: true);
    }

    private static StructuredClarificationPrompt BuildStructuredClarificationPrompt(
        IReadOnlyList<string> missingConstraints,
        LocalDiscoveryConstraintExtractionResult extraction,
        CompanionLocationGrounding grounding,
        IReadOnlyDictionary<string, string> metadata,
        ConversationStateSnapshot state)
    {
        var missingPlaceType = missingConstraints.Contains("place_type", StringComparer.OrdinalIgnoreCase);
        var missingLocation = missingConstraints.Contains("area_or_location", StringComparer.OrdinalIgnoreCase);
        var hasSearchableLocation = grounding.HasCoordinates
                                    || grounding.HasTypedArea
                                    || extraction.HasExplicitLocality
                                    || state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var knownArea)
                                    && !string.IsNullOrWhiteSpace(knownArea)
                                    && !string.Equals(knownArea, "near_me", StringComparison.OrdinalIgnoreCase);

        if (missingPlaceType && (!missingLocation || hasSearchableLocation))
        {
            return new StructuredClarificationPrompt(
                Slot: ClarificationSlot.ExplorationPlaceType,
                PromptIntent: "exploration_missing_place_type",
                Question: "I can search near you. What would you like me to look for?",
                SuggestedOptions: ["Coffee shops", "Restaurants", "Shops", "Parks"]);
        }

        if (missingLocation)
        {
            var permissionState = ResolveLocationPermissionState(metadata);
            return permissionState switch
            {
                "denied_open_settings" or "unavailable" => new StructuredClarificationPrompt(
                    Slot: ClarificationSlot.ExplorationLocation,
                    PromptIntent: "location_permission_denied_or_unavailable",
                    Question: "I can't use your current location right now. Enable location in settings, or tell me an area to search.",
                    SuggestedOptions: ["Allow location", "Specify area"]),
                "denied_can_ask_again" or "unknown" => new StructuredClarificationPrompt(
                    Slot: ClarificationSlot.ExplorationLocation,
                    PromptIntent: "location_permission_prompt",
                    Question: "I can do that. I don't have your location yet. Do you want to search near your current location or in a specific area?",
                    SuggestedOptions: ["Use current location", "Specify area"]),
                _ => new StructuredClarificationPrompt(
                    Slot: ClarificationSlot.ExplorationLocation,
                    PromptIntent: "location_missing_fix",
                    Question: "I can search as soon as location is set. Do you want to use your current location or specify an area?",
                    SuggestedOptions: ["Use current location", "Specify area"])
            };
        }

        return new StructuredClarificationPrompt(
            Slot: ClarificationSlot.ExplorationRefinement,
            PromptIntent: "exploration_refine",
            Question: "What should I refine first?",
            SuggestedOptions: ["Distance", "Parking", "Open now"]);
    }

    private static string ResolveLocationPermissionState(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue(CompanionLocationMetadataKeys.PermissionState, out var exactValue)
            && !string.IsNullOrWhiteSpace(exactValue))
        {
            return exactValue.Trim().ToLowerInvariant();
        }

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, CompanionLocationMetadataKeys.PermissionState, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value.Trim().ToLowerInvariant();
            }
        }

        return "unknown";
    }

    private static IReadOnlyDictionary<string, string> MergeExplorationConstraintState(
        IReadOnlyDictionary<string, string> existing,
        LocalDiscoveryConstraintExtractionResult extraction,
        CompanionLocationGrounding grounding)
    {
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        if (extraction.PlaceTypeHints.Count > 0)
        {
            merged[ConversationConstraintKeys.ExplorationPlaceTypes] = string.Join('|', extraction.PlaceTypeHints);
        }

        var area = extraction.HasExplicitLocality && !string.IsNullOrWhiteSpace(extraction.LocalityHint)
            ? extraction.LocalityHint!.Trim()
            : extraction.HasNearMeLanguage
                ? "near_me"
                : grounding.HasTypedArea
                    ? grounding.TypedArea!.Trim()
                    : null;
        if (!string.IsNullOrWhiteSpace(area))
        {
            merged[ConversationConstraintKeys.ExplorationArea] = area;
        }

        if (extraction.TimeHints.Count > 0)
        {
            merged[ConversationConstraintKeys.ExplorationTime] = string.Join('|', extraction.TimeHints);
        }

        return merged;
    }

    private sealed record StructuredClarificationPrompt(
        ClarificationSlot Slot,
        string PromptIntent,
        string Question,
        IReadOnlyList<string> SuggestedOptions);

    private static IReadOnlyList<PlaceSearchItem> SelectStructuredShortlist(
        IReadOnlyList<PlaceSearchItem> items,
        int maxCount)
    {
        if (items.Count == 0 || maxCount <= 0)
        {
            return [];
        }

        return items
            .Where(static item => !string.IsNullOrWhiteSpace(item.PlaceId) && !string.IsNullOrWhiteSpace(item.Name))
            .Take(maxCount)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveRequestedPlaceTypes(
        IReadOnlyDictionary<string, string> mergedConstraints,
        LocalDiscoveryConstraintExtractionResult extraction)
    {
        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationPlaceTypes, out var encoded)
            && !string.IsNullOrWhiteSpace(encoded))
        {
            return encoded
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeTypeToken)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return extraction.PlaceTypeHints
            .Select(NormalizeTypeToken)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PlaceSearchItem> FilterItemsByRequestedPlaceTypes(
        IReadOnlyList<PlaceSearchItem> items,
        IReadOnlyList<string> requestedPlaceTypes)
    {
        if (items.Count == 0 || requestedPlaceTypes.Count == 0)
        {
            return [];
        }

        return items
            .Where(item => MatchesRequestedPlaceType(item, requestedPlaceTypes))
            .ToArray();
    }

    private static bool MatchesRequestedPlaceType(
        PlaceSearchItem item,
        IReadOnlyList<string> requestedPlaceTypes)
    {
        var candidateTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryType = NormalizeTypeToken(item.PrimaryType);
        if (!string.IsNullOrWhiteSpace(primaryType))
        {
            candidateTypes.Add(primaryType);
        }

        if (item.Types is { Count: > 0 })
        {
            foreach (var value in item.Types)
            {
                var normalized = NormalizeTypeToken(value);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    candidateTypes.Add(normalized);
                }
            }
        }

        if (candidateTypes.Count == 0)
        {
            return false;
        }

        foreach (var requested in requestedPlaceTypes)
        {
            var normalizedRequested = NormalizeTypeToken(requested);
            if (string.IsNullOrWhiteSpace(normalizedRequested))
            {
                continue;
            }

            var requestedFamily = BuildRequestedTypeFamily(normalizedRequested);
            if (!string.IsNullOrWhiteSpace(primaryType))
            {
                if (IsTypeFamilyMatch(primaryType, requestedFamily))
                {
                    return true;
                }

                // If Google provides a primary type and it doesn't match the requested family,
                // don't allow secondary labels to override it.
                continue;
            }

            if (candidateTypes.Any(candidateType => IsTypeFamilyMatch(candidateType, requestedFamily)))
            {
                return true;
            }

            if (IsCoffeeFamily(requestedFamily)
                && item.ServesCoffee == true
                && HasCoffeeSignal(item.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCoffeeFamily(ISet<string> requestedFamily)
    {
        return requestedFamily.Any(value =>
            string.Equals(value, "cafe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "coffee_shop", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCoffeeSignal(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
               && (name.Contains("coffee", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("cafe", StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> BuildRequestedTypeFamily(string requestedType)
    {
        var family = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            requestedType
        };

        if (string.Equals(requestedType, "cafe", StringComparison.OrdinalIgnoreCase))
        {
            family.Add("coffee_shop");
        }
        else if (string.Equals(requestedType, "coffee_shop", StringComparison.OrdinalIgnoreCase))
        {
            family.Add("cafe");
        }

        if (requestedType.EndsWith("_store", StringComparison.OrdinalIgnoreCase))
        {
            family.Add("store");
        }

        return family;
    }

    private static bool IsTypeFamilyMatch(string candidateType, ISet<string> requestedFamily)
    {
        if (requestedFamily.Contains(candidateType))
        {
            return true;
        }

        foreach (var requestedType in requestedFamily)
        {
            if (candidateType.EndsWith($"_{requestedType}", StringComparison.OrdinalIgnoreCase)
                || requestedType.EndsWith($"_{candidateType}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTypeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        return normalized;
    }

    private static IReadOnlyDictionary<string, string> BuildNormalizedConstraints(
        IReadOnlyDictionary<string, string> mergedConstraints,
        LocalDiscoveryConstraintExtractionResult extraction,
        CompanionLocationGrounding grounding)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationPlaceTypes, out var placeTypes)
            && !string.IsNullOrWhiteSpace(placeTypes))
        {
            result["place_types"] = placeTypes;
        }
        else if (extraction.PlaceTypeHints.Count > 0)
        {
            result["place_types"] = string.Join('|', extraction.PlaceTypeHints);
        }

        if (extraction.PreferenceHints.Count > 0)
        {
            result["preference_hints"] = string.Join('|', extraction.PreferenceHints);
        }

        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationTime, out var timeHints)
            && !string.IsNullOrWhiteSpace(timeHints))
        {
            result["time_hints"] = timeHints;
        }
        else if (extraction.TimeHints.Count > 0)
        {
            result["time_hints"] = string.Join('|', extraction.TimeHints);
        }

        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var area)
            && !string.IsNullOrWhiteSpace(area))
        {
            result["typed_area"] = area;
        }
        else if (grounding.HasTypedArea)
        {
            result["typed_area"] = grounding.TypedArea!;
        }

        return result;
    }

    private static string BuildQueryFingerprint(
        string shapedQuery,
        LocalDiscoveryConstraintExtractionResult extraction)
    {
        var raw = $"{shapedQuery}|{string.Join(',', extraction.PlaceTypeHints)}|{string.Join(',', extraction.PreferenceHints)}|{string.Join(',', extraction.TimeHints)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static IReadOnlyList<GroundedDataPoint> BuildStructuredFacts(IReadOnlyList<PlaceSearchItem> selectedItems)
    {
        return selectedItems
            .Select(item => new GroundedDataPoint(
                Label: item.Name,
                Value: BuildPlaceFact(item)))
            .ToArray();
    }

    private static string BuildPlaceFact(PlaceSearchItem item)
    {
        var parts = new List<string>();
        var category = ResolveCategoryFromPlaces(item);
        if (!string.IsNullOrWhiteSpace(category))
        {
            parts.Add(category);
        }

        if (item.Rating.HasValue)
        {
            parts.Add($"rating {item.Rating.Value:0.0}");
        }

        if (!string.IsNullOrWhiteSpace(item.ShortFormattedAddress))
        {
            parts.Add(item.ShortFormattedAddress!);
        }
        else if (!string.IsNullOrWhiteSpace(item.FormattedAddress))
        {
            parts.Add(item.FormattedAddress!);
        }

        if (item.OpeningHours?.OpenNow == true)
        {
            parts.Add("open now");
        }

        return string.Join(", ", parts);
    }

    private static string? ResolveCategoryFromPlaces(PlaceSearchItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.PrimaryTypeDisplayName))
        {
            return item.PrimaryTypeDisplayName.Trim();
        }

        var normalizedPrimaryType = NormalizeTypeToken(item.PrimaryType);
        if (!string.IsNullOrWhiteSpace(normalizedPrimaryType))
        {
            return HumanizeTypeToken(normalizedPrimaryType);
        }

        var bestType = item.Types?
            .Select(NormalizeTypeToken)
            .Where(static type => !string.IsNullOrWhiteSpace(type))
            .OrderByDescending(GetTypeSpecificityScore)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(bestType))
        {
            return HumanizeTypeToken(bestType);
        }

        return string.IsNullOrWhiteSpace(item.Category)
            ? null
            : item.Category.Trim();
    }

    private static int GetTypeSpecificityScore(string typeToken)
    {
        var tokenCount = typeToken.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var lengthScore = Math.Min(typeToken.Length / 8, 4);
        var genericPenalty = typeToken switch
        {
            "point_of_interest" => 4,
            "establishment" => 4,
            "food" => 2,
            _ => 0
        };

        return (tokenCount * 3) + lengthScore - genericPenalty;
    }

    private static string HumanizeTypeToken(string typeToken)
    {
        var words = typeToken
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CapitalizeWord)
            .ToArray();
        return words.Length == 0
            ? typeToken
            : string.Join(' ', words);
    }

    private static string CapitalizeWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return $"{char.ToUpperInvariant(value[0])}{value[1..].ToLowerInvariant()}";
    }

    private static string ResolveCountryCode(
        IReadOnlyDictionary<string, string> metadata,
        ConversationStateSnapshot state)
    {
        var keys = new[] { "chat_country_code", "chat_country", "countryCode", "country" };
        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().ToUpperInvariant();
            }
        }

        if (state.Constraints.TryGetValue("country", out var stateCountry)
            && !string.IsNullOrWhiteSpace(stateCountry))
        {
            return stateCountry.Trim().ToUpperInvariant();
        }

        return "IE";
    }
}

public sealed class OpenExplorationHandler : IConversationModeHandler
{
    public bool CanHandle(ConversationMode mode, ExplorationSubtype? explorationSubtype)
    {
        return mode == ConversationMode.Exploration
               && explorationSubtype == ExplorationSubtype.Open;
    }

    public Task<ConversationModeExecutionResult> ExecuteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var suggestions = BuildStrategicSuggestions(request.Request.UserMessage);
        var groundedData = new GroundedDataEnvelope(
            Entities: suggestions
                .Select((item, index) => new ConversationSuggestedEntity(
                    EntityId: $"open-{index + 1}",
                    Label: item.Label,
                    Rank: index + 1))
                .ToArray(),
            SummaryFacts: suggestions
                .Select(item => new GroundedDataPoint(item.Label, item.Why))
                .ToArray(),
            Warnings: ["open_exploration_phase1_reasoning_only"]);

        var nextState = request.State with
        {
            ActiveMode = ConversationMode.Exploration,
            ModeCandidate = ConversationMode.Exploration,
            ReadinessLevel = ConversationReadinessLevel.R2_DirectionKnown,
            PendingClarification = null,
            NeedsFollowUp = true
        };

        return Task.FromResult(
            new ConversationModeExecutionResult(
                CompositionRequest: new ResponseCompositionRequest(
                    ResponseType: ResponseCompositionType.Suggest,
                    ToneDirective: ResponseToneDirective.Supportive,
                    Strategy: ConversationBehaviorStrategy.GeneralGuidance,
                    Mode: ConversationMode.Exploration,
                    ReadinessLevel: nextState.ReadinessLevel,
                    UserMessage: request.Request.UserMessage,
                    GroundedData: groundedData,
                    Constraints: nextState.Constraints,
                    MissingConstraints: [],
                    MaxLengthHint: 650,
                    ClarificationQuestion: null,
                    SuggestedOptions: ["Make it more concrete", "Add an area", "Ask for a shortlist"]),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: nextState,
                ResultContext: request.ResultContext,
                Warnings: ["open_exploration_phase1_reasoning_only"],
                FollowUpIntentHints: ["make_exploration_concrete", "provide_typed_location"],
                Succeeded: true));
    }

    private static IReadOnlyList<(string Label, string Why)> BuildStrategicSuggestions(string userMessage)
    {
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("quiet", StringComparison.Ordinal)
            || normalized.Contains("calm", StringComparison.Ordinal))
        {
            return
            [
                ("Large park edge or riverside path", "Usually better for quiet walking, visibility, and easier exits if it gets busy."),
                ("Waterfront promenade near residential zones", "Often gives open sightlines, evening lighting, and a calmer pace than city-centre hotspots."),
                ("Botanic garden or campus green corridor", "These tend to feel intentional, accessible, and less noisy than generic 'things to do' areas.")
            ];
        }

        if (normalized.Contains("safe", StringComparison.Ordinal)
            || normalized.Contains("night", StringComparison.Ordinal)
            || normalized.Contains("lighting", StringComparison.Ordinal))
        {
            return
            [
                ("Busy but not chaotic waterfront or town-centre promenade", "Better lighting, foot traffic, and transport options help reduce the 'isolated but dark' tradeoff."),
                ("Well-used park perimeter near cafes or transit", "A park edge can preserve atmosphere while keeping sightlines and easy fallback routes."),
                ("Cultural quarter with evening foot traffic", "These areas often balance ambience with visibility and access to nearby help or transport.")
            ];
        }

        return
        [
            ("Waterfront walk", "Good default for openness, orientation, and easy progression if you want atmosphere without overcommitting."),
            ("Large park with multiple entrances", "Better for flexible pacing, easy exit points, and adapting the plan once you arrive."),
            ("Neighbourhood high street with cafes and side streets", "Useful when you want options, transport, and the ability to pivot into something more concrete.")
        ];
    }
}

public sealed class FinancialModeHandler : IConversationModeHandler
{
    public bool CanHandle(ConversationMode mode, ExplorationSubtype? explorationSubtype)
    {
        return mode == ConversationMode.Financial;
    }

    public Task<ConversationModeExecutionResult> ExecuteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nextState = request.State with
        {
            ActiveMode = ConversationMode.Financial,
            ModeCandidate = ConversationMode.Financial,
            ReadinessLevel = ConversationReadinessLevel.R2_DirectionKnown,
            PendingClarification = null,
            NeedsFollowUp = true
        };

        return Task.FromResult(
            new ConversationModeExecutionResult(
                CompositionRequest: new ResponseCompositionRequest(
                    ResponseType: ResponseCompositionType.Placeholder,
                    ToneDirective: ResponseToneDirective.Supportive,
                    Strategy: ConversationBehaviorStrategy.FinancialPlaceholderTransition,
                    Mode: ConversationMode.Financial,
                    ReadinessLevel: nextState.ReadinessLevel,
                    UserMessage: request.Request.UserMessage,
                    GroundedData: new GroundedDataEnvelope([], [], ["financial_mode_placeholder"]),
                    Constraints: nextState.Constraints,
                    MissingConstraints: nextState.MissingConstraints ?? [],
                    MaxLengthHint: 420,
                    ClarificationQuestion: null,
                    SuggestedOptions: ["Review subscriptions", "Look at spending patterns", "Set a specific budget goal"]),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: nextState,
                ResultContext: request.ResultContext,
                Warnings: ["financial_mode_placeholder"],
                FollowUpIntentHints: ["financial_focus_selection"],
                Succeeded: true));
    }
}

public sealed class GeneralKnowledgeModeHandler : IConversationModeHandler
{
    public bool CanHandle(ConversationMode mode, ExplorationSubtype? explorationSubtype)
    {
        return mode == ConversationMode.GeneralKnowledge;
    }

    public Task<ConversationModeExecutionResult> ExecuteAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nextState = request.State with
        {
            ActiveMode = ConversationMode.GeneralKnowledge,
            ModeCandidate = ConversationMode.GeneralKnowledge,
            PendingClarification = null,
            NeedsFollowUp = false
        };

        return Task.FromResult(
            new ConversationModeExecutionResult(
                CompositionRequest: new ResponseCompositionRequest(
                    ResponseType: ResponseCompositionType.Direct,
                    ToneDirective: ResponseToneDirective.Concise,
                    Strategy: request.StrategyDecision.Strategy,
                    Mode: ConversationMode.GeneralKnowledge,
                    ReadinessLevel: request.StrategyDecision.Readiness.To,
                    UserMessage: request.Request.UserMessage,
                    GroundedData: new GroundedDataEnvelope([], [], []),
                    Constraints: nextState.Constraints,
                    MissingConstraints: [],
                    MaxLengthHint: 550),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: nextState,
                ResultContext: request.ResultContext,
                Warnings: [],
                FollowUpIntentHints: [],
                Succeeded: true));
    }
}
