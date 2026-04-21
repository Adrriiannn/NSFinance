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

        var shaped = queryShaper.Shape(request.Request.UserMessage, locationContext, extraction);
        warnings.UnionWith(shaped.ReasonCodes);

        if (request.StrategyDecision.Readiness.To != ConversationReadinessLevel.R4_ToolReady
            || request.StrategyDecision.ToolExecutionPermission != ToolExecutionPermission.EligibleIfGuardPasses)
        {
            warnings.Add("chat.tool.guard_blocked");
            var blockedState = request.State with
            {
                ActiveMode = ConversationMode.Conversation,
                ModeCandidate = ConversationMode.Exploration,
                MissingConstraints = BuildMissingConstraints(extraction, grounding),
                LastClarificationPrompt = "I can search once we pin down the missing detail or confirm the area."
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
                    SuggestedOptions: ["Add an area", "Share a concrete place type"]),
                DeterministicReplyText: null,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                State: blockedState,
                ResultContext: request.ResultContext,
                Warnings: warnings.ToArray(),
                FollowUpIntentHints: ["clarify_intent"],
                Succeeded: true);
        }

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
            return new ConversationModeExecutionResult(
                CompositionRequest: new ResponseCompositionRequest(
                    ResponseType: ResponseCompositionType.Fallback,
                    ToneDirective: ResponseToneDirective.Neutral,
                    Strategy: ConversationBehaviorStrategy.SuggestAndClarify,
                    Mode: ConversationMode.Exploration,
                    ReadinessLevel: request.State.ReadinessLevel,
                    UserMessage: request.Request.UserMessage,
                    GroundedData: new GroundedDataEnvelope([], [], warnings.ToArray()),
                    Constraints: request.State.Constraints,
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
                    NeedsFollowUp = true
                },
                ResultContext: request.ResultContext,
                Warnings: warnings.ToArray(),
                FollowUpIntentHints: ["refine_place_preferences"],
                Succeeded: true);
        }

        ResultContextSnapshot? persistedResultContext = request.ResultContext;
        ConversationStateSnapshot nextState = request.State;
        if (request.Request.UserId.HasValue && request.Request.ConversationThreadId.HasValue)
        {
            var write = await resultContextService.WriteAsync(
                new ResultContextWriteRequest(
                    UserId: request.Request.UserId.Value,
                    ConversationThreadId: request.Request.ConversationThreadId.Value,
                    SourceMode: ConversationMode.Exploration,
                    SourceSubtype: ExplorationSubtype.Structured,
                    QueryFingerprint: BuildQueryFingerprint(shaped.Query, extraction),
                    NormalizedConstraints: BuildNormalizedConstraints(extraction, grounding),
                    SuggestedEntities: searchResult.Items
                        .Take(8)
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
            nextState = request.State with
            {
                ActiveMode = ConversationMode.Exploration,
                ModeCandidate = ConversationMode.Exploration,
                ReadinessLevel = ConversationReadinessLevel.R4_ToolReady,
                LastSuggestedEntities = write.Snapshot.SuggestedEntities
                    .Select(item => new ConversationSuggestedEntity(item.EntityId, item.Label, item.Rank, item.StableReference))
                    .ToArray(),
                ResultContextRef = write.Reference,
                LastExecutionFingerprint = write.Snapshot.QueryFingerprint,
                NeedsFollowUp = true
            };
        }

        var groundedData = new GroundedDataEnvelope(
            Entities: searchResult.Items
                .Take(8)
                .Select((item, index) => new ConversationSuggestedEntity(item.PlaceId, item.Name, index + 1, item.GoogleMapsUri))
                .ToArray(),
            SummaryFacts: BuildStructuredFacts(searchResult),
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
        CompanionLocationGrounding grounding)
    {
        var missing = new List<string>();
        if (extraction.PlaceTypeHints.Count == 0)
        {
            missing.Add("place_type");
        }

        if (!grounding.HasCoordinates && !grounding.HasTypedArea && !extraction.HasExplicitLocality)
        {
            missing.Add("area_or_location");
        }

        return missing;
    }

    private static IReadOnlyDictionary<string, string> BuildNormalizedConstraints(
        LocalDiscoveryConstraintExtractionResult extraction,
        CompanionLocationGrounding grounding)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (extraction.PlaceTypeHints.Count > 0)
        {
            result["place_types"] = string.Join('|', extraction.PlaceTypeHints);
        }

        if (extraction.PreferenceHints.Count > 0)
        {
            result["preference_hints"] = string.Join('|', extraction.PreferenceHints);
        }

        if (extraction.TimeHints.Count > 0)
        {
            result["time_hints"] = string.Join('|', extraction.TimeHints);
        }

        if (grounding.HasTypedArea)
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

    private static IReadOnlyList<GroundedDataPoint> BuildStructuredFacts(PlaceSearchResult result)
    {
        return result.Items
            .Take(6)
            .Select(item => new GroundedDataPoint(
                Label: item.Name,
                Value: BuildPlaceFact(item)))
            .ToArray();
    }

    private static string BuildPlaceFact(PlaceSearchItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            parts.Add(item.Category);
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
