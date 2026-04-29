using System.Globalization;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionActionResolver
{
    CompanionResolvedAction Resolve(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContext,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ConversationIntelligenceResult? intelligence);
}

public sealed partial class CompanionActionResolver : ICompanionActionResolver
{
    public CompanionResolvedAction Resolve(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContext,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ConversationIntelligenceResult? intelligence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(resultContext);

        var activeResult = resultContext.ActiveResultContext;
        var nextAction = intelligence?.NextAction.Type;
        var targetPreviousResults = activeResult is not null
                                    && (intelligence?.TaskState.TargetPreviousResults == true
                                        || IsPriorResultAction(nextAction));
        var reason = intelligence?.NextAction.Reason
                     ?? interpretation?.RecommendedNextStep
                     ?? "Resolved from turn interpretation and conversation state.";
        var placeQuery = ResolvePlaceQuery(request.UserMessage, interpretation, retrievalPlan, activeResult);
        var locationQuery = ResolveLocationQuery(interpretation, retrievalPlan);
        var requirement = ResolveRequirement(request.UserMessage, interpretation, intelligence);
        var sortGoal = ResolveSortGoal(request.UserMessage, intelligence);
        var includeConcepts = ResolveIncludeConcepts(request.UserMessage, interpretation, retrievalPlan, placeQuery);
        var excludeConcepts = ResolveExcludeConcepts(request.UserMessage, interpretation, retrievalPlan, placeQuery);
        var preferences = MergeDistinct(
            interpretation?.PlacePlan.Preferences,
            retrievalPlan?.Preferences,
            requirement is null ? [] : [requirement]);
        var timeFilters = MergeDistinct(
            interpretation?.PlacePlan.TimeFilters,
            retrievalPlan?.TimeFilters);
        var targetResultSetId = activeResult?.ResultSetId.ToString("D", CultureInfo.InvariantCulture);

        if (string.Equals(intelligence?.ConversationPhase, "closing", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                CompanionActionKind.CloseConversation,
                reason,
                requiresToolExecution: false,
                requiresClarification: false);
        }

        if (targetPreviousResults)
        {
            var kind = nextAction switch
            {
                "sort_previous_results" => CompanionActionKind.SortPreviousResults,
                "enrich_details" => CompanionActionKind.EnrichPreviousResults,
                "compare_previous_results" => CompanionActionKind.ComparePreviousResults,
                _ when !string.IsNullOrWhiteSpace(sortGoal) => CompanionActionKind.SortPreviousResults,
                _ => CompanionActionKind.FilterPreviousResults
            };

            return Build(
                kind,
                reason,
                requiresToolExecution: true,
                requiresClarification: false);
        }

        if (interpretation?.InScopeVerdict == TurnInterpretationInScopeVerdict.OutOfScope
            || string.Equals(nextAction, "soft_redirect", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                CompanionActionKind.SoftRedirect,
                reason,
                requiresToolExecution: false,
                requiresClarification: false);
        }

        if (intelligence?.ShouldClarify == true
            || string.Equals(nextAction, "ask_clarification", StringComparison.OrdinalIgnoreCase)
            || interpretation?.ActionType is TurnInterpretationActionType.MissingLocation or TurnInterpretationActionType.MissingTarget)
        {
            var clarificationNeed = interpretation?.ActionType == TurnInterpretationActionType.MissingLocation
                ? "location"
                : interpretation?.ActionType == TurnInterpretationActionType.MissingTarget
                    ? "place_query"
                    : intelligence?.NextAction.Requirement;
            return Build(
                CompanionActionKind.AskClarification,
                reason,
                requiresToolExecution: false,
                requiresClarification: true,
                clarificationNeed: clarificationNeed);
        }

        if (interpretation?.IntentFamily is TurnInterpretationIntentFamily.PlaceDiscovery or TurnInterpretationIntentFamily.Mixed
            && (interpretation.ActionType == TurnInterpretationActionType.ReadyForSearch
                || intelligence?.ShouldExecuteTool == true
                || string.Equals(nextAction, "execute_search", StringComparison.OrdinalIgnoreCase)))
        {
            return Build(
                CompanionActionKind.NewPlaceSearch,
                reason,
                requiresToolExecution: true,
                requiresClarification: false);
        }

        if (intelligence?.ConversationPhase == "off_topic")
        {
            return Build(
                CompanionActionKind.SoftRedirect,
                reason,
                requiresToolExecution: false,
                requiresClarification: false);
        }

        return Build(
            interpretation?.ActionType == TurnInterpretationActionType.SoftRedirect
                ? CompanionActionKind.SoftRedirect
                : CompanionActionKind.AnswerDirectly,
            reason,
            requiresToolExecution: false,
            requiresClarification: false);

        CompanionResolvedAction Build(
            CompanionActionKind kind,
            string actionReason,
            bool requiresToolExecution,
            bool requiresClarification,
            string? clarificationNeed = null)
        {
            return new CompanionResolvedAction(
                Kind: kind,
                Reason: actionReason,
                RequiresToolExecution: requiresToolExecution,
                RequiresClarification: requiresClarification,
                ClarificationNeed: clarificationNeed,
                PlaceQuery: placeQuery,
                LocationQuery: locationQuery,
                Requirement: requirement,
                SortGoal: sortGoal,
                TargetResultSetId: targetResultSetId,
                IncludeConcepts: includeConcepts,
                ExcludeConcepts: excludeConcepts,
                Preferences: preferences,
                TimeFilters: timeFilters,
                Warnings: BuildWarnings(kind, activeResult, intelligence, interpretation));
        }
    }

    private static bool IsPriorResultAction(string? nextAction)
    {
        return nextAction is "filter_previous_results" or "sort_previous_results" or "enrich_details";
    }

    private static string? ResolvePlaceQuery(
        string userMessage,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ResultContextSnapshot? activeResult)
    {
        if (!string.IsNullOrWhiteSpace(retrievalPlan?.BrandTerm))
        {
            return retrievalPlan.BrandTerm!.Trim();
        }

        var brand = interpretation?.PlacePlan.BrandOrEntityTerms.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(brand))
        {
            return brand.Trim();
        }

        var fromMessage = StripLocationAndRefinementNoise(userMessage);
        if (!string.IsNullOrWhiteSpace(fromMessage)
            && !IsResultOnlyFollowUp(fromMessage))
        {
            return fromMessage;
        }

        if (activeResult?.NormalizedConstraints.TryGetValue("brand_term", out var previousBrand) == true
            && !string.IsNullOrWhiteSpace(previousBrand))
        {
            return previousBrand;
        }

        if (activeResult?.NormalizedConstraints.TryGetValue("canonical_concept", out var previousConcept) == true
            && !string.IsNullOrWhiteSpace(previousConcept))
        {
            return previousConcept;
        }

        return retrievalPlan?.CanonicalConcept ?? interpretation?.PlacePlan.CanonicalConcept;
    }

    private static string? ResolveLocationQuery(
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan)
    {
        if (!string.IsNullOrWhiteSpace(retrievalPlan?.ResolvedAreaHint))
        {
            return retrievalPlan.ResolvedAreaHint!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(interpretation?.LocationPlan.ResolvedAreaHint))
        {
            return interpretation.LocationPlan.ResolvedAreaHint!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(interpretation?.LocationPlan.ExplicitAreaText))
        {
            return interpretation.LocationPlan.ExplicitAreaText!.Trim();
        }

        return interpretation?.LocationPlan.NearMeSemantic == true || retrievalPlan?.NearMeSemantic == true
            ? "near me"
            : null;
    }

    private static string? ResolveRequirement(
        string userMessage,
        TurnInterpretationV2? interpretation,
        ConversationIntelligenceResult? intelligence)
    {
        if (!string.IsNullOrWhiteSpace(intelligence?.NextAction.Requirement))
        {
            return intelligence.NextAction.Requirement!.Trim();
        }

        var preference = interpretation?.PlacePlan.Preferences.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(preference))
        {
            return preference.Trim();
        }

        var normalized = Normalize(userMessage);
        if (normalized.Contains("parking", StringComparison.Ordinal)
            || normalized.Contains("car park", StringComparison.Ordinal)
            || normalized.Contains("car parks", StringComparison.Ordinal))
        {
            return "parking";
        }

        return null;
    }

    private static string? ResolveSortGoal(
        string userMessage,
        ConversationIntelligenceResult? intelligence)
    {
        if (string.Equals(intelligence?.NextAction.Requirement, "closest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intelligence?.NextAction.Requirement, "nearest", StringComparison.OrdinalIgnoreCase))
        {
            return "distance";
        }

        var normalized = Normalize(userMessage);
        return normalized.Contains("closest", StringComparison.Ordinal)
               || normalized.Contains("nearest", StringComparison.Ordinal)
               || normalized.Contains("distance", StringComparison.Ordinal)
            ? "distance"
            : null;
    }

    private static IReadOnlyList<string> ResolveIncludeConcepts(
        string userMessage,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        string? placeQuery)
    {
        var concepts = MergeDistinct(
            interpretation?.PlacePlan.BrandOrEntityTerms,
            interpretation?.PlacePlan.CandidateDomains,
            interpretation?.PlacePlan.IncludeTypes,
            retrievalPlan?.IncludedTypes,
            string.IsNullOrWhiteSpace(retrievalPlan?.CanonicalConcept) ? [] : [retrievalPlan.CanonicalConcept!],
            string.IsNullOrWhiteSpace(placeQuery) ? [] : [placeQuery!]);

        if (ContainsFineDining(userMessage, placeQuery, interpretation?.PlacePlan.CanonicalConcept))
        {
            concepts = concepts.Concat(["fine dining", "upscale restaurant", "restaurant"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return concepts;
    }

    private static IReadOnlyList<string> ResolveExcludeConcepts(
        string userMessage,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        string? placeQuery)
    {
        var excludes = MergeDistinct(
            interpretation?.PlacePlan.ExcludeTypes,
            retrievalPlan?.ExcludedTypes);
        var normalized = Normalize(userMessage);
        if (normalized.Contains("not fast food", StringComparison.Ordinal)
            || normalized.Contains("no fast food", StringComparison.Ordinal)
            || normalized.Contains("not takeaway", StringComparison.Ordinal)
            || ContainsFineDining(userMessage, placeQuery, interpretation?.PlacePlan.CanonicalConcept))
        {
            excludes = excludes.Concat(["fast food", "takeaway", "meal takeaway"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return excludes;
    }

    private static IReadOnlyList<string> BuildWarnings(
        CompanionActionKind kind,
        ResultContextSnapshot? activeResult,
        ConversationIntelligenceResult? intelligence,
        TurnInterpretationV2? interpretation)
    {
        var warnings = new List<string>();
        if (kind is CompanionActionKind.FilterPreviousResults or CompanionActionKind.SortPreviousResults or CompanionActionKind.EnrichPreviousResults
            && activeResult is null)
        {
            warnings.Add("resolved_action_prior_result_missing");
        }

        if (intelligence is null)
        {
            warnings.Add("resolved_action_without_conversation_intelligence");
        }

        if (interpretation is null)
        {
            warnings.Add("resolved_action_without_turn_interpretation");
        }

        return warnings;
    }

    private static IReadOnlyList<string> MergeDistinct(params IReadOnlyList<string>?[] values)
    {
        return values
            .Where(static list => list is not null)
            .SelectMany(static list => list!)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsFineDining(string userMessage, string? placeQuery, string? canonicalConcept)
    {
        return Normalize(userMessage).Contains("fine dining", StringComparison.Ordinal)
               || Normalize(placeQuery).Contains("fine dining", StringComparison.Ordinal)
               || Normalize(canonicalConcept).Contains("fine dining", StringComparison.Ordinal);
    }

    private static string StripLocationAndRefinementNoise(string userMessage)
    {
        var normalized = userMessage.Trim();
        normalized = LocationSuffixPattern().Replace(normalized, string.Empty);
        normalized = NearMePattern().Replace(normalized, string.Empty);
        normalized = normalized.Trim(' ', ',', '.', '?', '!');
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static bool IsResultOnlyFollowUp(string value)
    {
        var normalized = Normalize(value);
        return normalized is "which one" or "closest only" or "just the closest please" or "closest please" or "open now"
               || normalized.StartsWith("which one ", StringComparison.Ordinal)
               || normalized.StartsWith("just ", StringComparison.Ordinal)
               || normalized.StartsWith("not ", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    [GeneratedRegex(@"\b(?:near me|nearby|around me|around here|close to me)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NearMePattern();

    [GeneratedRegex(@"\b(?:in|around|near)\s+[a-z0-9][a-z0-9\s'\-]{1,60}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationSuffixPattern();
}
