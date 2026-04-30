using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Options;

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
    IResultContextService resultContextService,
    IOptions<AIIntegrationOptions>? options = null,
    IGooglePlacesPhotoService? photoService = null) : IConversationModeHandler
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
        var interpretation = TurnInterpretationMetadataMapper.ReadInterpretation(request.ClientMetadata);
        var retrievalPlan = NormalizeRetrievalPlan(
            TurnInterpretationMetadataMapper.ReadRetrievalPlan(request.ClientMetadata),
            request.Request.UserMessage,
            extraction,
            constraintExtractor,
            warnings);
        var grounding = CompanionLocationGroundingParser.Parse(request.ClientMetadata, request.State);
        var explorationTtlMinutes = Math.Max(
            5,
            options?.Value.Architecture.ExplorationConstraintTtlMinutes ?? 30);
        var rememberedArea = TryResolveFreshAreaFromState(
            request.State,
            explorationTtlMinutes);
        var mergedConstraints = MergeExplorationConstraintState(
            request.State.Constraints,
            extraction,
            grounding,
            retrievalPlan,
            rememberedArea);
        var typedArea = extraction.LocalityHint
                        ?? grounding.TypedArea
                        ?? retrievalPlan?.ResolvedAreaHint
                        ?? rememberedArea;
        if (!string.IsNullOrWhiteSpace(rememberedArea))
        {
            warnings.Add("structured_exploration_reused_recent_area");
        }
        var locationContext = new PlaceSearchLocationContext(
            Source: grounding.HasCoordinates ? grounding.Source : !string.IsNullOrWhiteSpace(typedArea) ? "typed_area" : null,
            Latitude: grounding.HasCoordinates ? grounding.Latitude : null,
            Longitude: grounding.HasCoordinates ? grounding.Longitude : null,
            RadiusMeters: grounding.HasCoordinates ? grounding.RadiusMeters : null,
            TypedArea: typedArea,
            LocalityLabel: grounding.LocalityLabel ?? typedArea,
            AccuracyBucket: grounding.AccuracyBucket,
            CapturedAtUtc: grounding.CapturedAtUtc,
            PlannerSelectedDomain: retrievalPlan?.SelectedDomain,
            PlannerSelectedConcept: retrievalPlan?.CanonicalConcept,
            PlannerIntentFamily: retrievalPlan?.IntentFamily,
            PlannerAuthoritative: retrievalPlan?.PlannerAuthoritative == true,
            HasNearMeSemantic: retrievalPlan?.NearMeSemantic == true || interpretation?.LocationPlan.NearMeSemantic == true,
            PlannerExecutionMode: retrievalPlan?.SearchScope == "brand_first"
                ? RealWorldExecutionMode.FocusedPlaceSearch
                : RealWorldExecutionMode.FocusedThemeSearch,
            SearchScope: retrievalPlan?.SearchScope,
            PlannerBrandTerm: retrievalPlan?.BrandTerm,
            PlannerCanonicalConcept: retrievalPlan?.CanonicalConcept,
            PlannerIncludeTypes: retrievalPlan?.IncludedTypes,
            PlannerExcludeTypes: retrievalPlan?.ExcludedTypes);

        var missingConstraints = BuildMissingConstraints(
            extraction,
            grounding,
            request.State,
            interpretation,
            retrievalPlan,
            rememberedArea);
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
                    ClarificationQuestion: null,
                    SuggestedOptions: []),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: blockedState,
                ResultContext: request.ResultContext,
                Warnings: warnings.ToArray(),
                FollowUpIntentHints: ["clarify_intent", clarification.PromptIntent],
                Succeeded: true);
        }

        var brandFirstEnabled = options?.Value.Architecture.PlacesBrandFirstEnabled ?? true;
        var querySeed = brandFirstEnabled && !string.IsNullOrWhiteSpace(retrievalPlan?.BrandTerm)
            ? retrievalPlan.BrandTerm!
            : !string.IsNullOrWhiteSpace(retrievalPlan?.CanonicalConcept)
                ? retrievalPlan.CanonicalConcept!
                : request.Request.UserMessage;
        var shaped = queryShaper.Shape(querySeed, locationContext, extraction);
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
            grounding,
            retrievalPlan,
            rememberedArea);
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
                            Category: ResolveCategoryFromPlaces(item),
                            Attributes: BuildResultContextAttributes(item, locationContext)))
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
        var structuredResults = BuildStructuredPlaceResults(selectedItems, locationContext, photoService);

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
                MaxLengthHint: structuredResults is null ? 700 : 240,
                ClarificationQuestion: null,
                SuggestedOptions: []),
            DeterministicReplyText: null,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            State: nextState,
            ResultContext: persistedResultContext,
            Warnings: warnings.ToArray(),
            FollowUpIntentHints: ["refine_place_preferences", "compare_options"],
            Succeeded: true,
            StructuredResults: structuredResults);
    }

    private static PlaceRetrievalPlanV1? NormalizeRetrievalPlan(
        PlaceRetrievalPlanV1? retrievalPlan,
        string userMessage,
        LocalDiscoveryConstraintExtractionResult originalExtraction,
        ILocalDiscoveryConstraintExtractor constraintExtractor,
        ISet<string> warnings)
    {
        if (retrievalPlan is null || string.IsNullOrWhiteSpace(retrievalPlan.CanonicalConcept))
        {
            return retrievalPlan;
        }

        if (IsReliableCanonicalConcept(
                retrievalPlan.CanonicalConcept!,
                userMessage,
                originalExtraction,
                constraintExtractor))
        {
            return retrievalPlan;
        }

        warnings.Add("structured_exploration_unreliable_canonical_ignored");
        return retrievalPlan with
        {
            CanonicalConcept = null,
            ReasonCodes = retrievalPlan.ReasonCodes
                .Concat(["unreliable_canonical_ignored"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static bool IsReliableCanonicalConcept(
        string canonicalConcept,
        string userMessage,
        LocalDiscoveryConstraintExtractionResult originalExtraction,
        ILocalDiscoveryConstraintExtractor constraintExtractor)
    {
        var candidate = canonicalConcept.Trim();
        if (candidate.Length < 2)
        {
            return false;
        }

        var candidateExtraction = constraintExtractor.Extract(candidate);
        if (candidateExtraction.IsLocalDiscoveryCandidate
            || candidateExtraction.PlaceTypeHints.Count > 0
            || candidateExtraction.PreferenceHints.Count > 0)
        {
            return true;
        }

        var candidateTokens = ExtractConceptTokens(candidate);
        if (candidateTokens.Count == 0)
        {
            return false;
        }

        if (originalExtraction.PlaceTypeHints.Count == 0)
        {
            return true;
        }

        var originalTokens = ExtractConceptTokens(userMessage);
        return originalTokens.Count == 0 || candidateTokens.Overlaps(originalTokens);
    }

    private static HashSet<string> ExtractConceptTokens(string? value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        var normalized = value
            .ToLowerInvariant()
            .Replace("i'd", " ")
            .Replace("i'm", " ");
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = token.Trim(' ', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']');
            if (cleaned.Length < 3 || IsConversationFillerToken(cleaned))
            {
                continue;
            }

            result.Add(cleaned);
        }

        return result;
    }

    private static bool IsConversationFillerToken(string token)
    {
        return token is "like"
            or "see"
            or "show"
            or "some"
            or "please"
            or "near"
            or "nearby"
            or "around"
            or "find"
            or "would"
            or "could"
            or "should"
            or "want"
            or "need"
            or "looking";
    }

    private static IReadOnlyList<string> BuildMissingConstraints(
        LocalDiscoveryConstraintExtractionResult extraction,
        CompanionLocationGrounding grounding,
        ConversationStateSnapshot state,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        string? rememberedArea = null)
    {
        var missing = new List<string>();
        var hasKnownPlaceTypes = extraction.PlaceTypeHints.Count > 0
                                 || state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationPlaceTypes, out var existingPlaceTypes)
                                 && !string.IsNullOrWhiteSpace(existingPlaceTypes);
        var interpretedTypes = retrievalPlan?.IncludedTypes
                               ?? interpretation?.PlacePlan.IncludeTypes
                               ?? [];
        var hasBrandSignal = !string.IsNullOrWhiteSpace(retrievalPlan?.BrandTerm)
                             || interpretation?.PlacePlan.BrandOrEntityTerms.Count > 0;
        var hasConceptSignal = !string.IsNullOrWhiteSpace(retrievalPlan?.CanonicalConcept)
                               || !string.IsNullOrWhiteSpace(interpretation?.PlacePlan.CanonicalConcept);
        var missingTargetByInterpretation = interpretation?.ActionType == TurnInterpretationActionType.MissingTarget;
        if ((!hasKnownPlaceTypes && interpretedTypes.Count == 0 && !hasBrandSignal && !hasConceptSignal)
            || missingTargetByInterpretation)
        {
            missing.Add("place_type");
        }

        var hasPersistentArea = state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var existingArea)
                                && !string.IsNullOrWhiteSpace(existingArea)
                                && !string.Equals(existingArea, "near_me", StringComparison.OrdinalIgnoreCase);
        var hasInterpretedArea = !string.IsNullOrWhiteSpace(retrievalPlan?.ResolvedAreaHint)
                                 || !string.IsNullOrWhiteSpace(interpretation?.LocationPlan.ResolvedAreaHint)
                                 || !string.IsNullOrWhiteSpace(interpretation?.LocationPlan.ExplicitAreaText);
        var hasKnownArea = grounding.HasCoordinates
                           || grounding.HasTypedArea
                           || extraction.HasExplicitLocality
                           || hasPersistentArea
                           || !string.IsNullOrWhiteSpace(rememberedArea)
                           || hasInterpretedArea;
        var requiresLocation = retrievalPlan?.RequiresLocation
                               ?? interpretation?.LocationPlan.RequiresLocation
                               ?? hasKnownPlaceTypes
                               || hasBrandSignal
                               || hasConceptSignal;
        var missingLocationByInterpretation = interpretation?.ActionType == TurnInterpretationActionType.MissingLocation
                                              || interpretation?.LocationPlan.ClarificationNeeded == true
                                              && requiresLocation;
        if ((!hasKnownArea && requiresLocation) || missingLocationByInterpretation)
        {
            missing.Add("area_or_location");
        }

        return missing
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
                ClarificationQuestion: null,
                SuggestedOptions: []),
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
                Question: null,
                SuggestedOptions: []);
        }

        if (missingLocation)
        {
            var permissionState = ResolveLocationPermissionState(metadata);
            return permissionState switch
            {
                "denied_open_settings" or "unavailable" => new StructuredClarificationPrompt(
                    Slot: ClarificationSlot.ExplorationLocation,
                    PromptIntent: "location_permission_denied_or_unavailable",
                    Question: null,
                    SuggestedOptions: []),
                "denied_can_ask_again" or "unknown" => new StructuredClarificationPrompt(
                    Slot: ClarificationSlot.ExplorationLocation,
                    PromptIntent: "location_permission_prompt",
                    Question: null,
                    SuggestedOptions: []),
                _ => new StructuredClarificationPrompt(
                    Slot: ClarificationSlot.ExplorationLocation,
                    PromptIntent: "location_missing_fix",
                    Question: null,
                    SuggestedOptions: [])
            };
        }

        return new StructuredClarificationPrompt(
            Slot: ClarificationSlot.ExplorationRefinement,
            PromptIntent: "exploration_refine",
            Question: null,
            SuggestedOptions: []);
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
        CompanionLocationGrounding grounding,
        PlaceRetrievalPlanV1? retrievalPlan,
        string? rememberedArea)
    {
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        var effectivePlaceTypes = retrievalPlan?.IncludedTypes is { Count: > 0 }
            ? retrievalPlan.IncludedTypes
            : extraction.PlaceTypeHints;
        if (effectivePlaceTypes.Count > 0)
        {
            merged[ConversationConstraintKeys.ExplorationPlaceTypes] = string.Join('|', effectivePlaceTypes);
        }

        if (retrievalPlan?.ExcludedTypes is { Count: > 0 })
        {
            merged[ConversationConstraintKeys.ExplorationExcludeTypes] = string.Join('|', retrievalPlan.ExcludedTypes);
        }

        var area = extraction.HasExplicitLocality && !string.IsNullOrWhiteSpace(extraction.LocalityHint)
            ? extraction.LocalityHint!.Trim()
            : grounding.HasTypedArea
                ? grounding.TypedArea!.Trim()
                : !string.IsNullOrWhiteSpace(retrievalPlan?.ResolvedAreaHint)
                    ? retrievalPlan.ResolvedAreaHint!.Trim()
                    : !string.IsNullOrWhiteSpace(rememberedArea)
                        ? rememberedArea.Trim()
                        : extraction.HasNearMeLanguage || retrievalPlan?.NearMeSemantic == true
                            ? "near_me"
                            : null;
        if (!string.IsNullOrWhiteSpace(area))
        {
            merged[ConversationConstraintKeys.ExplorationArea] = area;
        }

        var effectiveTimeHints = retrievalPlan?.TimeFilters is { Count: > 0 }
            ? retrievalPlan.TimeFilters
            : extraction.TimeHints;
        if (effectiveTimeHints.Count > 0)
        {
            merged[ConversationConstraintKeys.ExplorationTime] = string.Join('|', effectiveTimeHints);
        }

        var effectivePreferences = retrievalPlan?.Preferences is { Count: > 0 }
            ? retrievalPlan.Preferences
            : extraction.PreferenceHints;
        if (effectivePreferences.Count > 0)
        {
            merged[ConversationConstraintKeys.ExplorationPreferences] = string.Join('|', effectivePreferences);
        }

        var effectiveAudience = retrievalPlan?.AudienceFilters is { Count: > 0 }
            ? retrievalPlan.AudienceFilters
            : extraction.AudienceHints;
        if (effectiveAudience.Count > 0)
        {
            merged[ConversationConstraintKeys.ExplorationAudience] = string.Join('|', effectiveAudience);
        }

        if (!string.IsNullOrWhiteSpace(retrievalPlan?.BrandTerm))
        {
            merged[ConversationConstraintKeys.ExplorationBrandTerm] = retrievalPlan.BrandTerm!;
        }

        if (!string.IsNullOrWhiteSpace(retrievalPlan?.CanonicalConcept))
        {
            merged[ConversationConstraintKeys.ExplorationCanonicalConcept] = retrievalPlan.CanonicalConcept!;
        }

        return merged;
    }

    private sealed record StructuredClarificationPrompt(
        ClarificationSlot Slot,
        string PromptIntent,
        string? Question,
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
        var primaryType = NormalizeTypeToken(item.PrimaryType);
        var orderedCandidateTypes = new List<string>();
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(primaryType) && seenTypes.Add(primaryType))
        {
            orderedCandidateTypes.Add(primaryType);
        }

        if (item.Types is { Count: > 0 })
        {
            foreach (var value in item.Types)
            {
                var normalized = NormalizeTypeToken(value);
                if (!string.IsNullOrWhiteSpace(normalized) && seenTypes.Add(normalized))
                {
                    orderedCandidateTypes.Add(normalized);
                }
            }
        }

        if (orderedCandidateTypes.Count == 0)
        {
            return false;
        }

        var canonicalType = ResolveCanonicalTypeForFiltering(primaryType, orderedCandidateTypes);

        foreach (var requested in requestedPlaceTypes)
        {
            var normalizedRequested = NormalizeTypeToken(requested);
            if (string.IsNullOrWhiteSpace(normalizedRequested))
            {
                continue;
            }

            var requestedFamily = BuildRequestedTypeFamily(normalizedRequested);
            if (!string.IsNullOrWhiteSpace(canonicalType))
            {
                if (IsTypeFamilyMatch(canonicalType, requestedFamily))
                {
                    return true;
                }

                // Canonical non-generic type takes precedence over secondary hints.
                // This blocks venues like dance halls leaking into coffee queries.
                if (!IsGenericPlaceType(canonicalType))
                {
                    continue;
                }
            }

            if (orderedCandidateTypes.Any(candidateType => IsTypeFamilyMatch(candidateType, requestedFamily)))
            {
                return true;
            }

            if (IsCoffeeFamily(requestedFamily)
                && item.ServesCoffee == true
                && HasCoffeeSignal(item.Name)
                && (string.IsNullOrWhiteSpace(canonicalType) || IsGenericPlaceType(canonicalType)))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveCanonicalTypeForFiltering(
        string primaryType,
        IReadOnlyList<string> orderedCandidateTypes)
    {
        if (!string.IsNullOrWhiteSpace(primaryType))
        {
            return primaryType;
        }

        var firstSpecific = orderedCandidateTypes
            .FirstOrDefault(type => !IsGenericPlaceType(type));
        if (!string.IsNullOrWhiteSpace(firstSpecific))
        {
            return firstSpecific;
        }

        return orderedCandidateTypes.FirstOrDefault() ?? string.Empty;
    }

    private static bool IsGenericPlaceType(string typeToken)
    {
        return string.Equals(typeToken, "point_of_interest", StringComparison.OrdinalIgnoreCase)
               || string.Equals(typeToken, "establishment", StringComparison.OrdinalIgnoreCase)
               || string.Equals(typeToken, "food", StringComparison.OrdinalIgnoreCase)
               || string.Equals(typeToken, "store", StringComparison.OrdinalIgnoreCase);
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
        CompanionLocationGrounding grounding,
        PlaceRetrievalPlanV1? retrievalPlan,
        string? rememberedArea)
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

        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationExcludeTypes, out var excludeTypes)
            && !string.IsNullOrWhiteSpace(excludeTypes))
        {
            result["exclude_types"] = excludeTypes;
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
        else if (!string.IsNullOrWhiteSpace(retrievalPlan?.ResolvedAreaHint))
        {
            result["typed_area"] = retrievalPlan.ResolvedAreaHint!;
        }
        else if (!string.IsNullOrWhiteSpace(rememberedArea))
        {
            result["typed_area"] = rememberedArea!;
        }

        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationBrandTerm, out var brandTerm)
            && !string.IsNullOrWhiteSpace(brandTerm))
        {
            result["brand_term"] = brandTerm;
        }
        else if (!string.IsNullOrWhiteSpace(retrievalPlan?.BrandTerm))
        {
            result["brand_term"] = retrievalPlan.BrandTerm!;
        }

        if (mergedConstraints.TryGetValue(ConversationConstraintKeys.ExplorationCanonicalConcept, out var canonicalConcept)
            && !string.IsNullOrWhiteSpace(canonicalConcept))
        {
            result["canonical_concept"] = canonicalConcept;
        }
        else if (!string.IsNullOrWhiteSpace(retrievalPlan?.CanonicalConcept))
        {
            result["canonical_concept"] = retrievalPlan.CanonicalConcept!;
        }

        if (!string.IsNullOrWhiteSpace(retrievalPlan?.SearchScope))
        {
            result["search_scope"] = retrievalPlan.SearchScope;
        }

        if (retrievalPlan?.SelectedDomain is RealWorldDiscoveryDomain selectedDomain)
        {
            result["planner_domain"] = selectedDomain.ToString();
        }

        return result;
    }

    private static string? TryResolveFreshAreaFromState(ConversationStateSnapshot state, int ttlMinutes)
    {
        if (!state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var area)
            || string.IsNullOrWhiteSpace(area))
        {
            return null;
        }

        var trimmedArea = area.Trim();
        if (string.Equals(trimmedArea, "near_me", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationAreaExpiresUtc, out var expiresRaw)
            && DateTimeOffset.TryParse(expiresRaw, out var expiresUtc))
        {
            return expiresUtc >= now ? trimmedArea : null;
        }

        if (state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationAreaLastUsedUtc, out var lastUsedRaw)
            && DateTimeOffset.TryParse(lastUsedRaw, out var lastUsedUtc))
        {
            return lastUsedUtc >= now.AddMinutes(-Math.Max(5, ttlMinutes))
                ? trimmedArea
                : null;
        }

        return null;
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

    private static CompanionStructuredResults? BuildStructuredPlaceResults(
        IReadOnlyList<PlaceSearchItem> selectedItems,
        PlaceSearchLocationContext? locationContext,
        IGooglePlacesPhotoService? photoService)
    {
        var cards = selectedItems
            .Where(static item => !string.IsNullOrWhiteSpace(item.PlaceId) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item =>
            {
                var opensInMinutes = TryComputeFutureMinutes(item.OpeningHours?.NextOpenTimeUtc);
                var photoUrls = BuildPlacePhotoUrls(item.Photos, photoService);
                return new CompanionPlaceCardResult(
                    Id: item.PlaceId,
                    Name: item.Name,
                    DistanceMeters: TryComputeDistanceMeters(
                        locationContext?.Latitude,
                        locationContext?.Longitude,
                        item.Location),
                    PhotoUrl: photoUrls.FirstOrDefault(),
                    PhotoUrls: photoUrls,
                    FormattedAddress: item.FormattedAddress,
                    ShortFormattedAddress: item.ShortFormattedAddress,
                    Rating: item.Rating,
                    OpenNow: item.OpeningHours?.OpenNow,
                    PriceLevel: item.PriceLevel,
                    WebsiteUrl: item.WebsiteUri,
                    Category: ResolveCategoryFromPlaces(item),
                    PrimaryTypeDisplayName: item.PrimaryTypeDisplayName,
                    ClosesInMinutes: null,
                    OpensInMinutes: item.OpeningHours?.OpenNow == false ? opensInMinutes : null,
                    PhoneNumber: item.NationalPhoneNumber,
                    MenuUrl: TryResolveMenuUrl(item.WebsiteUri),
                    GoogleMapsUri: item.GoogleMapsUri,
                    Latitude: item.Location?.Latitude,
                    Longitude: item.Location?.Longitude);
            })
            .ToArray();
        return cards.Length == 0
            ? null
            : new CompanionStructuredResults("places", cards);
    }

    private static IReadOnlyList<string> BuildPlacePhotoUrls(
        IReadOnlyList<PlacePhotoSummary>? photos,
        IGooglePlacesPhotoService? photoService)
    {
        if (photos is null || photos.Count == 0 || photoService is null)
        {
            return [];
        }

        return photos
            .Where(photo => !string.IsNullOrWhiteSpace(photo.Name))
            .Select(photo => photoService.BuildAppPhotoUrl(photo.Name, maxWidthPx: 900, maxHeightPx: 520))
            .Where(static url => !string.IsNullOrWhiteSpace(url))
            .Select(static url => url!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static int? TryComputeFutureMinutes(DateTimeOffset? futureUtc)
    {
        if (!futureUtc.HasValue)
        {
            return null;
        }

        var minutes = (int)Math.Ceiling((futureUtc.Value - DateTimeOffset.UtcNow).TotalMinutes);
        return minutes > 0 && minutes < (7 * 24 * 60)
            ? minutes
            : null;
    }

    private static string? TryResolveMenuUrl(string? websiteUri)
    {
        if (string.IsNullOrWhiteSpace(websiteUri))
        {
            return null;
        }

        return websiteUri.Contains("menu", StringComparison.OrdinalIgnoreCase)
            ? websiteUri.Trim()
            : null;
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

    private static IReadOnlyDictionary<string, string> BuildResultContextAttributes(
        PlaceSearchItem item,
        PlaceSearchLocationContext? locationContext)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent("primary_type", item.PrimaryType);
        AddIfPresent("primary_type_display_name", item.PrimaryTypeDisplayName);
        AddIfPresent("types", item.Types is { Count: > 0 } ? string.Join("|", item.Types) : null);
        AddIfPresent("short_address", item.ShortFormattedAddress);
        AddIfPresent("formatted_address", item.FormattedAddress);
        AddIfPresent("price_level", item.PriceLevel);
        AddIfPresent("business_status", item.BusinessStatus);
        AddIfPresent("rating", item.Rating?.ToString("0.0", CultureInfo.InvariantCulture));
        AddIfPresent("user_rating_count", item.UserRatingCount?.ToString(CultureInfo.InvariantCulture));
        AddIfPresent("open_now", item.OpeningHours?.OpenNow?.ToString());
        AddIfPresent("takeout", item.Takeout?.ToString());
        AddIfPresent("delivery", item.Delivery?.ToString());
        AddIfPresent("dine_in", item.DineIn?.ToString());
        AddIfPresent("reservable", item.Reservable?.ToString());
        AddIfPresent("outdoor_seating", item.OutdoorSeating?.ToString());
        AddIfPresent("allows_dogs", item.AllowsDogs?.ToString());
        AddIfPresent("wheelchair_accessible_parking", item.AccessibilityOptions?.WheelchairAccessibleParking?.ToString());

        var distanceMeters = TryComputeDistanceMeters(
            locationContext?.Latitude,
            locationContext?.Longitude,
            item.Location);
        AddIfPresent("distance_meters", distanceMeters?.ToString("0", CultureInfo.InvariantCulture));
        return attributes;

        void AddIfPresent(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[key] = value.Trim();
            }
        }
    }

    private static double? TryComputeDistanceMeters(
        double? sourceLatitude,
        double? sourceLongitude,
        PlaceLocationSummary? target)
    {
        if (!sourceLatitude.HasValue || !sourceLongitude.HasValue || target is null)
        {
            return null;
        }

        const double EarthRadiusMeters = 6_371_000d;
        var sourceLatRad = DegreesToRadians(sourceLatitude.Value);
        var targetLatRad = DegreesToRadians(target.Latitude);
        var deltaLat = DegreesToRadians(target.Latitude - sourceLatitude.Value);
        var deltaLon = DegreesToRadians(target.Longitude - sourceLongitude.Value);
        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)
                + (Math.Cos(sourceLatRad) * Math.Cos(targetLatRad)
                   * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d));
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double value)
    {
        return value * (Math.PI / 180d);
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

        var groundedData = new GroundedDataEnvelope(
            Entities: [],
            SummaryFacts: [],
            Warnings: ["open_exploration_ai_composed"]);

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
                    SuggestedOptions: []),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: nextState,
                ResultContext: request.ResultContext,
                Warnings: ["open_exploration_ai_composed"],
                FollowUpIntentHints: ["open_exploration_continuation"],
                Succeeded: true));
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
                    SuggestedOptions: []),
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
