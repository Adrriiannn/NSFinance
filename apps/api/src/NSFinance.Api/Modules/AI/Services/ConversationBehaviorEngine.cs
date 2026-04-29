namespace NSFinance.Api.Modules.AI.Services;

public interface IConversationBehaviorEngine
{
    Task<ConversationBehaviorResult> EvaluateAsync(
        ConversationBehaviorRequest request,
        CancellationToken cancellationToken);
}

public sealed class ConversationBehaviorEngine(
    IConversationDecisionEngine decisionEngine,
    IConversationModelRoutingPolicy modelRoutingPolicy,
    IReadinessTransitionPolicy readinessTransitionPolicy,
    IFollowUpBindingPolicy followUpBindingPolicy,
    IContradictionResolutionPolicy contradictionResolutionPolicy,
    IFinancialActivationPolicy financialActivationPolicy,
    IExplorationSubtypeDecisionPolicy explorationSubtypeDecisionPolicy,
    IToolGuardWarningPolicy toolGuardWarningPolicy,
    IChatTelemetry telemetry) : IConversationBehaviorEngine
{
    public async Task<ConversationBehaviorResult> EvaluateAsync(
        ConversationBehaviorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clarificationRewrite = ResolveClarificationTurnRewrite(
            request.Request,
            request.EffectiveState);
        var workingRequest = clarificationRewrite.Request;
        var startingState = clarificationRewrite.State;
        var interpretation = TurnInterpretationMetadataMapper.ReadInterpretation(request.ClientMetadata);
        var signals = ConversationSignalAnalyzer.Analyze(workingRequest.UserMessage, interpretation);

        var resultContextReadResult = request.ResultContextReadResult
                                      ?? new ResultContextReadResult(
                                          ActiveResultContext: request.ResultContext,
                                          BindingClassification: ResultContextBindingClassification.None,
                                          UsedClientResultSetId: false,
                                          ExpiredBindingCleared: false,
                                          ReasonCodes: []);

        var binding = followUpBindingPolicy.Determine(
            workingRequest,
            startingState,
            resultContextReadResult);
        if (clarificationRewrite.SuppressResultContextBinding
            && binding.BindingType is not FollowUpBindingType.NewTopic)
        {
            binding = binding with
            {
                BindingType = FollowUpBindingType.None,
                ActiveResultSetId = null,
                SelectedEntityId = null,
                ReasonCodes = binding.ReasonCodes
                    .Concat(["clarification_slot_fill_overrides_result_context"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        var contradictionResolved = contradictionResolutionPolicy.Apply(
            startingState,
            workingRequest.UserMessage,
            binding.BindingType,
            signals);
        var effectiveBindingType = contradictionResolved.BindingTypeOverride ?? binding.BindingType;

        var effectiveState = contradictionResolved.State;
        var primaryDecisionSelection = modelRoutingPolicy.SelectPrimaryDecision(
            request with
            {
                Request = workingRequest,
                EffectiveState = effectiveState
            },
            signals,
            effectiveBindingType,
            effectiveState);
        var primaryDecisionEvaluation = await decisionEngine.EvaluateAsync(
            request with
            {
                Request = workingRequest,
                EffectiveState = effectiveState
            },
            primaryDecisionSelection,
            cancellationToken);
        var rawDecision = primaryDecisionEvaluation.Decision;

        var normalizedDecision = EnforceBehaviorRules(rawDecision, signals, effectiveState);
        var financiallyGuardedDecision = financialActivationPolicy.Apply(
            normalizedDecision,
            effectiveState,
            signals);
        var scopeGovernedDecision = ApplyConversationIntelligenceGuard(
            ApplyScopeGovernance(financiallyGuardedDecision, interpretation),
            request.ConversationIntelligence,
            effectiveState);
        ExplorationSubtypeDecision? explorationSubtypeDecision = null;
        ConversationModelSelectionPlan? explorationSubtypeModelSelection = null;
        var explorationSubtypeResolutionSource = "not_applicable";
        var decisionModelInvocationCount = primaryDecisionEvaluation.UsedModelInvocation ? 1 : 0;
        var heavyDecisionModelCallCount = primaryDecisionEvaluation.UsedModelInvocation
                                          && primaryDecisionEvaluation.ModelSelection.SelectionKind == ConversationModelSelectionKind.HeavyReasoning
            ? 1
            : 0;
        var fastDecisionModelCallCount = primaryDecisionEvaluation.UsedModelInvocation
                                         && primaryDecisionEvaluation.ModelSelection.SelectionKind == ConversationModelSelectionKind.Fast
            ? 1
            : 0;
        if (scopeGovernedDecision.ModeCandidate == ConversationMode.Exploration)
        {
            var resolutionPlan = explorationSubtypeDecisionPolicy.DetermineResolution(
                workingRequest.UserMessage,
                effectiveState,
                request.ResultContext,
                effectiveBindingType,
                scopeGovernedDecision);
            explorationSubtypeResolutionSource = resolutionPlan.ResolutionSource;

            if (!resolutionPlan.RequiresModelReasoning)
            {
                explorationSubtypeDecision = resolutionPlan.Decision;
                explorationSubtypeModelSelection = modelRoutingPolicy.SelectExplorationSubtype(
                    new ConversationModeRequest(
                        Request: workingRequest,
                        ContextMessages: request.ContextMessages,
                        ContextSummary: request.ContextSummary,
                        State: effectiveState,
                        ResultContext: request.ResultContext,
                        StrategyDecision: scopeGovernedDecision,
                        ExplorationSubtypeDecision: resolutionPlan.Decision,
                        ClientMetadata: request.ClientMetadata,
                        ConversationIntelligence: request.ConversationIntelligence),
                    signals,
                    resolutionPlan,
                    primaryDecisionEvaluation.ModelSelection);
            }
            else
            {
                explorationSubtypeModelSelection = modelRoutingPolicy.SelectExplorationSubtype(
                    new ConversationModeRequest(
                        Request: workingRequest,
                        ContextMessages: request.ContextMessages,
                        ContextSummary: request.ContextSummary,
                        State: effectiveState,
                        ResultContext: request.ResultContext,
                        StrategyDecision: scopeGovernedDecision,
                        ExplorationSubtypeDecision: null,
                        ClientMetadata: request.ClientMetadata,
                        ConversationIntelligence: request.ConversationIntelligence),
                    signals,
                    resolutionPlan,
                    primaryDecisionEvaluation.ModelSelection);
                var subtypeEvaluation = await decisionEngine.DetermineExplorationSubtypeAsync(
                    new ConversationModeRequest(
                        Request: workingRequest,
                        ContextMessages: request.ContextMessages,
                        ContextSummary: request.ContextSummary,
                        State: effectiveState,
                        ResultContext: request.ResultContext,
                        StrategyDecision: scopeGovernedDecision,
                        ExplorationSubtypeDecision: null,
                        ClientMetadata: request.ClientMetadata,
                        ConversationIntelligence: request.ConversationIntelligence),
                    explorationSubtypeModelSelection,
                    cancellationToken);
                explorationSubtypeDecision = explorationSubtypeDecisionPolicy.Normalize(
                    workingRequest.UserMessage,
                    effectiveState,
                    subtypeEvaluation.Decision);
                explorationSubtypeResolutionSource = subtypeEvaluation.Decision.ReasonCodes.Any(
                    static code => code.StartsWith("fallback_exploration_subtype_", StringComparison.OrdinalIgnoreCase))
                    ? "model_fallback"
                    : subtypeEvaluation.ModelSelection.SelectionKind == ConversationModelSelectionKind.HeavyReasoning
                        ? "model_heavy"
                        : "model_fast";
                if (subtypeEvaluation.UsedModelInvocation)
                {
                    decisionModelInvocationCount += 1;
                }

                if (subtypeEvaluation.UsedModelInvocation
                    && subtypeEvaluation.ModelSelection.SelectionKind == ConversationModelSelectionKind.HeavyReasoning)
                {
                    heavyDecisionModelCallCount += 1;
                }
                else if (subtypeEvaluation.UsedModelInvocation
                         && subtypeEvaluation.ModelSelection.SelectionKind == ConversationModelSelectionKind.Fast)
                {
                    fastDecisionModelCallCount += 1;
                }
            }

            await telemetry.TrackAsync(
                "chat.turn.exploration_subtype_resolution",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = workingRequest.CorrelationId,
                    ["resolutionSource"] = explorationSubtypeResolutionSource,
                    ["usedModelReasoning"] = explorationSubtypeModelSelection?.SelectionKind != ConversationModelSelectionKind.Deterministic
                                             && resolutionPlan.RequiresModelReasoning,
                    ["heavySubtypeCallSkipped"] = explorationSubtypeModelSelection?.SelectionKind != ConversationModelSelectionKind.HeavyReasoning,
                    ["decisionModelCallCount"] = decisionModelInvocationCount,
                    ["subtypeSelectionKind"] = explorationSubtypeModelSelection?.SelectionKind.ToString(),
                    ["subtypeSelectionReason"] = explorationSubtypeModelSelection?.SelectionReason,
                    ["bindingType"] = effectiveBindingType.ToString(),
                    ["hasActiveResultContext"] = request.ResultContext is not null,
                    ["subtype"] = explorationSubtypeDecision?.Subtype.ToString(),
                    ["reasonCodes"] = (explorationSubtypeDecision?.ReasonCodes ?? resolutionPlan.ReasonCodes).ToArray()
                },
                cancellationToken);
        }

        var finalModeCandidateDecision = scopeGovernedDecision;

        if (finalModeCandidateDecision.ModeCandidate == ConversationMode.Exploration
            && explorationSubtypeDecision?.Subtype == ExplorationSubtype.Structured
            && (CompanionLocationGroundingParser.RequiresCurrentLocation(workingRequest.UserMessage)
                || interpretation?.LocationPlan.NearMeSemantic == true
                || interpretation?.LocationPlan.RequiresLocation == true))
        {
            var grounding = CompanionLocationGroundingParser.Parse(request.ClientMetadata, effectiveState);
            var hasFreshAreaMemory = HasFreshAreaMemory(effectiveState);
            if (!grounding.HasCoordinates && !grounding.HasTypedArea && !hasFreshAreaMemory)
            {
                finalModeCandidateDecision = finalModeCandidateDecision with
                {
                    Strategy = ConversationBehaviorStrategy.SuggestAndClarify,
                    Readiness = finalModeCandidateDecision.Readiness with
                    {
                        To = ConversationReadinessLevel.R3_StructuredIncomplete
                    },
                    ToolExecutionPermission = ToolExecutionPermission.Forbidden,
                    ClarificationQuestion = finalModeCandidateDecision.ClarificationQuestion
                                            ?? "I can search once you share an area or allow location access.",
                    SuggestedOptions = finalModeCandidateDecision.SuggestedOptions.Count > 0
                        ? finalModeCandidateDecision.SuggestedOptions
                        : ["Share an area", "Enable location", "Keep it exploratory"],
                    ReasonCodes = finalModeCandidateDecision.ReasonCodes
                        .Concat(["behavior_guard_near_me_requires_location_evidence"])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }
        }

        var guardedReadiness = readinessTransitionPolicy.Apply(
            effectiveState,
            finalModeCandidateDecision,
            explorationSubtypeDecision);
        var finalDecision = finalModeCandidateDecision with
        {
            Readiness = guardedReadiness.Transition,
            ToolExecutionPermission = guardedReadiness.ToolExecutionPermission,
            FollowUpBindingType = effectiveBindingType,
            ReasonCodes = finalModeCandidateDecision.ReasonCodes
                .Concat(binding.ReasonCodes)
                .Concat(contradictionResolved.ReasonCodes)
                .Concat(clarificationRewrite.ReasonCodes)
                .Concat(guardedReadiness.ReasonCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var finalState = effectiveState with
        {
            SchemaVersion = 2,
            ActiveMode = ConversationMode.Conversation,
            ModeCandidate = finalDecision.ModeCandidate,
            ReadinessLevel = finalDecision.Readiness.To,
            InferredIntent = InferIntent(finalDecision, signals),
            MissingConstraints = DetermineMissingConstraints(finalDecision, explorationSubtypeDecision),
            ConversationSignals = signals,
            LastClarificationPrompt = finalDecision.ClarificationQuestion,
            LastSuggestedOptions = finalDecision.SuggestedOptions,
            SelectedEntityId = finalDecision.FollowUpBindingType is FollowUpBindingType.None or FollowUpBindingType.NewTopic
                ? null
                : binding.SelectedEntityId ?? effectiveState.SelectedEntityId,
            TransitionIntent = ResolveTransitionIntent(finalDecision, effectiveState, signals),
            Confidence = finalDecision.Confidence,
            NeedsFollowUp = finalDecision.Readiness.To <= ConversationReadinessLevel.R3_StructuredIncomplete,
            FollowUpBindingType = finalDecision.FollowUpBindingType,
            ResultContextRef = finalDecision.FollowUpBindingType is FollowUpBindingType.None or FollowUpBindingType.NewTopic
                ? null
                : binding.ActiveResultSetId is Guid activeResultSetId
                ? new ConversationResultContextReference(
                    ActiveResultSetId: activeResultSetId,
                    BranchRootResultSetId: resultContextReadResult.ActiveResultContext?.BranchRootResultSetId,
                    ActiveUntilUtc: resultContextReadResult.ActiveResultContext?.ActiveUntilUtc,
                    ExpiresUtc: resultContextReadResult.ActiveResultContext?.ExpiresUtc)
                : effectiveState.ResultContextRef,
            LoopGuards = UpdateLoopGuards(effectiveState.LoopGuards, finalDecision),
            PendingClarification = ResolvePendingClarificationState(
                finalDecision,
                explorationSubtypeDecision,
                effectiveState,
                request.ClientMetadata)
        };

        var routeToModeHandler = ShouldRouteToModeHandler(finalDecision, explorationSubtypeDecision);
        var compositionRequest = routeToModeHandler
            ? null
            : BuildDirectModeCompositionRequest(workingRequest.UserMessage, finalDecision, finalState);
        var warnings = new HashSet<string>(
            toolGuardWarningPolicy.Determine(finalDecision, explorationSubtypeDecision),
            StringComparer.OrdinalIgnoreCase);

        await telemetry.TrackAsync(
            "chat.turn.strategy_selected",
            new Dictionary<string, object?>
            {
                ["correlationId"] = workingRequest.CorrelationId,
                ["strategy"] = finalDecision.Strategy.ToString(),
                ["modeCandidate"] = finalDecision.ModeCandidate.ToString(),
                ["readinessFrom"] = finalDecision.Readiness.From.ToString(),
                ["readinessTo"] = finalDecision.Readiness.To.ToString(),
                ["explorationSubtypeResolutionSource"] = explorationSubtypeResolutionSource,
                ["decisionModelCallCount"] = decisionModelInvocationCount,
                ["heavyDecisionModelCallCount"] = heavyDecisionModelCallCount,
                ["fastDecisionModelCallCount"] = fastDecisionModelCallCount,
                ["primaryDecisionSelectionKind"] = primaryDecisionEvaluation.ModelSelection.SelectionKind.ToString(),
                ["primaryDecisionSelectionReason"] = primaryDecisionEvaluation.ModelSelection.SelectionReason,
                ["primaryDecisionEscalationJustification"] = primaryDecisionEvaluation.ModelSelection.EscalationJustification,
                ["subtypeSelectionKind"] = explorationSubtypeModelSelection?.SelectionKind.ToString(),
                ["subtypeSelectionReason"] = explorationSubtypeModelSelection?.SelectionReason
            },
            cancellationToken);

        return new ConversationBehaviorResult(
            StrategyDecision: finalDecision,
            State: finalState,
            RouteToModeHandler: routeToModeHandler,
            StayInDirectMode: !routeToModeHandler,
            TargetMode: routeToModeHandler ? finalDecision.ModeCandidate : ConversationMode.Conversation,
            ExplorationSubtypeDecision: explorationSubtypeDecision,
            CompositionRequest: compositionRequest,
            PrimaryDecisionModelSelection: primaryDecisionEvaluation.ModelSelection,
            ExplorationSubtypeModelSelection: explorationSubtypeModelSelection,
            DecisionModelCallCount: decisionModelInvocationCount,
            HeavyDecisionModelCallCount: heavyDecisionModelCallCount,
            FastDecisionModelCallCount: fastDecisionModelCallCount,
            ReasonCodes: finalDecision.ReasonCodes,
            Warnings: warnings.ToArray());
    }

    private static bool HasFreshAreaMemory(ConversationStateSnapshot state)
    {
        if (!state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var area)
            || string.IsNullOrWhiteSpace(area)
            || string.Equals(area, "near_me", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!state.Constraints.TryGetValue(ConversationConstraintKeys.ExplorationAreaExpiresUtc, out var expiresRaw)
            || string.IsNullOrWhiteSpace(expiresRaw)
            || !DateTimeOffset.TryParse(expiresRaw, out var expiresUtc))
        {
            return false;
        }

        return expiresUtc >= DateTimeOffset.UtcNow;
    }

    private static ConversationTurnStrategyDecision ApplyScopeGovernance(
        ConversationTurnStrategyDecision decision,
        TurnInterpretationV2? interpretation)
    {
        if (interpretation is null
            || interpretation.InScopeVerdict == TurnInterpretationInScopeVerdict.InScope)
        {
            return decision;
        }

        if (interpretation.InScopeVerdict == TurnInterpretationInScopeVerdict.Borderline)
        {
            return decision with
            {
                ReasonCodes = decision.ReasonCodes
                    .Concat(["scope_governance_borderline_allowed"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        return decision with
        {
            Strategy = ConversationBehaviorStrategy.SuggestAndClarify,
            ModeCandidate = ConversationMode.Conversation,
            Readiness = decision.Readiness with
            {
                To = ConversationReadinessLevel.R2_DirectionKnown
            },
            ToolExecutionPermission = ToolExecutionPermission.Forbidden,
            ClarificationQuestion = "I can help best with finance, nearby places, or planning decisions tied to money. Which direction should we take?",
            SuggestedOptions =
            [
                "Nearby places",
                "Budget guidance",
                "Spending question"
            ],
            ReasonCodes = decision.ReasonCodes
                .Concat(["scope_governance_soft_redirect"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static ConversationTurnStrategyDecision ApplyConversationIntelligenceGuard(
        ConversationTurnStrategyDecision decision,
        ConversationIntelligenceResult? intelligence,
        ConversationStateSnapshot state)
    {
        if (intelligence is null)
        {
            return decision;
        }

        if (string.Equals(intelligence.ConversationPhase, "closing", StringComparison.OrdinalIgnoreCase))
        {
            return decision with
            {
                Strategy = ConversationBehaviorStrategy.DirectAnswer,
                ModeCandidate = ConversationMode.Conversation,
                Readiness = decision.Readiness with
                {
                    From = state.ReadinessLevel,
                    To = ConversationReadinessLevel.R1_Vague
                },
                FollowUpBindingType = FollowUpBindingType.None,
                ClarificationQuestion = null,
                SuggestedOptions = [],
                ToolExecutionPermission = ToolExecutionPermission.Forbidden,
                ReasonCodes = decision.ReasonCodes
                    .Concat(["conversation_intelligence_closing"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        if (intelligence.ShouldClarify)
        {
            return decision with
            {
                Strategy = ConversationBehaviorStrategy.ClarifyOnly,
                ToolExecutionPermission = ToolExecutionPermission.Forbidden,
                ReasonCodes = decision.ReasonCodes
                    .Concat(["conversation_intelligence_clarify"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        if (intelligence.ShouldExecuteTool
            && string.Equals(intelligence.NextAction.Type, "execute_search", StringComparison.OrdinalIgnoreCase)
            && decision.ModeCandidate == ConversationMode.Exploration)
        {
            return decision with
            {
                Strategy = ConversationBehaviorStrategy.ToolReadyHandoff,
                Readiness = decision.Readiness with
                {
                    From = state.ReadinessLevel,
                    To = ConversationReadinessLevel.R4_ToolReady
                },
                ToolExecutionPermission = ToolExecutionPermission.EligibleIfGuardPasses,
                ReasonCodes = decision.ReasonCodes
                    .Concat(["conversation_intelligence_execute_search"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        if (intelligence.TaskState.TargetPreviousResults
            && decision.FollowUpBindingType is FollowUpBindingType.None or FollowUpBindingType.NewTopic)
        {
            return decision with
            {
                FollowUpBindingType = FollowUpBindingType.Refine,
                ReasonCodes = decision.ReasonCodes
                    .Concat(["conversation_intelligence_prior_result_target"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        return decision;
    }

    private static ConversationTurnStrategyDecision EnforceBehaviorRules(
        ConversationTurnStrategyDecision decision,
        ConversationSignals signals,
        ConversationStateSnapshot state)
    {
        var reasonCodes = new HashSet<string>(decision.ReasonCodes, StringComparer.OrdinalIgnoreCase);
        var next = decision;
        var requiresAcknowledgeAndGuide = signals.HasEmotionalFraming || signals.HasSubjectiveLanguage;

        if ((decision.Readiness.To == ConversationReadinessLevel.R0_Unknown
             || decision.Readiness.To == ConversationReadinessLevel.R1_Vague)
            && decision.Strategy == ConversationBehaviorStrategy.DirectAnswer
            && !(signals.HasFactualQuestion && signals.HasCompleteQuestion))
        {
            next = next with
            {
                Strategy = ConversationBehaviorStrategy.SuggestAndClarify,
                ClarificationQuestion = next.ClarificationQuestion ?? "What specific part do you want to focus on first?",
                SuggestedOptions = next.SuggestedOptions.Count > 0
                    ? next.SuggestedOptions
                    : ["Clarify the goal", "Share a constraint"]
            };
            reasonCodes.Add("behavior_guard_no_direct_answer_below_r2");
        }

        if ((decision.Readiness.To == ConversationReadinessLevel.R1_Vague
             || decision.Readiness.To == ConversationReadinessLevel.R2_DirectionKnown)
            && decision.Strategy == ConversationBehaviorStrategy.DirectAnswer
            && !(signals.HasFactualQuestion && signals.HasCompleteQuestion))
        {
            next = next with
            {
                Strategy = ConversationBehaviorStrategy.SuggestAndClarify
            };
            reasonCodes.Add("behavior_guard_suggest_and_clarify_default");
        }

        if (requiresAcknowledgeAndGuide)
        {
            next = next with
            {
                Strategy = ConversationBehaviorStrategy.AcknowledgeAndGuide
            };
            reasonCodes.Add("behavior_guard_acknowledge_and_guide");
        }

        if (next.ModeCandidate == ConversationMode.Financial
            && next.Readiness.To < ConversationReadinessLevel.R2_DirectionKnown)
        {
            next = next with
            {
                ModeCandidate = ConversationMode.Conversation,
                Strategy = requiresAcknowledgeAndGuide
                    ? ConversationBehaviorStrategy.AcknowledgeAndGuide
                    : ConversationBehaviorStrategy.SuggestAndClarify
            };
            reasonCodes.Add("behavior_guard_financial_requires_guided_validation");
        }

        if ((state.LoopGuards?.SameClarificationIntentCount ?? 0) >= 2
            && next.Strategy is ConversationBehaviorStrategy.ClarifyOnly or ConversationBehaviorStrategy.SuggestAndClarify)
        {
            next = next with
            {
                Strategy = ConversationBehaviorStrategy.GeneralGuidance,
                ClarificationQuestion = null,
                SuggestedOptions = next.SuggestedOptions.Count > 0
                    ? next.SuggestedOptions
                    : ["Use a reasonable default", "Show concrete examples"]
            };
            reasonCodes.Add("behavior_guard_loop_break_to_guidance");
        }

        return next with
        {
            ReasonCodes = reasonCodes.ToArray()
        };
    }

    private static bool ShouldRouteToModeHandler(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision)
    {
        if (decision.ModeCandidate == ConversationMode.Conversation)
        {
            return false;
        }

        if (decision.Readiness.To < ConversationReadinessLevel.R2_DirectionKnown)
        {
            return false;
        }

        if (decision.ModeCandidate == ConversationMode.Financial
            && decision.Strategy is ConversationBehaviorStrategy.ConfirmAndTransition or ConversationBehaviorStrategy.FinancialPlaceholderTransition)
        {
            return true;
        }

        if (decision.ModeCandidate == ConversationMode.Exploration)
        {
            if (explorationSubtypeDecision?.Subtype == ExplorationSubtype.Open)
            {
                return decision.Readiness.To >= ConversationReadinessLevel.R2_DirectionKnown;
            }

            return decision.Readiness.To >= ConversationReadinessLevel.R2_DirectionKnown;
        }

        return decision.Readiness.To >= ConversationReadinessLevel.R2_DirectionKnown;
    }

    private static ResponseCompositionRequest BuildDirectModeCompositionRequest(
        string userMessage,
        ConversationTurnStrategyDecision decision,
        ConversationStateSnapshot state)
    {
        var responseType = decision.Strategy switch
        {
            ConversationBehaviorStrategy.DirectAnswer => ResponseCompositionType.Direct,
            ConversationBehaviorStrategy.SuggestAndClarify => ResponseCompositionType.Suggest,
            ConversationBehaviorStrategy.ClarifyOnly => ResponseCompositionType.Clarify,
            ConversationBehaviorStrategy.AcknowledgeAndGuide => ResponseCompositionType.Suggest,
            _ => ResponseCompositionType.Fallback
        };

        var tone = decision.Strategy == ConversationBehaviorStrategy.AcknowledgeAndGuide
            ? ResponseToneDirective.Supportive
            : decision.Strategy == ConversationBehaviorStrategy.DirectAnswer
                ? ResponseToneDirective.Concise
                : ResponseToneDirective.Neutral;

        return new ResponseCompositionRequest(
            ResponseType: responseType,
            ToneDirective: tone,
            Strategy: decision.Strategy,
            Mode: decision.ModeCandidate == ConversationMode.GeneralKnowledge
                ? ConversationMode.GeneralKnowledge
                : ConversationMode.Conversation,
            ReadinessLevel: decision.Readiness.To,
            UserMessage: userMessage,
            GroundedData: new GroundedDataEnvelope([], [], []),
            Constraints: state.Constraints,
            MissingConstraints: state.MissingConstraints ?? [],
            MaxLengthHint: 500,
            ClarificationQuestion: decision.ClarificationQuestion,
            SuggestedOptions: decision.SuggestedOptions);
    }

    private static string InferIntent(
        ConversationTurnStrategyDecision decision,
        ConversationSignals signals)
    {
        if (decision.ModeCandidate == ConversationMode.Exploration)
        {
            return "exploration";
        }

        if (signals.HasFinancialSignal)
        {
            return "financial";
        }

        if (decision.ModeCandidate == ConversationMode.GeneralKnowledge)
        {
            return "general_knowledge";
        }

        return "conversation";
    }

    private static IReadOnlyList<string> DetermineMissingConstraints(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision)
    {
        if (explorationSubtypeDecision?.MissingConstraints.Count > 0)
        {
            return explorationSubtypeDecision.MissingConstraints;
        }

        if (!string.IsNullOrWhiteSpace(decision.ClarificationQuestion))
        {
            return ["clarification_needed"];
        }

        return [];
    }

    private static string ResolveTransitionIntent(
        ConversationTurnStrategyDecision decision,
        ConversationStateSnapshot state,
        ConversationSignals signals)
    {
        if (decision.FollowUpBindingType == FollowUpBindingType.NewTopic)
        {
            return ConversationTransitionIntents.TopicSwitch;
        }

        if (decision.FollowUpBindingType == FollowUpBindingType.NewBranch)
        {
            return ConversationTransitionIntents.NewBranch;
        }

        if (decision.FollowUpBindingType == FollowUpBindingType.Refine)
        {
            return ConversationTransitionIntents.RefineCurrentBranch;
        }

        return decision.Strategy switch
        {
            ConversationBehaviorStrategy.ToolReadyHandoff => ConversationTransitionIntents.ToolReadyHandoff,
            ConversationBehaviorStrategy.ConfirmAndTransition => ConversationTransitionIntents.ConfirmAndTransition,
            ConversationBehaviorStrategy.FinancialPlaceholderTransition => ConversationTransitionIntents.FinancialTransition,
            ConversationBehaviorStrategy.RefinePriorResultSet => ConversationTransitionIntents.RefinePriorResults,
            _ when decision.ModeCandidate == ConversationMode.Conversation
                   && signals.HasFinancialSignal
                   && decision.Strategy is ConversationBehaviorStrategy.AcknowledgeAndGuide
                       or ConversationBehaviorStrategy.SuggestAndClarify
                => ConversationTransitionIntents.FinancialValidationPending,
            _ when !string.IsNullOrWhiteSpace(state.TransitionIntent)
                => state.TransitionIntent!,
            _ => ConversationTransitionIntents.DirectMode
        };
    }

    private static ClarificationTurnRewrite ResolveClarificationTurnRewrite(
        UserChatRequest request,
        ConversationStateSnapshot state)
    {
        var pending = state.PendingClarification;
        if (pending is null
            || pending.Slot == ClarificationSlot.None
            || string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return new ClarificationTurnRewrite(
                Request: request,
                State: state,
                SuppressResultContextBinding: false,
                ReasonCodes: []);
        }

        if (pending.Slot == ClarificationSlot.ExplorationLocation
            && TryResolveLocationSlotFill(request.UserMessage, out var resolvedArea, out var locationPhrase))
        {
            var stitchedMessage = BuildStructuredContinuationMessage(
                state,
                pending,
                placeTypeOverride: pending.KnownPlaceTypes,
                areaPhraseOverride: locationPhrase);
            if (string.IsNullOrWhiteSpace(stitchedMessage))
            {
                stitchedMessage = request.UserMessage.Trim();
            }

            var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase)
            {
                [ConversationConstraintKeys.ExplorationArea] = resolvedArea
            };

            if (!string.IsNullOrWhiteSpace(pending.KnownPlaceTypes))
            {
                constraints[ConversationConstraintKeys.ExplorationPlaceTypes] = pending.KnownPlaceTypes!;
            }

            if (!string.IsNullOrWhiteSpace(pending.KnownTime))
            {
                constraints[ConversationConstraintKeys.ExplorationTime] = pending.KnownTime!;
            }

            return new ClarificationTurnRewrite(
                Request: request with
                {
                    UserMessage = stitchedMessage
                },
                State: state with
                {
                    Constraints = constraints,
                    PendingClarification = null
                },
                SuppressResultContextBinding: true,
                ReasonCodes: ["clarification_slot_fill_location_resolved", "clarification_slot_fill_stitched"]);
        }

        if (pending.Slot == ClarificationSlot.ExplorationPlaceType)
        {
            var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(request.UserMessage);
            if (extraction.PlaceTypeHints.Count > 0)
            {
                var resolvedPlaceTypes = string.Join('|', extraction.PlaceTypeHints);
                var stitchedMessage = BuildStructuredContinuationMessage(
                    state,
                    pending,
                    placeTypeOverride: resolvedPlaceTypes,
                    areaPhraseOverride: null);
                if (string.IsNullOrWhiteSpace(stitchedMessage))
                {
                    stitchedMessage = request.UserMessage.Trim();
                }

                var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase)
                {
                    [ConversationConstraintKeys.ExplorationPlaceTypes] = resolvedPlaceTypes
                };

                if (!string.IsNullOrWhiteSpace(pending.KnownArea))
                {
                    constraints[ConversationConstraintKeys.ExplorationArea] = pending.KnownArea!;
                }

                if (!string.IsNullOrWhiteSpace(pending.KnownTime))
                {
                    constraints[ConversationConstraintKeys.ExplorationTime] = pending.KnownTime!;
                }

                return new ClarificationTurnRewrite(
                    Request: request with
                    {
                        UserMessage = stitchedMessage
                    },
                    State: state with
                    {
                        Constraints = constraints,
                        PendingClarification = null
                    },
                    SuppressResultContextBinding: true,
                    ReasonCodes: ["clarification_slot_fill_place_type_resolved", "clarification_slot_fill_stitched"]);
            }
        }

        if (pending.Slot == ClarificationSlot.FinancialFocus
            && !string.IsNullOrWhiteSpace(ConversationPolicyHelpers.ResolveFinancialFocus(request.UserMessage.Trim().ToLowerInvariant())))
        {
            return new ClarificationTurnRewrite(
                Request: request,
                State: state with
                {
                    PendingClarification = null
                },
                SuppressResultContextBinding: false,
                ReasonCodes: ["clarification_slot_fill_financial_focus_resolved"]);
        }

        return new ClarificationTurnRewrite(
            Request: request,
            State: state,
            SuppressResultContextBinding: false,
            ReasonCodes: []);
    }

    private static PendingClarificationState? ResolvePendingClarificationState(
        ConversationTurnStrategyDecision decision,
        ExplorationSubtypeDecision? explorationSubtypeDecision,
        ConversationStateSnapshot effectiveState,
        IReadOnlyDictionary<string, string> clientMetadata)
    {
        var isClarifying = decision.Strategy is ConversationBehaviorStrategy.SuggestAndClarify or ConversationBehaviorStrategy.ClarifyOnly;
        if (!isClarifying)
        {
            return null;
        }

        ClarificationSlot slot = ClarificationSlot.None;
        string? promptIntent = null;
        var knownPlaceTypes = ReadConstraintValue(effectiveState, ConversationConstraintKeys.ExplorationPlaceTypes);
        var knownArea = ReadConstraintValue(effectiveState, ConversationConstraintKeys.ExplorationArea);
        var knownTime = ReadConstraintValue(effectiveState, ConversationConstraintKeys.ExplorationTime);

        if (decision.ModeCandidate == ConversationMode.Exploration)
        {
            var missing = (explorationSubtypeDecision?.MissingConstraints ?? [])
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToArray();

            if (decision.ReasonCodes.Contains("behavior_guard_near_me_requires_location_evidence", StringComparer.OrdinalIgnoreCase)
                || missing.Contains("location_or_area", StringComparer.OrdinalIgnoreCase)
                || missing.Contains("area_or_location", StringComparer.OrdinalIgnoreCase))
            {
                slot = ClarificationSlot.ExplorationLocation;
                promptIntent = ResolveLocationPromptIntent(clientMetadata);
            }
            else if (missing.Contains("place_type", StringComparer.OrdinalIgnoreCase))
            {
                slot = ClarificationSlot.ExplorationPlaceType;
                promptIntent = "exploration_missing_place_type";
            }
        }
        else if (decision.ModeCandidate == ConversationMode.Conversation
                 && decision.ReasonCodes.Contains("financial_activation_requires_validated_thread", StringComparer.OrdinalIgnoreCase))
        {
            slot = ClarificationSlot.FinancialFocus;
            promptIntent = "financial_focus_validation";
        }

        if (slot == ClarificationSlot.None)
        {
            return null;
        }

        return new PendingClarificationState(
            Slot: slot,
            PromptIntent: promptIntent,
            KnownPlaceTypes: knownPlaceTypes,
            KnownArea: knownArea,
            KnownTime: knownTime,
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string ResolveLocationPromptIntent(IReadOnlyDictionary<string, string> metadata)
    {
        if (TryGetMetadataValue(metadata, CompanionLocationMetadataKeys.PermissionState, out var permissionState))
        {
            return permissionState switch
            {
                "granted" => "location_missing_fix",
                "denied_open_settings" or "unavailable" => "location_permission_denied_or_unavailable",
                "denied_can_ask_again" or "unknown" => "location_permission_prompt",
                _ => "location_missing"
            };
        }

        return "location_missing";
    }

    private static string? ReadConstraintValue(ConversationStateSnapshot state, string key)
    {
        return state.Constraints.TryGetValue(key, out var value)
               && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static string BuildStructuredContinuationMessage(
        ConversationStateSnapshot state,
        PendingClarificationState pending,
        string? placeTypeOverride,
        string? areaPhraseOverride)
    {
        var placeTypesRaw = string.IsNullOrWhiteSpace(placeTypeOverride)
            ? pending.KnownPlaceTypes ?? ReadConstraintValue(state, ConversationConstraintKeys.ExplorationPlaceTypes)
            : placeTypeOverride;
        var areaRaw = pending.KnownArea ?? ReadConstraintValue(state, ConversationConstraintKeys.ExplorationArea);
        var timeRaw = pending.KnownTime ?? ReadConstraintValue(state, ConversationConstraintKeys.ExplorationTime);

        var placePhrase = ResolvePlacePhrase(placeTypesRaw);
        var locationPhrase = areaPhraseOverride ?? ResolveAreaPhrase(areaRaw);
        var timePhrase = ResolveTimePhrase(timeRaw);

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(placePhrase))
        {
            parts.Add(placePhrase!);
        }

        if (!string.IsNullOrWhiteSpace(locationPhrase))
        {
            parts.Add(locationPhrase!);
        }

        if (!string.IsNullOrWhiteSpace(timePhrase))
        {
            parts.Add(timePhrase!);
        }

        return string.Join(' ', parts).Trim();
    }

    private static bool TryResolveLocationSlotFill(
        string userMessage,
        out string resolvedAreaConstraint,
        out string locationPhrase)
    {
        resolvedAreaConstraint = string.Empty;
        locationPhrase = string.Empty;
        var trimmed = userMessage.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (CompanionLocationGroundingParser.RequiresCurrentLocation(trimmed))
        {
            resolvedAreaConstraint = "near_me";
            locationPhrase = "near me";
            return true;
        }

        var candidateArea = trimmed;
        if (candidateArea.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
        {
            candidateArea = candidateArea[3..].Trim();
        }

        if (!CompanionLocationGroundingParser.IsValidAreaHint(candidateArea))
        {
            return false;
        }

        resolvedAreaConstraint = candidateArea;
        locationPhrase = $"in {candidateArea}";
        return true;
    }

    private static string? ResolvePlacePhrase(string? placeTypesRaw)
    {
        if (string.IsNullOrWhiteSpace(placeTypesRaw))
        {
            return null;
        }

        var firstType = placeTypesRaw
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstType))
        {
            return null;
        }

        return firstType.Trim().ToLowerInvariant() switch
        {
            "cafe" => "coffee shops",
            "restaurant" => "restaurants",
            "store" => "shops",
            "supermarket" => "supermarkets",
            "park" => "parks",
            "movie_theater" => "cinemas",
            "performing_arts_theater" => "theatres",
            "pharmacy" => "pharmacies",
            "gas_station" => "petrol stations",
            "gym" => "gyms",
            "bar" => "pubs",
            _ => firstType.Replace('_', ' ')
        };
    }

    private static string? ResolveAreaPhrase(string? areaRaw)
    {
        if (string.IsNullOrWhiteSpace(areaRaw))
        {
            return null;
        }

        var normalized = areaRaw.Trim();
        return string.Equals(normalized, "near_me", StringComparison.OrdinalIgnoreCase)
            ? "near me"
            : $"in {normalized}";
    }

    private static string? ResolveTimePhrase(string? timeRaw)
    {
        if (string.IsNullOrWhiteSpace(timeRaw))
        {
            return null;
        }

        var firstTimeToken = timeRaw
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstTimeToken))
        {
            return null;
        }

        return firstTimeToken.Trim().ToLowerInvariant() switch
        {
            "open_now" => "open now",
            "tonight" => "tonight",
            "weekend" => "this weekend",
            "today" => "today",
            _ => firstTimeToken.Replace('_', ' ')
        };
    }

    private static bool TryGetMetadataValue(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out string value)
    {
        value = string.Empty;
        if (metadata.TryGetValue(key, out var exactValue)
            && !string.IsNullOrWhiteSpace(exactValue))
        {
            value = exactValue.Trim().ToLowerInvariant();
            return true;
        }

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                value = pair.Value.Trim().ToLowerInvariant();
                return true;
            }
        }

        return false;
    }

    private sealed record ClarificationTurnRewrite(
        UserChatRequest Request,
        ConversationStateSnapshot State,
        bool SuppressResultContextBinding,
        IReadOnlyList<string> ReasonCodes);

    private static ConversationLoopGuards UpdateLoopGuards(
        ConversationLoopGuards? loopGuards,
        ConversationTurnStrategyDecision decision)
    {
        var current = loopGuards ?? new ConversationLoopGuards();
        var clarificationFingerprint = string.IsNullOrWhiteSpace(decision.ClarificationQuestion)
            ? null
            : decision.ClarificationQuestion.Trim().ToLowerInvariant();

        var sameClarificationCount = decision.Strategy is ConversationBehaviorStrategy.SuggestAndClarify or ConversationBehaviorStrategy.ClarifyOnly
            ? string.Equals(current.LastClarificationFingerprint, clarificationFingerprint, StringComparison.Ordinal)
                ? current.SameClarificationIntentCount + 1
                : 1
            : 0;

        return current with
        {
            SameClarificationIntentCount = sameClarificationCount,
            StrategicQuestionCountThisTurn = string.IsNullOrWhiteSpace(decision.ClarificationQuestion) ? 0 : 1,
            ConsecutiveNoProgressTurns = decision.Readiness.To <= ConversationReadinessLevel.R2_DirectionKnown
                ? current.ConsecutiveNoProgressTurns + 1
                : 0,
            LastClarificationFingerprint = clarificationFingerprint
        };
    }
}
