namespace NSFinance.Api.Modules.AI.Services;

public sealed record ReadinessGuardResult(
    ReadinessTransition Transition,
    ToolExecutionPermission ToolExecutionPermission,
    IReadOnlyList<string> ReasonCodes);

public interface IReadinessTransitionPolicy
{
    ReadinessGuardResult Apply(
        ConversationStateSnapshot currentState,
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision);
}

public sealed class ReadinessTransitionPolicy : IReadinessTransitionPolicy
{
    public ReadinessGuardResult Apply(
        ConversationStateSnapshot currentState,
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision)
    {
        var reasonCodes = new HashSet<string>(decision.ReasonCodes, StringComparer.OrdinalIgnoreCase);
        var finalReadiness = decision.Readiness.To;
        var currentReadiness = currentState.ReadinessLevel;

        if (decision.ModeCandidate == ConversationMode.Financial
            && finalReadiness == ConversationReadinessLevel.R4_ToolReady)
        {
            finalReadiness = ConversationReadinessLevel.R3_StructuredIncomplete;
            reasonCodes.Add("readiness_guard_financial_never_tool_ready");
        }

        if (decision.ModeCandidate == ConversationMode.Exploration
            && explorationSubtypeDecision?.Subtype == ExplorationSubtype.Open
            && finalReadiness == ConversationReadinessLevel.R4_ToolReady)
        {
            finalReadiness = ConversationReadinessLevel.R3_StructuredIncomplete;
            reasonCodes.Add("readiness_guard_open_exploration_never_tool_ready");
        }

        var toolPermission = finalReadiness == ConversationReadinessLevel.R4_ToolReady
                             && decision.ToolExecutionPermission == ToolExecutionPermission.EligibleIfGuardPasses
            ? ToolExecutionPermission.EligibleIfGuardPasses
            : ToolExecutionPermission.Forbidden;

        if (toolPermission == ToolExecutionPermission.Forbidden)
        {
            reasonCodes.Add("readiness_guard_tools_forbidden");
        }

        return new ReadinessGuardResult(
            Transition: new ReadinessTransition(
                From: currentReadiness,
                To: finalReadiness),
            ToolExecutionPermission: toolPermission,
            ReasonCodes: reasonCodes.ToArray());
    }
}

public sealed record FollowUpBindingPolicyResult(
    FollowUpBindingType BindingType,
    Guid? ActiveResultSetId,
    string? SelectedEntityId,
    IReadOnlyList<string> ReasonCodes);

public interface IFollowUpBindingPolicy
{
    FollowUpBindingPolicyResult Determine(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult);
}

public sealed class FollowUpBindingPolicy : IFollowUpBindingPolicy
{
    public FollowUpBindingPolicyResult Determine(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult)
    {
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadata = request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Guid? activeResultSetId = null;
        if (TryReadGuid(metadata, "chat_result_set_id", out var clientResultSetId))
        {
            activeResultSetId = clientResultSetId;
            reasonCodes.Add("followup_binding_client_result_set");
        }
        else if (state.ResultContextRef?.ActiveResultSetId is Guid stateResultSetId)
        {
            activeResultSetId = stateResultSetId;
            reasonCodes.Add("followup_binding_state_result_set");
        }
        else if (resultContextReadResult.ActiveResultContext is not null)
        {
            activeResultSetId = resultContextReadResult.ActiveResultContext.ResultSetId;
            reasonCodes.Add("followup_binding_active_context");
        }

        var selectedEntityId = ReadMetadataValue(metadata, "chat_selected_entity_id")
                               ?? state.SelectedEntityId;
        if (!string.IsNullOrWhiteSpace(selectedEntityId))
        {
            reasonCodes.Add("followup_binding_selected_entity");
        }

        var bindingType = resultContextReadResult.BindingClassification switch
        {
            ResultContextBindingClassification.BindPrior => FollowUpBindingType.BindPrior,
            ResultContextBindingClassification.Refine => FollowUpBindingType.Refine,
            ResultContextBindingClassification.NewBranch => FollowUpBindingType.NewBranch,
            ResultContextBindingClassification.NewTopic => FollowUpBindingType.NewTopic,
            _ => FollowUpBindingType.None
        };

        return new FollowUpBindingPolicyResult(
            BindingType: bindingType,
            ActiveResultSetId: activeResultSetId,
            SelectedEntityId: string.IsNullOrWhiteSpace(selectedEntityId) ? null : selectedEntityId.Trim(),
            ReasonCodes: reasonCodes.ToArray());
    }

    private static bool TryReadGuid(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out Guid result)
    {
        result = Guid.Empty;
        var value = ReadMetadataValue(metadata, key);
        return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out result);
    }

    private static string? ReadMetadataValue(
        IReadOnlyDictionary<string, string> metadata,
        string key)
    {
        if (metadata.TryGetValue(key, out var exactValue))
        {
            return exactValue;
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

public sealed record ContradictionResolutionResult(
    ConversationStateSnapshot State,
    IReadOnlyList<string> ReasonCodes);

public interface IContradictionResolutionPolicy
{
    ContradictionResolutionResult Apply(
        ConversationStateSnapshot state,
        string userMessage,
        FollowUpBindingType bindingType);
}

public sealed class ContradictionResolutionPolicy : IContradictionResolutionPolicy
{
    private static readonly string[] CorrectionMarkers =
    [
        "actually",
        "instead",
        "not ",
        "don't",
        "do not",
        "wrong",
        "change of plan",
        "new question",
        "something else"
    ];

    public ContradictionResolutionResult Apply(
        ConversationStateSnapshot state,
        string userMessage,
        FollowUpBindingType bindingType)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new ContradictionResolutionResult(state, []);
        }

        var normalized = userMessage.Trim().ToLowerInvariant();
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextState = state;

        if (bindingType == FollowUpBindingType.NewTopic
            || normalized.Contains("new topic", StringComparison.Ordinal)
            || normalized.Contains("different question", StringComparison.Ordinal))
        {
            nextState = nextState with
            {
                ReadinessLevel = ConversationReadinessLevel.R1_Vague,
                NeedsFollowUp = true,
                FollowUpBindingType = FollowUpBindingType.NewTopic,
                TransitionIntent = "topic_switch",
                ResultContextRef = null,
                SelectedEntityId = null,
                LastSuggestedEntities = [],
                LastExecutionFingerprint = null
            };
            reasonCodes.Add("contradiction_topic_switch_reset");
            return new ContradictionResolutionResult(nextState, reasonCodes.ToArray());
        }

        if (!CorrectionMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            return new ContradictionResolutionResult(state, []);
        }

        var downgradedReadiness = state.ReadinessLevel switch
        {
            ConversationReadinessLevel.R4_ToolReady => ConversationReadinessLevel.R3_StructuredIncomplete,
            ConversationReadinessLevel.R3_StructuredIncomplete => ConversationReadinessLevel.R2_DirectionKnown,
            _ => state.ReadinessLevel
        };

        nextState = nextState with
        {
            ReadinessLevel = downgradedReadiness,
            TransitionIntent = "correction",
            NeedsFollowUp = true
        };
        reasonCodes.Add("contradiction_readiness_downgraded");

        return new ContradictionResolutionResult(nextState, reasonCodes.ToArray());
    }
}
