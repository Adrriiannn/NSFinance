namespace NSFinance.Api.Modules.AI.Services;

public interface IConversationBehaviorEngine
{
    Task<ConversationBehaviorResult> EvaluateAsync(
        ConversationBehaviorRequest request,
        CancellationToken cancellationToken);
}

public sealed class ConversationBehaviorEngine(
    IConversationDecisionEngine decisionEngine,
    IReadinessTransitionPolicy readinessTransitionPolicy,
    IFollowUpBindingPolicy followUpBindingPolicy,
    IContradictionResolutionPolicy contradictionResolutionPolicy,
    IChatTelemetry telemetry) : IConversationBehaviorEngine
{
    public async Task<ConversationBehaviorResult> EvaluateAsync(
        ConversationBehaviorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resultContextReadResult = request.ResultContextReadResult
                                      ?? new ResultContextReadResult(
                                          ActiveResultContext: request.ResultContext,
                                          BindingClassification: ResultContextBindingClassification.None,
                                          UsedClientResultSetId: false,
                                          ExpiredBindingCleared: false,
                                          ReasonCodes: []);

        var binding = followUpBindingPolicy.Determine(
            request.Request,
            request.EffectiveState,
            resultContextReadResult);
        var contradictionResolved = contradictionResolutionPolicy.Apply(
            request.EffectiveState,
            request.Request.UserMessage,
            binding.BindingType);

        var effectiveState = contradictionResolved.State;
        var rawDecision = await decisionEngine.EvaluateAsync(
            request with
            {
                EffectiveState = effectiveState
            },
            cancellationToken);

        var signals = ConversationSignalAnalyzer.Analyze(request.Request.UserMessage);
        var normalizedDecision = EnforceBehaviorRules(rawDecision, signals, effectiveState);
        ExplorationSubtypeDecision? explorationSubtypeDecision = null;
        if (normalizedDecision.ModeCandidate == ConversationMode.Exploration)
        {
            explorationSubtypeDecision = await decisionEngine.DetermineExplorationSubtypeAsync(
                new ConversationModeRequest(
                    Request: request.Request,
                    ContextMessages: request.ContextMessages,
                    ContextSummary: request.ContextSummary,
                    State: effectiveState,
                    ResultContext: request.ResultContext,
                    StrategyDecision: normalizedDecision,
                    ExplorationSubtypeDecision: null,
                    ClientMetadata: request.ClientMetadata),
                cancellationToken);
        }

        if (normalizedDecision.ModeCandidate == ConversationMode.Exploration
            && explorationSubtypeDecision?.Subtype == ExplorationSubtype.Structured
            && CompanionLocationGroundingParser.RequiresCurrentLocation(request.Request.UserMessage))
        {
            var grounding = CompanionLocationGroundingParser.Parse(request.ClientMetadata, effectiveState);
            if (!grounding.HasCoordinates && !grounding.HasTypedArea)
            {
                normalizedDecision = normalizedDecision with
                {
                    Strategy = ConversationBehaviorStrategy.SuggestAndClarify,
                    Readiness = normalizedDecision.Readiness with
                    {
                        To = ConversationReadinessLevel.R3_StructuredIncomplete
                    },
                    ToolExecutionPermission = ToolExecutionPermission.Forbidden,
                    ClarificationQuestion = normalizedDecision.ClarificationQuestion
                                            ?? "I can search once you share an area or allow location access.",
                    SuggestedOptions = normalizedDecision.SuggestedOptions.Count > 0
                        ? normalizedDecision.SuggestedOptions
                        : ["Share an area", "Enable location", "Keep it exploratory"],
                    ReasonCodes = normalizedDecision.ReasonCodes
                        .Concat(["behavior_guard_near_me_requires_location_evidence"])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }
        }

        var guardedReadiness = readinessTransitionPolicy.Apply(
            effectiveState,
            normalizedDecision,
            explorationSubtypeDecision);
        var finalDecision = normalizedDecision with
        {
            Readiness = guardedReadiness.Transition,
            ToolExecutionPermission = guardedReadiness.ToolExecutionPermission,
            FollowUpBindingType = binding.BindingType,
            ReasonCodes = normalizedDecision.ReasonCodes
                .Concat(binding.ReasonCodes)
                .Concat(contradictionResolved.ReasonCodes)
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
            SelectedEntityId = finalDecision.FollowUpBindingType == FollowUpBindingType.NewTopic
                ? null
                : binding.SelectedEntityId,
            TransitionIntent = ResolveTransitionIntent(finalDecision),
            Confidence = finalDecision.Confidence,
            NeedsFollowUp = finalDecision.Readiness.To <= ConversationReadinessLevel.R3_StructuredIncomplete,
            FollowUpBindingType = finalDecision.FollowUpBindingType,
            ResultContextRef = finalDecision.FollowUpBindingType == FollowUpBindingType.NewTopic
                ? null
                : resultContextReadResult.ActiveResultContext?.ResultSetId is Guid activeResultSetId
                ? new ConversationResultContextReference(
                    ActiveResultSetId: activeResultSetId,
                    BranchRootResultSetId: resultContextReadResult.ActiveResultContext!.BranchRootResultSetId,
                    ActiveUntilUtc: resultContextReadResult.ActiveResultContext.ActiveUntilUtc,
                    ExpiresUtc: resultContextReadResult.ActiveResultContext.ExpiresUtc)
                : effectiveState.ResultContextRef,
            LoopGuards = UpdateLoopGuards(effectiveState.LoopGuards, finalDecision)
        };

        var routeToModeHandler = ShouldRouteToModeHandler(finalDecision, explorationSubtypeDecision);
        var compositionRequest = routeToModeHandler
            ? null
            : BuildDirectModeCompositionRequest(request.Request.UserMessage, finalDecision, finalState);
        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (guardedReadiness.ToolExecutionPermission == ToolExecutionPermission.Forbidden)
        {
            warnings.Add("chat.tool.guard_blocked");
        }

        await telemetry.TrackAsync(
            "chat.turn.strategy_selected",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.Request.CorrelationId,
                ["strategy"] = finalDecision.Strategy.ToString(),
                ["modeCandidate"] = finalDecision.ModeCandidate.ToString(),
                ["readinessFrom"] = finalDecision.Readiness.From.ToString(),
                ["readinessTo"] = finalDecision.Readiness.To.ToString()
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
            ReasonCodes: finalDecision.ReasonCodes,
            Warnings: warnings.ToArray());
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

    private static string ResolveTransitionIntent(ConversationTurnStrategyDecision decision)
    {
        return decision.Strategy switch
        {
            ConversationBehaviorStrategy.ToolReadyHandoff => "tool_ready_handoff",
            ConversationBehaviorStrategy.ConfirmAndTransition => "confirm_and_transition",
            ConversationBehaviorStrategy.FinancialPlaceholderTransition => "financial_transition",
            ConversationBehaviorStrategy.RefinePriorResultSet => "refine_prior_results",
            _ => "direct_mode"
        };
    }

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
