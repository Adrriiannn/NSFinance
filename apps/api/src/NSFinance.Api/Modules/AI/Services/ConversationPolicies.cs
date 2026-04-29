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
        var signals = ConversationSignalAnalyzer.Analyze(request.UserMessage);
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(request.UserMessage);
        var snapshot = resultContextReadResult.ActiveResultContext;
        var bindingType = MapBindingType(resultContextReadResult.BindingClassification);

        Guid? clientResultSetId = null;
        if (TryReadGuid(metadata, "chat_result_set_id", out var explicitResultSetId))
        {
            clientResultSetId = explicitResultSetId;
            reasonCodes.Add("followup_binding_client_result_set");
        }

        var explicitSelectedEntityId = ReadMetadataValue(metadata, "chat_selected_entity_id");
        if (!string.IsNullOrWhiteSpace(explicitSelectedEntityId))
        {
            reasonCodes.Add("followup_binding_selected_entity_explicit");
        }

        if (snapshot is null)
        {
            return new FollowUpBindingPolicyResult(
                BindingType: FollowUpBindingType.None,
                ActiveResultSetId: null,
                SelectedEntityId: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        reasonCodes.Add("followup_binding_active_context");

        var semanticFamily = ConversationPolicyHelpers.ResolveSemanticFamily(state, signals);
        var hasExplicitBindingEvidence = clientResultSetId.HasValue
                                         || !string.IsNullOrWhiteSpace(explicitSelectedEntityId)
                                         || signals.HasResultReferenceSignal
                                         || signals.HasComparisonSignal;
        var sourceFamily = snapshot.SourceMode switch
        {
            ConversationMode.Exploration => ConversationSemanticFamilies.Exploration,
            ConversationMode.Financial => ConversationSemanticFamilies.Financial,
            ConversationMode.GeneralKnowledge => ConversationSemanticFamilies.GeneralKnowledge,
            _ => ConversationSemanticFamilies.Conversation
        };

        if (snapshot.IsActiveWindowExpired && !hasExplicitBindingEvidence)
        {
            reasonCodes.Add("followup_binding_active_window_expired");
            return new FollowUpBindingPolicyResult(
                BindingType: FollowUpBindingType.None,
                ActiveResultSetId: null,
                SelectedEntityId: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        if (IsStrongSemanticMismatch(semanticFamily, sourceFamily))
        {
            reasonCodes.Add("followup_binding_semantic_family_mismatch");
            return new FollowUpBindingPolicyResult(
                BindingType: FollowUpBindingType.NewTopic,
                ActiveResultSetId: null,
                SelectedEntityId: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        if (bindingType == FollowUpBindingType.None
            && IsFreshStructuredSearchTurn(extraction, signals))
        {
            reasonCodes.Add("followup_binding_fresh_structured_query_precedence");
            return new FollowUpBindingPolicyResult(
                BindingType: FollowUpBindingType.None,
                ActiveResultSetId: null,
                SelectedEntityId: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        if (bindingType == FollowUpBindingType.None)
        {
            if (signals.HasComparisonSignal)
            {
                bindingType = snapshot.ParentResultSetId.HasValue
                              || snapshot.BranchRootResultSetId != snapshot.ResultSetId
                    ? FollowUpBindingType.Refine
                    : FollowUpBindingType.BindPrior;
                reasonCodes.Add("followup_binding_lineage_compare");
            }
            else if (signals.HasResultReferenceSignal)
            {
                bindingType = FollowUpBindingType.BindPrior;
                reasonCodes.Add("followup_binding_result_reference");
            }
            else if (signals.HasBranchingSignal
                     && string.Equals(sourceFamily, ConversationSemanticFamilies.Exploration, StringComparison.OrdinalIgnoreCase))
            {
                bindingType = FollowUpBindingType.NewBranch;
                reasonCodes.Add("followup_binding_branch_signal");
            }
        }

        if (bindingType != FollowUpBindingType.None
            && bindingType != FollowUpBindingType.NewTopic
            && snapshot.IsActiveWindowExpired
            && !hasExplicitBindingEvidence
            && !clientResultSetId.HasValue)
        {
            reasonCodes.Add("followup_binding_expired_without_evidence");
            bindingType = FollowUpBindingType.None;
        }

        var activeResultSetId = bindingType is FollowUpBindingType.BindPrior or FollowUpBindingType.Refine or FollowUpBindingType.NewBranch
            ? snapshot.ResultSetId
            : clientResultSetId;
        var selectedEntityId = bindingType == FollowUpBindingType.BindPrior
                               || (!string.IsNullOrWhiteSpace(explicitSelectedEntityId)
                                   && bindingType != FollowUpBindingType.NewTopic)
            ? (explicitSelectedEntityId ?? snapshot.SelectedEntityId ?? state.SelectedEntityId)?.Trim()
            : null;

        return new FollowUpBindingPolicyResult(
            BindingType: bindingType,
            ActiveResultSetId: activeResultSetId,
            SelectedEntityId: string.IsNullOrWhiteSpace(selectedEntityId) ? null : selectedEntityId,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static FollowUpBindingType MapBindingType(ResultContextBindingClassification classification)
    {
        return classification switch
        {
            ResultContextBindingClassification.BindPrior => FollowUpBindingType.BindPrior,
            ResultContextBindingClassification.Refine => FollowUpBindingType.Refine,
            ResultContextBindingClassification.NewBranch => FollowUpBindingType.NewBranch,
            ResultContextBindingClassification.NewTopic => FollowUpBindingType.NewTopic,
            _ => FollowUpBindingType.None
        };
    }

    private static bool IsStrongSemanticMismatch(string semanticFamily, string sourceFamily)
    {
        if (string.Equals(semanticFamily, ConversationSemanticFamilies.Conversation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(semanticFamily, sourceFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFreshStructuredSearchTurn(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        if (signals.HasBranchingSignal || signals.HasComparisonSignal || signals.HasResultReferenceSignal)
        {
            return false;
        }

        var hasPlaceType = extraction.PlaceTypeHints.Count > 0 || signals.HasConcretePlaceSignal;
        var hasLocationFrame = extraction.HasNearMeLanguage
                               || extraction.HasExplicitLocality
                               || extraction.TimeHints.Count > 0
                               || signals.HasExplicitLocation;
        return hasPlaceType && hasLocationFrame;
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
    FollowUpBindingType? BindingTypeOverride,
    IReadOnlyList<string> ReasonCodes);

public interface IContradictionResolutionPolicy
{
    ContradictionResolutionResult Apply(
        ConversationStateSnapshot state,
        string userMessage,
        FollowUpBindingType bindingType,
        ConversationSignals signals);
}

public sealed class ContradictionResolutionPolicy : IContradictionResolutionPolicy
{
    public ContradictionResolutionResult Apply(
        ConversationStateSnapshot state,
        string userMessage,
        FollowUpBindingType bindingType,
        ConversationSignals signals)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new ContradictionResolutionResult(state, null, []);
        }

        var normalized = userMessage.Trim().ToLowerInvariant();
        var mutationSegment = ConversationPolicyHelpers.ResolveMutationSegment(userMessage);
        var normalizedMutationSegment = mutationSegment.ToLowerInvariant();
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentFamily = ConversationPolicyHelpers.ResolveSemanticFamily(
            state,
            state.ConversationSignals ?? signals);
        var turnFamily = ConversationPolicyHelpers.ResolveSemanticFamily(state, signals);

        if (bindingType == FollowUpBindingType.NewTopic
            || signals.HasTopicSwitchSignal
            || HasHardTopicReset(currentFamily, turnFamily))
        {
            return BuildTopicReset(state, turnFamily, normalizedMutationSegment, reasonCodes);
        }

        var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase);
        var nextState = state;
        var bindingOverride = bindingType == FollowUpBindingType.NewBranch
            ? FollowUpBindingType.NewBranch
            : (FollowUpBindingType?)null;
        var materialRefine = false;
        var materialBranch = bindingType == FollowUpBindingType.NewBranch;

        if (!string.Equals(turnFamily, ConversationSemanticFamilies.Conversation, StringComparison.OrdinalIgnoreCase))
        {
            constraints[ConversationConstraintKeys.SemanticFamily] = turnFamily;
        }

        if (string.Equals(turnFamily, ConversationSemanticFamilies.Exploration, StringComparison.OrdinalIgnoreCase))
        {
            var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(mutationSegment);
            var mutationSignals = ConversationSignalAnalyzer.Analyze(mutationSegment);
            var existingSubtype = ReadConstraint(state, ConversationConstraintKeys.ExplorationSubtype);
            var updatedPlaceTypes = ConversationPolicyHelpers.ResolveExplorationPlaceTypes(extraction);
            var updatedArea = ResolveUpdatedExplorationArea(normalizedMutationSegment, extraction, mutationSignals);
            var updatedPreferences = ConversationPolicyHelpers.ResolveExplorationPreferences(normalizedMutationSegment, extraction);
            var updatedTime = ConversationPolicyHelpers.ResolveExplorationTime(extraction);
            var targetSubtype = ResolveExplorationSubtypeForMutation(
                existingSubtype,
                normalizedMutationSegment,
                extraction,
                mutationSignals,
                updatedPlaceTypes);

            var freshStructuredQuery = ConversationPolicyHelpers.HasConcreteStructuredSearchFrame(extraction, mutationSignals)
                                       && !mutationSignals.HasBranchingSignal
                                       && !mutationSignals.HasComparisonSignal
                                       && !mutationSignals.HasResultReferenceSignal
                                       && !mutationSignals.HasCorrectionSignal;
            if (freshStructuredQuery)
            {
                constraints.Clear();
                constraints[ConversationConstraintKeys.SemanticFamily] = ConversationSemanticFamilies.Exploration;
                constraints[ConversationConstraintKeys.ExplorationSubtype] = ExplorationSubtype.Structured.ToString();
                if (!string.IsNullOrWhiteSpace(updatedPlaceTypes))
                {
                    constraints[ConversationConstraintKeys.ExplorationPlaceTypes] = updatedPlaceTypes;
                }

                if (!string.IsNullOrWhiteSpace(updatedArea))
                {
                    constraints[ConversationConstraintKeys.ExplorationArea] = updatedArea;
                }

                if (!string.IsNullOrWhiteSpace(updatedTime))
                {
                    constraints[ConversationConstraintKeys.ExplorationTime] = updatedTime;
                }

                if (!string.IsNullOrWhiteSpace(updatedPreferences))
                {
                    constraints[ConversationConstraintKeys.ExplorationPreferences] = updatedPreferences;
                }

                reasonCodes.Add("contradiction_fresh_structured_query_reset");
                return new ContradictionResolutionResult(
                    State: state with
                    {
                        Constraints = constraints,
                        SelectedEntityId = null,
                        LastSuggestedEntities = [],
                        LastExecutionFingerprint = null,
                        ResultContextRef = null,
                        TransitionIntent = ConversationTransitionIntents.DirectMode,
                        NeedsFollowUp = true,
                        FollowUpBindingType = FollowUpBindingType.None
                    },
                    BindingTypeOverride: FollowUpBindingType.None,
                    ReasonCodes: reasonCodes.ToArray());
            }

            if (SetConstraint(constraints, ConversationConstraintKeys.ExplorationSubtype, targetSubtype.ToString()))
            {
                reasonCodes.Add("contradiction_exploration_subtype_rewritten");
                if (!string.IsNullOrWhiteSpace(existingSubtype))
                {
                    materialBranch = true;
                }
            }

            if (SetConstraint(constraints, ConversationConstraintKeys.ExplorationArea, updatedArea))
            {
                reasonCodes.Add("contradiction_exploration_area_rewritten");
                materialRefine = true;
            }

            if (SetConstraint(constraints, ConversationConstraintKeys.ExplorationPreferences, updatedPreferences))
            {
                reasonCodes.Add("contradiction_exploration_preferences_rewritten");
                materialRefine = true;
            }

            if (SetConstraint(constraints, ConversationConstraintKeys.ExplorationTime, updatedTime))
            {
                reasonCodes.Add("contradiction_exploration_time_rewritten");
                materialRefine = true;
            }

            var placeTypesChanged = SetConstraint(
                constraints,
                ConversationConstraintKeys.ExplorationPlaceTypes,
                updatedPlaceTypes);
            if (placeTypesChanged)
            {
                reasonCodes.Add("contradiction_exploration_place_types_rewritten");
                materialBranch = true;
            }

            if (targetSubtype == ExplorationSubtype.Open
                && (signals.HasCorrectionSignal || signals.HasBranchingSignal))
            {
                if (constraints.Remove(ConversationConstraintKeys.ExplorationPlaceTypes))
                {
                    reasonCodes.Add("contradiction_exploration_place_types_cleared");
                }
            }

            if (materialBranch)
            {
                bindingOverride = FollowUpBindingType.NewBranch;
                nextState = nextState with
                {
                    SelectedEntityId = null,
                    LastSuggestedEntities = [],
                    LastExecutionFingerprint = null,
                    ResultContextRef = targetSubtype == ExplorationSubtype.Open
                        ? null
                        : state.ResultContextRef,
                    TransitionIntent = ConversationTransitionIntents.NewBranch,
                    NeedsFollowUp = true
                };
            }
            else if (materialRefine)
            {
                bindingOverride ??= FollowUpBindingType.Refine;
                nextState = nextState with
                {
                    SelectedEntityId = null,
                    LastExecutionFingerprint = null,
                    TransitionIntent = ConversationTransitionIntents.RefineCurrentBranch,
                    NeedsFollowUp = true
                };
            }
        }
        else if (string.Equals(turnFamily, ConversationSemanticFamilies.Financial, StringComparison.OrdinalIgnoreCase))
        {
            var financialFocus = ConversationPolicyHelpers.ResolveFinancialFocus(normalizedMutationSegment);
            if (SetConstraint(constraints, ConversationConstraintKeys.FinancialFocus, financialFocus))
            {
                reasonCodes.Add("contradiction_financial_focus_rewritten");
            }
        }

        if ((signals.HasCorrectionSignal || bindingOverride is FollowUpBindingType.Refine or FollowUpBindingType.NewBranch)
            && state.ReadinessLevel != ConversationReadinessLevel.R0_Unknown)
        {
            nextState = nextState with
            {
                ReadinessLevel = ConversationPolicyHelpers.DowngradeReadiness(state.ReadinessLevel),
                TransitionIntent = string.IsNullOrWhiteSpace(nextState.TransitionIntent)
                    ? ConversationTransitionIntents.Correction
                    : nextState.TransitionIntent,
                NeedsFollowUp = true
            };
            reasonCodes.Add("contradiction_readiness_downgraded");
        }

        if (!string.Equals(turnFamily, currentFamily, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(turnFamily, ConversationSemanticFamilies.Conversation, StringComparison.OrdinalIgnoreCase))
        {
            nextState = nextState with
            {
                SelectedEntityId = null,
                LastSuggestedEntities = [],
                LastExecutionFingerprint = null,
                ResultContextRef = null
            };
        }

        return new ContradictionResolutionResult(
            State: nextState with
            {
                Constraints = constraints,
                FollowUpBindingType = bindingOverride ?? bindingType
            },
            BindingTypeOverride: bindingOverride,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static bool HasHardTopicReset(string currentFamily, string turnFamily)
    {
        if (string.Equals(turnFamily, ConversationSemanticFamilies.Conversation, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentFamily, ConversationSemanticFamilies.Conversation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(currentFamily, turnFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static ContradictionResolutionResult BuildTopicReset(
        ConversationStateSnapshot state,
        string turnFamily,
        string normalized,
        HashSet<string> reasonCodes)
    {
        var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(turnFamily, ConversationSemanticFamilies.Conversation, StringComparison.OrdinalIgnoreCase))
        {
            constraints[ConversationConstraintKeys.SemanticFamily] = turnFamily;
        }

        if (string.Equals(turnFamily, ConversationSemanticFamilies.Financial, StringComparison.OrdinalIgnoreCase))
        {
            var focus = ConversationPolicyHelpers.ResolveFinancialFocus(normalized);
            if (!string.IsNullOrWhiteSpace(focus))
            {
                constraints[ConversationConstraintKeys.FinancialFocus] = focus;
            }
        }

        if (string.Equals(turnFamily, ConversationSemanticFamilies.Exploration, StringComparison.OrdinalIgnoreCase))
        {
            var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(normalized);
            var subtype = ConversationPolicyHelpers.ResolveExplorationSubtype(extraction, ConversationSignalAnalyzer.Analyze(normalized));
            constraints[ConversationConstraintKeys.ExplorationSubtype] = subtype.ToString();

            SetConstraint(constraints, ConversationConstraintKeys.ExplorationPlaceTypes, ConversationPolicyHelpers.ResolveExplorationPlaceTypes(extraction));
            SetConstraint(constraints, ConversationConstraintKeys.ExplorationArea, ConversationPolicyHelpers.ResolveExplorationArea(extraction));
            SetConstraint(constraints, ConversationConstraintKeys.ExplorationPreferences, ConversationPolicyHelpers.ResolveExplorationPreferences(normalized, extraction));
            SetConstraint(constraints, ConversationConstraintKeys.ExplorationTime, ConversationPolicyHelpers.ResolveExplorationTime(extraction));
        }

        reasonCodes.Add("contradiction_topic_switch_reset");

        return new ContradictionResolutionResult(
            State: state with
            {
                Constraints = constraints,
                ReadinessLevel = ConversationReadinessLevel.R1_Vague,
                NeedsFollowUp = true,
                FollowUpBindingType = FollowUpBindingType.NewTopic,
                TransitionIntent = ConversationTransitionIntents.TopicSwitch,
                ResultContextRef = null,
                SelectedEntityId = null,
                LastSuggestedEntities = [],
                LastExecutionFingerprint = null,
                LastClarificationPrompt = null,
                LastSuggestedOptions = []
            },
            BindingTypeOverride: FollowUpBindingType.NewTopic,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static string? ReadConstraint(ConversationStateSnapshot state, string key)
    {
        return state.Constraints.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static ExplorationSubtype ResolveExplorationSubtypeForMutation(
        string? existingSubtype,
        string normalizedMutationSegment,
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals mutationSignals,
        string? updatedPlaceTypes)
    {
        var resolvedSubtype = ConversationPolicyHelpers.ResolveExplorationSubtype(extraction, mutationSignals);
        if (!Enum.TryParse<ExplorationSubtype>(existingSubtype, true, out var parsedExistingSubtype))
        {
            return resolvedSubtype;
        }

        var explicitOpenPivot = mutationSignals.HasAtmosphericExplorationIntent
                                || mutationSignals.HasSafetyExplorationIntent
                                || normalizedMutationSegment.Contains("walk", StringComparison.Ordinal)
                                || normalizedMutationSegment.Contains("beach", StringComparison.Ordinal);
        if (explicitOpenPivot)
        {
            return ExplorationSubtype.Open;
        }

        var explicitStructuredPivot = mutationSignals.HasStructuredExplorationIntent
                                      || extraction.HasNearMeLanguage
                                      || extraction.HasExplicitLocality
                                      || extraction.TimeHints.Count > 0;
        if (explicitStructuredPivot)
        {
            return ExplorationSubtype.Structured;
        }

        if (!string.IsNullOrWhiteSpace(updatedPlaceTypes)
            && parsedExistingSubtype == ExplorationSubtype.Structured)
        {
            return ExplorationSubtype.Structured;
        }

        return parsedExistingSubtype;
    }

    private static string? ResolveUpdatedExplorationArea(
        string normalizedMutationSegment,
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals mutationSignals)
    {
        var resolvedFromExtraction = ConversationPolicyHelpers.ResolveExplorationArea(extraction);
        if (!string.IsNullOrWhiteSpace(resolvedFromExtraction))
        {
            return resolvedFromExtraction;
        }

        if (!mutationSignals.HasBranchingSignal && !mutationSignals.HasCorrectionSignal)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedMutationSegment))
        {
            return null;
        }

        var candidate = normalizedMutationSegment
            .Trim()
            .Trim(',', ';', ':', '.', '?', '!');
        candidate = candidate.StartsWith("in ", StringComparison.OrdinalIgnoreCase)
            ? candidate[3..].Trim()
            : candidate.StartsWith("around ", StringComparison.OrdinalIgnoreCase)
                ? candidate[7..].Trim()
                : candidate.StartsWith("near ", StringComparison.OrdinalIgnoreCase)
                    ? candidate[5..].Trim()
                    : candidate;

        if (!CompanionLocationGroundingParser.IsValidAreaHint(candidate))
        {
            return null;
        }

        var candidateExtraction = ConversationPolicyHelpers.ExtractLocalDiscovery(candidate);
        var candidateArea = ConversationPolicyHelpers.ResolveExplorationArea(candidateExtraction);
        if (!string.IsNullOrWhiteSpace(candidateArea))
        {
            return candidateArea;
        }

        // Reject candidate fragments that mutate non-location constraints; this prevents
        // brittle keyword-blocklists and keeps location override logic domain-agnostic.
        var mutatesNonLocationConstraints = candidateExtraction.PlaceTypeHints.Count > 0
                                            || candidateExtraction.PreferenceHints.Count > 0
                                            || candidateExtraction.TimeHints.Count > 0
                                            || candidateExtraction.AudienceHints.Count > 0;
        if (mutatesNonLocationConstraints)
        {
            return null;
        }

        if (candidateExtraction.IsLocalDiscoveryCandidate
            && !candidateExtraction.HasExplicitLocality
            && !candidateExtraction.HasNearMeLanguage)
        {
            return null;
        }

        return candidate;
    }

    private static bool SetConstraint(
        IDictionary<string, string> constraints,
        string key,
        string? newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return false;
        }

        if (constraints.TryGetValue(key, out var existing)
            && string.Equals(existing, newValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        constraints[key] = newValue.Trim();
        return true;
    }
}

public interface IFinancialActivationPolicy
{
    ConversationTurnStrategyDecision Apply(
        ConversationTurnStrategyDecision decision,
        ConversationStateSnapshot currentState,
        ConversationSignals signals);
}

public sealed class FinancialActivationPolicy : IFinancialActivationPolicy
{
    public ConversationTurnStrategyDecision Apply(
        ConversationTurnStrategyDecision decision,
        ConversationStateSnapshot currentState,
        ConversationSignals signals)
    {
        if (decision.ModeCandidate != ConversationMode.Financial)
        {
            return decision;
        }

        var reasonCodes = new HashSet<string>(decision.ReasonCodes, StringComparer.OrdinalIgnoreCase);
        var hasValidatedThread = ConversationPolicyHelpers.HasFinancialThreadEngagement(currentState);
        var mayActivateFinancial = currentState.ActiveMode == ConversationMode.Financial
                                   || (hasValidatedThread
                                       && (signals.HasExplicitConfirmation || signals.HasFinancialFocusSelection));

        if (mayActivateFinancial)
        {
            reasonCodes.Add("financial_activation_validated");
            return decision with
            {
                Strategy = decision.Strategy is ConversationBehaviorStrategy.ConfirmAndTransition
                    or ConversationBehaviorStrategy.FinancialPlaceholderTransition
                    ? decision.Strategy
                    : ConversationBehaviorStrategy.ConfirmAndTransition,
                ToolExecutionPermission = ToolExecutionPermission.Forbidden,
                ReasonCodes = reasonCodes.ToArray()
            };
        }

        var guidedReadiness = hasValidatedThread
            ? ConversationReadinessLevel.R2_DirectionKnown
            : signals.HasFinancialFocusSelection
                ? ConversationReadinessLevel.R2_DirectionKnown
                : ConversationReadinessLevel.R1_Vague;

        reasonCodes.Add(hasValidatedThread
            ? "financial_activation_confirmation_required"
            : "financial_activation_requires_validated_thread");

        return decision with
        {
            ModeCandidate = ConversationMode.Conversation,
            Strategy = signals.HasEmotionalFraming
                ? ConversationBehaviorStrategy.AcknowledgeAndGuide
                : ConversationBehaviorStrategy.SuggestAndClarify,
            Readiness = decision.Readiness with
            {
                To = guidedReadiness
            },
            ClarificationQuestion = null,
            SuggestedOptions = [],
            ToolExecutionPermission = ToolExecutionPermission.Forbidden,
            ReasonCodes = reasonCodes.ToArray()
        };
    }
}

public interface IExplorationSubtypeDecisionPolicy
{
    ExplorationSubtypeResolutionPlan DetermineResolution(
        string userMessage,
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext,
        FollowUpBindingType bindingType,
        ConversationTurnStrategyDecision strategyDecision);

    ExplorationSubtypeDecision Normalize(
        string userMessage,
        ConversationStateSnapshot state,
        ExplorationSubtypeDecision decision);
}

public sealed class ExplorationSubtypeDecisionPolicy : IExplorationSubtypeDecisionPolicy
{
    public ExplorationSubtypeResolutionPlan DetermineResolution(
        string userMessage,
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext,
        FollowUpBindingType bindingType,
        ConversationTurnStrategyDecision strategyDecision)
    {
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var signals = ConversationSignalAnalyzer.Analyze(userMessage);
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(userMessage);
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (HasStructuredFollowUpContext(resultContext, bindingType)
            && !ConversationPolicyHelpers.ShouldPreferExperientialOpen(extraction, signals))
        {
            reasonCodes.Add("exploration_subtype_followup_structured_fast_path");
            if (signals.HasComparisonSignal || signals.HasResultReferenceSignal)
            {
                reasonCodes.Add("exploration_subtype_followup_reference_signal");
            }

            if (bindingType is FollowUpBindingType.Refine or FollowUpBindingType.NewBranch)
            {
                reasonCodes.Add("exploration_subtype_followup_branch_or_refine");
            }

            return new ExplorationSubtypeResolutionPlan(
                RequiresModelReasoning: false,
                Decision: BuildDeterministicDecision(
                    subtype: ExplorationSubtype.Structured,
                    confidence: 0.93d,
                    source: "result_context_structured_fast_path",
                    extraction: extraction,
                    existingDecision: null,
                    additionalReasonCodes: reasonCodes),
                ResolutionSource: "result_context_structured_fast_path",
                ReasonCodes: reasonCodes.ToArray());
        }

        if (ConversationPolicyHelpers.HasConcreteStructuredSearchFrame(extraction, signals)
            && !ConversationPolicyHelpers.ShouldPreferExperientialOpen(extraction, signals))
        {
            reasonCodes.Add("exploration_subtype_structured_fast_path");
            if (extraction.PlaceTypeHints.Count > 0)
            {
                reasonCodes.Add("exploration_subtype_fast_path_place_type");
            }

            if (extraction.HasNearMeLanguage || extraction.HasExplicitLocality)
            {
                reasonCodes.Add("exploration_subtype_fast_path_location_frame");
            }

            if (extraction.TimeHints.Count > 0)
            {
                reasonCodes.Add("exploration_subtype_fast_path_operational_filter");
            }

            return new ExplorationSubtypeResolutionPlan(
                RequiresModelReasoning: false,
                Decision: BuildDeterministicDecision(
                    subtype: ExplorationSubtype.Structured,
                    confidence: 0.91d,
                    source: "structured_fast_path",
                    extraction: extraction,
                    existingDecision: null,
                    additionalReasonCodes: reasonCodes),
                ResolutionSource: "structured_fast_path",
                ReasonCodes: reasonCodes.ToArray());
        }

        if (ConversationPolicyHelpers.HasStrongOpenExplorationIntent(extraction, signals)
            && !ConversationPolicyHelpers.HasOperationalStructuredFollowUpCue(normalized))
        {
            reasonCodes.Add("exploration_subtype_open_fast_path");
            return new ExplorationSubtypeResolutionPlan(
                RequiresModelReasoning: false,
                Decision: BuildDeterministicDecision(
                    subtype: ExplorationSubtype.Open,
                    confidence: 0.89d,
                    source: "open_fast_path",
                    extraction: extraction,
                    existingDecision: null,
                    additionalReasonCodes: reasonCodes),
                ResolutionSource: "open_fast_path",
                ReasonCodes: reasonCodes.ToArray());
        }

        reasonCodes.Add("exploration_subtype_heavy_model_required");
        if (!string.IsNullOrWhiteSpace(ReadExplorationSubtypeConstraint(state)))
        {
            reasonCodes.Add("exploration_subtype_existing_state_insufficient");
        }

        if (resultContext is not null)
        {
            reasonCodes.Add("exploration_subtype_active_result_context_present");
        }

        return new ExplorationSubtypeResolutionPlan(
            RequiresModelReasoning: true,
            Decision: null,
            ResolutionSource: "model_heavy_required",
            ReasonCodes: reasonCodes.ToArray());
    }

    public ExplorationSubtypeDecision Normalize(
        string userMessage,
        ConversationStateSnapshot state,
        ExplorationSubtypeDecision decision)
    {
        var signals = ConversationSignalAnalyzer.Analyze(userMessage);
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(userMessage);
        var targetSubtype = decision.Subtype;

        if (ConversationPolicyHelpers.HasConcreteStructuredSearchFrame(extraction, signals)
            && !ConversationPolicyHelpers.ShouldPreferExperientialOpen(extraction, signals))
        {
            targetSubtype = ExplorationSubtype.Structured;
        }
        else if (ConversationPolicyHelpers.HasStrongOpenExplorationIntent(extraction, signals))
        {
            targetSubtype = ExplorationSubtype.Open;
        }
        else if (decision.Subtype == ExplorationSubtype.None)
        {
            targetSubtype = ConversationPolicyHelpers.ResolveExplorationSubtype(extraction, signals);
        }

        var source = decision.ReasonCodes.Any(
            static code => code.StartsWith("fallback_exploration_subtype_", StringComparison.OrdinalIgnoreCase))
            ? "model_fallback"
            : "model_heavy";

        return BuildDeterministicDecision(
            targetSubtype,
            decision.Confidence,
            source,
            extraction,
            decision,
            []);
    }

    private static ExplorationSubtypeDecision BuildDeterministicDecision(
        ExplorationSubtype subtype,
        double confidence,
        string source,
        LocalDiscoveryConstraintExtractionResult extraction,
        ExplorationSubtypeDecision? existingDecision,
        IEnumerable<string> additionalReasonCodes)
    {
        var reasonCodes = new HashSet<string>(
            (existingDecision?.ReasonCodes ?? [])
            .Concat(additionalReasonCodes),
            StringComparer.OrdinalIgnoreCase);
        var missingConstraints = existingDecision?.MissingConstraints.ToList() ?? [];
        var boundedConfidence = Math.Round(
            Math.Clamp(confidence, 0.55d, 0.97d),
            4,
            MidpointRounding.AwayFromZero);

        if (subtype == ExplorationSubtype.Structured)
        {
            missingConstraints.RemoveAll(static item => string.Equals(item, "location_or_area", StringComparison.OrdinalIgnoreCase));
            reasonCodes.Add("exploration_subtype_structured_evidence");
            reasonCodes.Add($"exploration_subtype_resolution_source_{source}");

            return new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: boundedConfidence,
                ToolPathEligible: true,
                PrimaryWhy: "The request is acting like a concrete local search with clear place, area, or operational framing.",
                MissingConstraints: missingConstraints
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ReasonCodes: reasonCodes.ToArray());
        }

        if (!missingConstraints.Contains("location_or_area", StringComparer.OrdinalIgnoreCase)
            && !extraction.HasNearMeLanguage
            && !extraction.HasExplicitLocality)
        {
            missingConstraints.Add("location_or_area");
        }

        reasonCodes.Add("exploration_subtype_vibe_first_open");
        reasonCodes.Add($"exploration_subtype_resolution_source_{source}");

        return new ExplorationSubtypeDecision(
            Subtype: ExplorationSubtype.Open,
            Confidence: boundedConfidence,
            ToolPathEligible: false,
            PrimaryWhy: "The request is still experiential, atmospheric, safety-oriented, or context-first rather than a concrete place lookup.",
            MissingConstraints: missingConstraints
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ReasonCodes: reasonCodes.ToArray());
    }

    private static bool HasStructuredFollowUpContext(
        ResultContextSnapshot? resultContext,
        FollowUpBindingType bindingType)
    {
        return resultContext?.SourceMode == ConversationMode.Exploration
               && resultContext.SourceSubtype == ExplorationSubtype.Structured
               && bindingType is FollowUpBindingType.BindPrior
                   or FollowUpBindingType.Refine
                   or FollowUpBindingType.NewBranch;
    }

    private static bool HasOperationalRefinementCue(string normalized)
    {
        return ConversationPolicyHelpers.HasOperationalStructuredFollowUpCue(normalized);
    }

    private static string? ReadExplorationSubtypeConstraint(ConversationStateSnapshot state)
    {
        return state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationSubtype, out var subtype)
            && !string.IsNullOrWhiteSpace(subtype)
            ? subtype.Trim()
            : null;
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }
}

public interface IToolGuardWarningPolicy
{
    IReadOnlyList<string> Determine(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision);
}

public sealed class ToolGuardWarningPolicy : IToolGuardWarningPolicy
{
    public IReadOnlyList<string> Determine(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision)
    {
        if (decision.ToolExecutionPermission != ToolExecutionPermission.Forbidden)
        {
            return [];
        }

        var blockedToolIntent = decision.ReasonCodes.Contains(
                                    "readiness_guard_financial_never_tool_ready",
                                    StringComparer.OrdinalIgnoreCase)
                                || decision.ReasonCodes.Contains(
                                    "behavior_guard_near_me_requires_location_evidence",
                                    StringComparer.OrdinalIgnoreCase)
                                || (decision.ModeCandidate == ConversationMode.Exploration
                                    && (decision.Strategy == ConversationBehaviorStrategy.ToolReadyHandoff
                                        || explorationSubtypeDecision?.ToolPathEligible == true
                                        || decision.ReasonCodes.Contains(
                                            "readiness_guard_open_exploration_never_tool_ready",
                                            StringComparer.OrdinalIgnoreCase)));

        return blockedToolIntent ? ["chat.tool.guard_blocked"] : [];
    }
}

internal static class ConversationPolicyHelpers
{
    private static readonly LocalDiscoveryConstraintExtractor DiscoveryConstraintExtractor = new();

    public static LocalDiscoveryConstraintExtractionResult ExtractLocalDiscovery(string? userMessage)
        => DiscoveryConstraintExtractor.Extract(userMessage);

    public static string ResolveSemanticFamily(
        ConversationStateSnapshot state,
        ConversationSignals signals)
    {
        if (signals.HasFinancialSignal)
        {
            return ConversationSemanticFamilies.Financial;
        }

        if (signals.HasExplorationSignal)
        {
            return ConversationSemanticFamilies.Exploration;
        }

        if (signals.HasFactualQuestion && signals.HasCompleteQuestion)
        {
            return ConversationSemanticFamilies.GeneralKnowledge;
        }

        if (state.Constraints.TryGetValue(ConversationConstraintKeys.SemanticFamily, out var semanticFamily)
            && !string.IsNullOrWhiteSpace(semanticFamily))
        {
            return semanticFamily.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(state.InferredIntent))
        {
            var inferred = state.InferredIntent.Trim().ToLowerInvariant();
            if (inferred is ConversationSemanticFamilies.Exploration
                or ConversationSemanticFamilies.Financial
                or ConversationSemanticFamilies.GeneralKnowledge)
            {
                return inferred;
            }
        }

        return ConversationSemanticFamilies.Conversation;
    }

    public static bool HasFinancialThreadEngagement(ConversationStateSnapshot state)
    {
        return state.ActiveMode == ConversationMode.Financial
               || string.Equals(
                   state.TransitionIntent,
                   ConversationTransitionIntents.FinancialValidationPending,
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(state.InferredIntent, ConversationSemanticFamilies.Financial, StringComparison.OrdinalIgnoreCase)
               || (state.Constraints.TryGetValue(ConversationConstraintKeys.SemanticFamily, out var semanticFamily)
                   && string.Equals(semanticFamily, ConversationSemanticFamilies.Financial, StringComparison.OrdinalIgnoreCase))
               || !string.IsNullOrWhiteSpace(ReadConstraint(state, ConversationConstraintKeys.FinancialFocus));
    }

    public static string? ResolveFinancialFocus(string normalized)
    {
        if (normalized.Contains("subscription", StringComparison.Ordinal))
        {
            return "subscriptions";
        }

        if (normalized.Contains("spending", StringComparison.Ordinal)
            || normalized.Contains("spend", StringComparison.Ordinal)
            || normalized.Contains("expenses", StringComparison.Ordinal)
            || normalized.Contains("expense", StringComparison.Ordinal))
        {
            return "spending";
        }

        if (normalized.Contains("budget", StringComparison.Ordinal))
        {
            return "budget";
        }

        if (normalized.Contains("saving", StringComparison.Ordinal)
            || normalized.Contains("save", StringComparison.Ordinal))
        {
            return "savings";
        }

        return null;
    }

    public static string ResolveMutationSegment(string? userMessage)
    {
        var normalized = (userMessage ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lowered = normalized.ToLowerInvariant();

        if (TryTakeAfterMarker(lowered, normalized, "actually", out var afterActually))
        {
            return afterActually;
        }

        if (TryTakeAfterMarker(lowered, normalized, "what about", out var afterWhatAbout))
        {
            return TrimTrailingInstead(afterWhatAbout);
        }

        if (TryTakeAfterMarker(lowered, normalized, "try ", out var afterTry))
        {
            return TrimTrailingInstead(afterTry);
        }

        if (TryTakeAfterMarker(lowered, normalized, "same thing but in", out var afterSameThingButIn))
        {
            return TrimTrailingInstead(afterSameThingButIn);
        }

        if (TryTakeAfterMarker(lowered, normalized, "same thing in", out var afterSameThingIn))
        {
            return TrimTrailingInstead(afterSameThingIn);
        }

        if (TryTakeAfterMarker(lowered, normalized, "not near me", out var afterNotNearMe))
        {
            return afterNotNearMe.TrimStart(',', ' ');
        }

        if (lowered.Contains("instead", StringComparison.Ordinal))
        {
            var insteadIndex = lowered.LastIndexOf("instead", StringComparison.Ordinal);
            if (insteadIndex > 0)
            {
                var beforeInstead = normalized[..insteadIndex].Trim().Trim(',', ';', ':', '.', '?', '!');
                var commaIndex = beforeInstead.LastIndexOf(',');
                if (commaIndex >= 0 && commaIndex < beforeInstead.Length - 1)
                {
                    return beforeInstead[(commaIndex + 1)..].Trim();
                }

                return beforeInstead;
            }
        }

        if (TryTakeAfterMarker(lowered, normalized, "forget", out var afterForget))
        {
            var commaIndex = afterForget.LastIndexOf(',');
            if (commaIndex >= 0 && commaIndex < afterForget.Length - 1)
            {
                return afterForget[(commaIndex + 1)..].Trim();
            }

            return afterForget;
        }

        return normalized;
    }

    public static string? ResolveExplorationArea(LocalDiscoveryConstraintExtractionResult extraction)
    {
        if (extraction.HasExplicitLocality && !string.IsNullOrWhiteSpace(extraction.LocalityHint))
        {
            return extraction.LocalityHint!.Trim();
        }

        if (extraction.HasNearMeLanguage)
        {
            return "near_me";
        }

        return null;
    }

    public static string? ResolveExplorationPlaceTypes(LocalDiscoveryConstraintExtractionResult extraction)
    {
        return extraction.PlaceTypeHints.Count > 0
            ? string.Join('|', extraction.PlaceTypeHints.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            : null;
    }

    public static string? ResolveExplorationPreferences(
        string normalized,
        LocalDiscoveryConstraintExtractionResult extraction)
    {
        var preferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preference in extraction.PreferenceHints)
        {
            preferences.Add(preference);
        }

        if (ContainsAny(normalized, "quiet", "calm", "peaceful"))
        {
            preferences.Add("quiet");
        }

        if (ContainsAny(normalized, "lively", "vibrant", "busy", "energetic"))
        {
            preferences.Add("lively");
        }

        if (ContainsAny(normalized, "safe", "lighting", "well lit", "late walk", "night"))
        {
            preferences.Add("safe");
        }

        if (ContainsAny(normalized, "view", "beach view", "waterfront", "scenic"))
        {
            preferences.Add("scenic");
        }

        if (ContainsAny(normalized, "parking", "car park", "carpark"))
        {
            preferences.Add("parking");
        }

        if (ContainsAny(normalized, "seating", "seat", "outdoor seating"))
        {
            preferences.Add("seating");
        }

        if (ContainsAny(normalized, "top rated", "highly rated", "best rated", "rating"))
        {
            preferences.Add("rating");
        }

        return preferences.Count > 0
            ? string.Join('|', preferences.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            : null;
    }

    public static string? ResolveExplorationTime(LocalDiscoveryConstraintExtractionResult extraction)
    {
        return extraction.TimeHints.Count > 0
            ? string.Join('|', extraction.TimeHints.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            : null;
    }

    public static bool HasStrongStructuredExplorationIntent(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        return HasConcreteStructuredSearchFrame(extraction, signals);
    }

    public static bool HasConcreteStructuredSearchFrame(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        var hasDomainFrame = extraction.PlaceTypeHints.Count > 0 || signals.HasConcretePlaceSignal;
        var hasLocationFrame = extraction.HasNearMeLanguage
                               || extraction.HasExplicitLocality
                               || extraction.TimeHints.Count > 0
                               || signals.HasExplicitLocation;
        var hasAnchoredLocality = extraction.HasNearMeLanguage || extraction.HasExplicitLocality;
        var hasOperationalFrame = extraction.TimeHints.Count > 0
                                  || extraction.ReasonCodes.Contains("local_discovery_open_token", StringComparer.OrdinalIgnoreCase);
        var hasStructuredSkeleton = extraction.IsLocalDiscoveryCandidate
                                    && hasAnchoredLocality
                                    && hasOperationalFrame;

        return (hasDomainFrame && hasLocationFrame) || hasStructuredSkeleton;
    }

    public static bool HasAtmosphericExplorationIntent(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        return signals.HasAtmosphericExplorationIntent
               || signals.HasSafetyExplorationIntent
               || (signals.HasExplorationSignal
                   && !HasStrongStructuredExplorationIntent(extraction, signals)
                   && !extraction.HasNearMeLanguage
                   && !extraction.HasExplicitLocality
                   && extraction.TimeHints.Count == 0);
    }

    public static bool ShouldPreferExperientialOpen(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        return HasAtmosphericExplorationIntent(extraction, signals)
               && !HasConcreteStructuredSearchFrame(extraction, signals);
    }

    public static bool HasStrongOpenExplorationIntent(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        return ShouldPreferExperientialOpen(extraction, signals)
               || (signals.HasExplorationSignal
                   && extraction.PlaceTypeHints.Count == 0
                   && !extraction.HasNearMeLanguage
                   && !extraction.HasExplicitLocality
                   && extraction.TimeHints.Count == 0);
    }

    public static ExplorationSubtype ResolveExplorationSubtype(
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        return HasConcreteStructuredSearchFrame(extraction, signals)
               && !ShouldPreferExperientialOpen(extraction, signals)
            ? ExplorationSubtype.Structured
            : ExplorationSubtype.Open;
    }

    public static bool HasOperationalStructuredFollowUpCue(string normalized)
    {
        var extraction = ExtractLocalDiscovery(normalized);
        if (extraction.PreferenceHints.Count > 0
            || extraction.TimeHints.Count > 0
            || extraction.AudienceHints.Count > 0)
        {
            return true;
        }

        return ContainsAny(
            normalized,
            "compare",
            "versus",
            "vs",
            "against",
            "filter",
            "closer",
            "closest",
            "distance",
            "reviews",
            "accessible");
    }

    public static bool HasDeterministicStructuredFollowUpIntent(
        ResultContextSnapshot? resultContext,
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals,
        string normalized)
    {
        if (resultContext?.SourceMode != ConversationMode.Exploration
            || resultContext.SourceSubtype != ExplorationSubtype.Structured
            || ShouldPreferExperientialOpen(extraction, signals))
        {
            return false;
        }

        if (signals.HasComparisonSignal || signals.HasResultReferenceSignal)
        {
            return true;
        }

        if (HasOperationalStructuredFollowUpCue(normalized))
        {
            return true;
        }

        return signals.HasBranchingSignal
               && (extraction.HasExplicitLocality
                   || extraction.HasNearMeLanguage
                   || extraction.TimeHints.Count > 0
                   || extraction.PlaceTypeHints.Count > 0);
    }

    public static ExplorationSubtypeDecision BuildFallbackExplorationSubtypeDecision(string userMessage)
    {
        var signals = ConversationSignalAnalyzer.Analyze(userMessage);
        var extraction = ExtractLocalDiscovery(userMessage);
        var subtype = ResolveExplorationSubtype(extraction, signals);

        if (subtype == ExplorationSubtype.Structured)
        {
            return new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: 0.76d,
                ToolPathEligible: true,
                PrimaryWhy: "The request includes a concrete place/domain search with locality or operational constraints.",
                MissingConstraints: [],
                ReasonCodes: ["fallback_exploration_subtype_structured"]);
        }

        return new ExplorationSubtypeDecision(
            Subtype: ExplorationSubtype.Open,
            Confidence: 0.82d,
            ToolPathEligible: false,
            PrimaryWhy: "The request is still experiential, atmospheric, safety-oriented, or context-first.",
            MissingConstraints: !extraction.HasNearMeLanguage && !extraction.HasExplicitLocality
                ? ["location_or_area"]
                : [],
            ReasonCodes: ["fallback_exploration_subtype_open"]);
    }

    public static ConversationReadinessLevel DowngradeReadiness(ConversationReadinessLevel current)
    {
        return current switch
        {
            ConversationReadinessLevel.R4_ToolReady => ConversationReadinessLevel.R3_StructuredIncomplete,
            ConversationReadinessLevel.R3_StructuredIncomplete => ConversationReadinessLevel.R2_DirectionKnown,
            ConversationReadinessLevel.R2_DirectionKnown => ConversationReadinessLevel.R1_Vague,
            _ => current
        };
    }

    private static string? ReadConstraint(ConversationStateSnapshot state, string key)
    {
        return state.Constraints.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }

    private static bool TryTakeAfterMarker(
        string lowered,
        string original,
        string marker,
        out string result)
    {
        result = string.Empty;
        var markerIndex = lowered.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var start = markerIndex + marker.Length;
        if (start >= original.Length)
        {
            return false;
        }

        result = original[start..].Trim().Trim(',', ';', ':', '.', '?', '!');
        return result.Length > 0;
    }

    private static string TrimTrailingInstead(string value)
    {
        var lowered = value.ToLowerInvariant();
        return lowered.EndsWith("instead", StringComparison.Ordinal)
            ? value[..^"instead".Length].Trim().Trim(',', ';', ':', '.', '?', '!')
            : value;
    }
}
