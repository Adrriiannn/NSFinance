using System.Globalization;

namespace NSFinance.Api.Modules.AI.Services;

public interface IConversationDecisionEngine
{
    Task<ConversationDecisionEvaluationResult> EvaluateAsync(
        ConversationBehaviorRequest request,
        ConversationModelSelectionPlan modelSelection,
        CancellationToken cancellationToken);

    Task<ExplorationSubtypeEvaluationResult> DetermineExplorationSubtypeAsync(
        ConversationModeRequest request,
        ConversationModelSelectionPlan modelSelection,
        CancellationToken cancellationToken);
}

public sealed class ConversationDecisionEngine(
    IConversationDecisionPromptBuilder decisionPromptBuilder,
    IExplorationSubtypePromptBuilder explorationSubtypePromptBuilder,
    IConversationDecisionParser decisionParser,
    IExplorationSubtypeDecisionParser explorationSubtypeDecisionParser,
    IDeterministicConversationDecisionBuilder deterministicDecisionBuilder,
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IChatTelemetry telemetry,
    ILogger<ConversationDecisionEngine> logger) : IConversationDecisionEngine
{
    public async Task<ConversationDecisionEvaluationResult> EvaluateAsync(
        ConversationBehaviorRequest request,
        ConversationModelSelectionPlan modelSelection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (modelSelection.SelectionKind == ConversationModelSelectionKind.Deterministic
            || modelSelection.ModelClass is null)
        {
            var deterministicDecision = deterministicDecisionBuilder.BuildPrimaryDecision(request, modelSelection);

            await TrackSelectionAsync(
                request.Request.CorrelationId,
                "conversation_behavior_primary",
                modelSelection,
                route: null,
                usedModelInvocation: false,
                cancellationToken);

            return new ConversationDecisionEvaluationResult(
                Decision: deterministicDecision,
                ModelSelection: modelSelection,
                Route: null,
                UsedModelInvocation: false);
        }

        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            modelSelection.ModelClass.Value,
            complexityHint: request.EffectiveState.ReadinessLevel.ToString());
        var prompt = decisionPromptBuilder.BuildPrompt(
            new ConversationDecisionPromptInput(
                ChatRequest: request.Request,
                ContextMessages: request.ContextMessages,
                ContextSummary: request.ContextSummary,
                State: request.EffectiveState,
                ResultContext: request.ResultContext,
                ClientMetadata: request.ClientMetadata,
                FailureHistory: request.FailureHistory));

        await TrackSelectionAsync(
            route,
            request.Request.CorrelationId,
            "conversation_behavior_primary",
            modelSelection,
            usedModelInvocation: true,
            cancellationToken);
        var response = await aiClient.SendAsync(
            AIRequest.Create(
                taskType: AITaskType.ConversationDecision,
                preferredModelClass: modelSelection.ModelClass.Value,
                messages: prompt.Messages,
                correlationId: request.Request.CorrelationId,
                systemInstructions: prompt.SystemInstructions,
                structuredOutputSchemaName: prompt.StructuredSchemaName,
                temperature: 0.1d,
                maxOutputTokens: 500,
                metadata: request.Request.Metadata),
            route,
            cancellationToken);

        if (decisionParser.TryParse(
                response,
                route,
                request.EffectiveState,
                out var parsedDecision,
                out var reasonCodes,
                out var failureReason)
            && parsedDecision is not null)
        {
            return new ConversationDecisionEvaluationResult(
                Decision: parsedDecision with
                {
                    ReasonCodes = CombineReasonCodes(
                        parsedDecision.ReasonCodes,
                        reasonCodes,
                        modelSelection.ReasonCodes)
                },
                ModelSelection: modelSelection,
                Route: route,
                UsedModelInvocation: true);
        }

        logger.LogWarning(
            "Conversation decision parse fallback correlationId={CorrelationId} failureReason={FailureReason} selectionKind={SelectionKind} selectionReason={SelectionReason}",
            request.Request.CorrelationId,
            failureReason ?? "unknown",
            modelSelection.SelectionKind,
            modelSelection.SelectionReason);
        await telemetry.TrackAsync(
            "chat.response.fallback",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.Request.CorrelationId,
                ["failureReason"] = failureReason,
                ["stage"] = "conversation_decision",
                ["selectionKind"] = modelSelection.SelectionKind.ToString(),
                ["selectionReason"] = modelSelection.SelectionReason
            },
            cancellationToken);

        var baseFallbackDecision = BuildRecoveryFallbackDecision(request);
        var fallbackDecision = baseFallbackDecision with
        {
            ReasonCodes = CombineReasonCodes(
                baseFallbackDecision.ReasonCodes,
                modelSelection.ReasonCodes)
        };

        return new ConversationDecisionEvaluationResult(
            Decision: fallbackDecision,
            ModelSelection: modelSelection,
            Route: route,
            UsedModelInvocation: true);
    }

    public async Task<ExplorationSubtypeEvaluationResult> DetermineExplorationSubtypeAsync(
        ConversationModeRequest request,
        ConversationModelSelectionPlan modelSelection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (modelSelection.SelectionKind == ConversationModelSelectionKind.Deterministic
            || modelSelection.ModelClass is null)
        {
            var baseDecision = BuildFallbackExplorationSubtypeDecision(request.Request.UserMessage);
            var deterministicDecision = baseDecision with
            {
                ReasonCodes = CombineReasonCodes(
                    baseDecision.ReasonCodes,
                    modelSelection.ReasonCodes)
            };

            await TrackSelectionAsync(
                request.Request.CorrelationId,
                "exploration_subtype",
                modelSelection,
                route: null,
                usedModelInvocation: false,
                cancellationToken);

            return new ExplorationSubtypeEvaluationResult(
                Decision: deterministicDecision,
                ModelSelection: modelSelection,
                Route: null,
                UsedModelInvocation: false);
        }

        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            modelSelection.ModelClass.Value,
            complexityHint: "exploration_subtype");
        var prompt = explorationSubtypePromptBuilder.BuildPrompt(
            new ExplorationSubtypePromptInput(
                ChatRequest: request.Request,
                State: request.State,
                ResultContext: request.ResultContext,
                ClientMetadata: request.ClientMetadata));

        await TrackSelectionAsync(
            route,
            request.Request.CorrelationId,
            "exploration_subtype",
            modelSelection,
            usedModelInvocation: true,
            cancellationToken);
        var response = await aiClient.SendAsync(
            AIRequest.Create(
                taskType: AITaskType.ConversationDecision,
                preferredModelClass: modelSelection.ModelClass.Value,
                messages: prompt.Messages,
                correlationId: request.Request.CorrelationId,
                systemInstructions: prompt.SystemInstructions,
                structuredOutputSchemaName: prompt.StructuredSchemaName,
                temperature: 0.1d,
                maxOutputTokens: 250,
                metadata: request.Request.Metadata),
            route,
            cancellationToken);

        if (explorationSubtypeDecisionParser.TryParse(
                response,
                route,
                out var parsedDecision,
                out var reasonCodes,
                out var failureReason)
            && parsedDecision is not null)
        {
            return new ExplorationSubtypeEvaluationResult(
                Decision: parsedDecision with
                {
                    ReasonCodes = CombineReasonCodes(
                        parsedDecision.ReasonCodes,
                        reasonCodes,
                        modelSelection.ReasonCodes)
                },
                ModelSelection: modelSelection,
                Route: route,
                UsedModelInvocation: true);
        }

        logger.LogWarning(
            "Exploration subtype parse fallback correlationId={CorrelationId} failureReason={FailureReason} selectionKind={SelectionKind} selectionReason={SelectionReason}",
            request.Request.CorrelationId,
            failureReason ?? "unknown",
            modelSelection.SelectionKind,
            modelSelection.SelectionReason);

        var baseFallbackDecision = BuildFallbackExplorationSubtypeDecision(request.Request.UserMessage);
        var fallbackDecision = baseFallbackDecision with
        {
            ReasonCodes = CombineReasonCodes(
                baseFallbackDecision.ReasonCodes,
                modelSelection.ReasonCodes)
        };

        return new ExplorationSubtypeEvaluationResult(
            Decision: fallbackDecision,
            ModelSelection: modelSelection,
            Route: route,
            UsedModelInvocation: true);
    }

    private async Task TrackSelectionAsync(
        AIModelRoute route,
        string correlationId,
        string invocationStage,
        ConversationModelSelectionPlan modelSelection,
        bool usedModelInvocation,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "chat.model.invocation",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["taskType"] = route.TaskType.ToString(),
                ["modelClass"] = route.ModelClass.ToString(),
                ["model"] = route.Model,
                ["deployment"] = route.Deployment,
                ["invocationStage"] = invocationStage,
                ["routeReason"] = route.Reason,
                ["routeNotes"] = route.Notes.ToArray(),
                ["selectionKind"] = modelSelection.SelectionKind.ToString(),
                ["selectionReason"] = modelSelection.SelectionReason,
                ["escalationJustification"] = modelSelection.EscalationJustification,
                ["couldAvoidEscalation"] = modelSelection.CouldAvoidEscalation,
                ["selectionReasonCodes"] = modelSelection.ReasonCodes.ToArray(),
                ["usedModelInvocation"] = usedModelInvocation
            },
            cancellationToken);
    }

    private async Task TrackSelectionAsync(
        string correlationId,
        string invocationStage,
        ConversationModelSelectionPlan modelSelection,
        AIModelRoute? route,
        bool usedModelInvocation,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "chat.model.selection",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["invocationStage"] = invocationStage,
                ["selectionKind"] = modelSelection.SelectionKind.ToString(),
                ["selectionReason"] = modelSelection.SelectionReason,
                ["escalationJustification"] = modelSelection.EscalationJustification,
                ["couldAvoidEscalation"] = modelSelection.CouldAvoidEscalation,
                ["selectionReasonCodes"] = modelSelection.ReasonCodes.ToArray(),
                ["usedModelInvocation"] = usedModelInvocation,
                ["routeReason"] = route?.Reason,
                ["routeModelClass"] = route?.ModelClass.ToString(),
                ["routeModel"] = route?.Model,
                ["routeDeployment"] = route?.Deployment
            },
            cancellationToken);
    }

    private static IReadOnlyList<string> CombineReasonCodes(params IReadOnlyList<string>[] reasonCodeSets)
    {
        return reasonCodeSets
            .SelectMany(static codes => codes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ConversationTurnStrategyDecision BuildRecoveryFallbackDecision(ConversationBehaviorRequest request)
    {
        var signals = ConversationSignalAnalyzer.Analyze(request.Request.UserMessage);
        var currentReadiness = request.EffectiveState.ReadinessLevel;
        var suggestedOptions = Array.Empty<string>();

        if (signals.HasExplorationSignal)
        {
            var toolReady = signals.HasExplicitLocation && signals.HasConcretePlaceSignal;
            return new ConversationTurnStrategyDecision(
                Strategy: toolReady
                    ? ConversationBehaviorStrategy.ToolReadyHandoff
                    : ConversationBehaviorStrategy.SuggestAndClarify,
                ModeCandidate: ConversationMode.Exploration,
                Readiness: new ReadinessTransition(
                    From: currentReadiness,
                    To: toolReady
                        ? ConversationReadinessLevel.R4_ToolReady
                        : signals.HasConcretePlaceSignal
                            ? ConversationReadinessLevel.R3_StructuredIncomplete
                            : ConversationReadinessLevel.R2_DirectionKnown),
                Confidence: toolReady ? 0.86d : 0.71d,
                FollowUpBindingType: request.ResultContext is null
                    ? FollowUpBindingType.None
                    : FollowUpBindingType.Refine,
                ClarificationQuestion: toolReady
                    ? null
                    : "Do you want a concrete place search, or more exploratory suggestions first?",
                SuggestedOptions: toolReady ? [] : ["Concrete place search", "Exploratory ideas"],
                ToolExecutionPermission: toolReady
                    ? ToolExecutionPermission.EligibleIfGuardPasses
                    : ToolExecutionPermission.Forbidden,
                ReasonCodes:
                [
                    "fallback_decision_recovery",
                    "fallback_exploration_signal"
                ]);
        }

        if (signals.HasFinancialSignal && !signals.HasCompleteQuestion)
        {
            suggestedOptions = ["Review subscriptions", "Look at spending trends", "Set a clearer goal"];
            return new ConversationTurnStrategyDecision(
                Strategy: signals.HasEmotionalFraming
                    ? ConversationBehaviorStrategy.AcknowledgeAndGuide
                    : ConversationBehaviorStrategy.SuggestAndClarify,
                ModeCandidate: ConversationMode.Conversation,
                Readiness: new ReadinessTransition(
                    From: currentReadiness,
                    To: ConversationReadinessLevel.R1_Vague),
                Confidence: 0.78d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: "Do you want to focus on subscriptions, overall spending, or a specific budget concern?",
                SuggestedOptions: suggestedOptions,
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes:
                [
                    "fallback_decision_recovery",
                    "fallback_financial_validation_required"
                ]);
        }

        if (signals.HasFactualQuestion && signals.HasCompleteQuestion)
        {
            return new ConversationTurnStrategyDecision(
                Strategy: ConversationBehaviorStrategy.DirectAnswer,
                ModeCandidate: ConversationMode.GeneralKnowledge,
                Readiness: new ReadinessTransition(
                    From: currentReadiness,
                    To: ConversationReadinessLevel.R1_Vague),
                Confidence: 0.72d,
                FollowUpBindingType: FollowUpBindingType.None,
                ClarificationQuestion: null,
                SuggestedOptions: [],
                ToolExecutionPermission: ToolExecutionPermission.Forbidden,
                ReasonCodes:
                [
                    "fallback_decision_recovery",
                    "fallback_factual_complete"
                ]);
        }

        return new ConversationTurnStrategyDecision(
            Strategy: signals.HasEmotionalFraming || signals.HasSubjectiveLanguage
                ? ConversationBehaviorStrategy.AcknowledgeAndGuide
                : ConversationBehaviorStrategy.SuggestAndClarify,
            ModeCandidate: ConversationMode.Conversation,
            Readiness: new ReadinessTransition(
                From: currentReadiness,
                To: ConversationReadinessLevel.R1_Vague),
            Confidence: 0.55d,
            FollowUpBindingType: FollowUpBindingType.None,
            ClarificationQuestion: "What would feel most useful to focus on first?",
            SuggestedOptions: ["Clarify the goal", "Share a constraint", "Ask for general guidance"],
            ToolExecutionPermission: ToolExecutionPermission.Forbidden,
            ReasonCodes:
            [
                "fallback_decision_recovery",
                "fallback_general_guidance"
            ]);
    }

    private static ExplorationSubtypeDecision BuildFallbackExplorationSubtypeDecision(string userMessage)
    {
        return ConversationPolicyHelpers.BuildFallbackExplorationSubtypeDecision(userMessage);
    }
}

public interface IResponseComposer
{
    Task<ResponseCompositionResult> ComposeAsync(
        ResponseCompositionRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class ResponseComposer(
    IResponseCompositionPromptBuilder promptBuilder,
    IUserChatResponseParser userChatResponseParser,
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IChatTelemetry telemetry,
    ILogger<ResponseComposer> logger) : IResponseComposer
{
    public async Task<ResponseCompositionResult> ComposeAsync(
        ResponseCompositionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldUseDeterministicTemplate(request))
        {
            var deterministic = BuildDeterministicResponse(request);
            await TrackResponseSelectionAsync(
                correlationId,
                request.ResponseType,
                ConversationModelSelectionKind.Deterministic,
                deterministic.SelectionReason,
                usedModelInvocation: false,
                deterministic.FallbackUsed,
                deterministic.RecoveryReason,
                deterministic.Warnings,
                cancellationToken);
            return deterministic;
        }

        var route = modelRouter.Resolve(AITaskType.ResponseComposition, AIModelClass.Fast, request.ResponseType.ToString());
        var prompt = promptBuilder.BuildPrompt(new ResponseCompositionPromptInput(request, correlationId));
        await telemetry.TrackAsync(
            "chat.model.invocation",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["taskType"] = route.TaskType.ToString(),
                ["modelClass"] = route.ModelClass.ToString(),
                ["model"] = route.Model,
                ["deployment"] = route.Deployment,
                ["invocationStage"] = "response_composition",
                ["routeReason"] = route.Reason,
                ["routeNotes"] = route.Notes.ToArray(),
                ["selectionKind"] = ConversationModelSelectionKind.Fast.ToString(),
                ["selectionReason"] = "response_composition_fast",
                ["usedModelInvocation"] = true
            },
            cancellationToken);

        AIResponse response;
        try
        {
            response = await aiClient.SendAsync(
                AIRequest.Create(
                    taskType: AITaskType.ResponseComposition,
                    preferredModelClass: AIModelClass.Fast,
                    messages: prompt.Messages,
                    correlationId: correlationId,
                    systemInstructions: prompt.SystemInstructions,
                    structuredOutputSchemaName: prompt.StructuredSchemaName,
                    temperature: 0.2d,
                    maxOutputTokens: Math.Clamp(request.MaxLengthHint, 120, 900)),
                route,
                cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Response composition model invocation cancelled by transport correlationId={CorrelationId} responseType={ResponseType}",
                correlationId,
                request.ResponseType);

            return await BuildModelInvocationFallbackAsync(
                request,
                correlationId,
                recoveryReason: "response_composition_transport_cancelled",
                warnings:
                [
                    "response_composition_transport_cancelled",
                    "response_composition_safe_fallback"
                ],
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Response composition model invocation failed correlationId={CorrelationId} responseType={ResponseType}",
                correlationId,
                request.ResponseType);

            return await BuildModelInvocationFallbackAsync(
                request,
                correlationId,
                recoveryReason: "response_composition_model_call_failed",
                warnings:
                [
                    "response_composition_model_call_failed",
                    "response_composition_safe_fallback"
                ],
                cancellationToken);
        }

        if (userChatResponseParser.TryParse(
                response,
                route,
                out var parsedResponse,
                out var parserReasonCodes,
                out var parseFailureReason)
            && parsedResponse.Succeeded)
        {
            var replyText = request.ResponseType == ResponseCompositionType.ResultSummary
                ? BuildCanonicalStructuredResultSummaryReply(request)
                : parsedResponse.ReplyText;
            var success = new ResponseCompositionResult(
                ReplyText: replyText,
                SuggestedStructuredStateUpdates: parsedResponse.SuggestedStructuredStateUpdates,
                ModelUsed: parsedResponse.ModelUsed,
                DeploymentUsed: route.Deployment,
                ReasoningClass: parsedResponse.ReasoningClass,
                UsedModelInvocation: true,
                UsedDeterministicPath: false,
                FallbackUsed: false,
                SelectionReason: "response_composition_fast",
                RecoveryReason: null,
                Warnings: parsedResponse.Warnings,
                FollowUpIntentHints: parsedResponse.FollowUpIntentHints);

            await TrackResponseSelectionAsync(
                correlationId,
                request.ResponseType,
                ConversationModelSelectionKind.Fast,
                success.SelectionReason,
                usedModelInvocation: true,
                success.FallbackUsed,
                success.RecoveryReason,
                parserReasonCodes,
                cancellationToken);

            return success;
        }

        logger.LogWarning(
            "Response composition safe fallback correlationId={CorrelationId} responseType={ResponseType} failureReason={FailureReason}",
            correlationId,
            request.ResponseType,
            parseFailureReason ?? "structured_parse_failed");
        await telemetry.TrackAsync(
            "chat.response.fallback",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["stage"] = "response_composer",
                ["responseType"] = request.ResponseType.ToString(),
                ["failureReason"] = parseFailureReason ?? "structured_parse_failed",
                ["parserReasonCodes"] = parserReasonCodes.ToArray()
            },
            cancellationToken);

        var recovered = BuildDeterministicResponse(
            request,
            selectionReason: "response_composition_safe_fallback",
            fallbackUsed: true,
            usedModelInvocation: true,
            recoveryReason: parseFailureReason ?? "structured_parse_failed",
            warnings:
            [
                "structured_parse_failed",
                "response_composition_safe_fallback"
            ]);

        await TrackResponseSelectionAsync(
            correlationId,
            request.ResponseType,
            ConversationModelSelectionKind.Deterministic,
            recovered.SelectionReason,
            usedModelInvocation: true,
            recovered.FallbackUsed,
            recovered.RecoveryReason,
            recovered.Warnings,
            cancellationToken);

        return recovered;
    }

    private async Task<ResponseCompositionResult> BuildModelInvocationFallbackAsync(
        ResponseCompositionRequest request,
        string correlationId,
        string recoveryReason,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "chat.response.fallback",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["stage"] = "response_composer_model_invocation",
                ["responseType"] = request.ResponseType.ToString(),
                ["failureReason"] = recoveryReason
            },
            cancellationToken);

        var recovered = BuildDeterministicResponse(
            request,
            selectionReason: "response_composition_safe_fallback",
            fallbackUsed: true,
            usedModelInvocation: true,
            recoveryReason: recoveryReason,
            warnings: warnings);

        await TrackResponseSelectionAsync(
            correlationId,
            request.ResponseType,
            ConversationModelSelectionKind.Deterministic,
            recovered.SelectionReason,
            usedModelInvocation: true,
            recovered.FallbackUsed,
            recovered.RecoveryReason,
            recovered.Warnings,
            cancellationToken);

        return recovered;
    }

    private async Task TrackResponseSelectionAsync(
        string correlationId,
        ResponseCompositionType responseType,
        ConversationModelSelectionKind selectionKind,
        string selectionReason,
        bool usedModelInvocation,
        bool fallbackUsed,
        string? recoveryReason,
        IReadOnlyList<string> reasonCodes,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "chat.response.selection",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["selectionKind"] = selectionKind.ToString(),
                ["selectionReason"] = selectionReason,
                ["usedModelInvocation"] = usedModelInvocation,
                ["fallbackUsed"] = fallbackUsed,
                ["recoveryReason"] = recoveryReason,
                ["responseType"] = responseType.ToString(),
                ["reasonCodes"] = reasonCodes.ToArray()
            },
            cancellationToken);
    }

    private static bool ShouldUseDeterministicTemplate(ResponseCompositionRequest request)
    {
        return request.ResponseType is ResponseCompositionType.Fallback or ResponseCompositionType.Placeholder
               || (request.ResponseType == ResponseCompositionType.Clarify && request.GroundedData.Entities.Count == 0);
    }

    private static ResponseCompositionResult BuildDeterministicResponse(
        ResponseCompositionRequest request,
        string selectionReason = "deterministic_template",
        bool fallbackUsed = false,
        bool usedModelInvocation = false,
        string? recoveryReason = null,
        IReadOnlyList<string>? warnings = null)
    {
        var missing = request.MissingConstraints.Count > 0
            ? $" I still need: {string.Join(", ", request.MissingConstraints)}."
            : string.Empty;
        var options = request.SuggestedOptions is { Count: > 0 }
            ? $" Options: {string.Join("; ", request.SuggestedOptions.Take(3))}."
            : string.Empty;

        var replyText = request.ResponseType switch
        {
            ResponseCompositionType.Placeholder => "I can help with that, but I need to guide the conversation a little further before taking action.",
            ResponseCompositionType.Fallback => "I can still help from here without taking action yet." + missing + options,
            ResponseCompositionType.Clarify => (request.ClarificationQuestion ?? "Could you clarify what you want to focus on?") + options,
            _ => "Here’s the next helpful step based on what you shared." + missing + options
        };

        if (request.ResponseType == ResponseCompositionType.ResultSummary)
        {
            replyText = BuildCanonicalStructuredResultSummaryReply(request);
        }

        replyText = replyText.Replace("Hereâ€™s", "Here's", StringComparison.Ordinal);

        return new ResponseCompositionResult(
            ReplyText: replyText.Trim(),
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ModelUsed: "deterministic-response-composer",
            DeploymentUsed: "deterministic-response-composer",
            ReasoningClass: AIModelClass.Fast,
            UsedModelInvocation: usedModelInvocation,
            UsedDeterministicPath: true,
            FallbackUsed: fallbackUsed,
            SelectionReason: selectionReason,
            RecoveryReason: recoveryReason,
            Warnings: warnings ?? [],
            FollowUpIntentHints: BuildDeterministicFollowUpHints(request));
    }

    private static IReadOnlyList<string> BuildDeterministicFollowUpHints(ResponseCompositionRequest request)
    {
        if (request.ResponseType == ResponseCompositionType.ResultSummary
            && request.GroundedData.Entities.Count > 0)
        {
            return ["refine_place_preferences", "compare_options"];
        }

        return request.MissingConstraints.Count > 0
            ? ["clarify_intent"]
            : [];
    }

    private static string BuildCanonicalStructuredResultSummaryReply(ResponseCompositionRequest request)
    {
        if (request.GroundedData.Entities.Count == 0)
        {
            var options = request.SuggestedOptions is { Count: > 0 }
                ? $" You can refine by {string.Join(", ", request.SuggestedOptions.Take(3)).ToLowerInvariant()}."
                : string.Empty;
            return "I found grounded results, but I need one more detail to shape a clean shortlist." + options;
        }

        var factLookup = request.GroundedData.SummaryFacts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.Label) && !string.IsNullOrWhiteSpace(fact.Value))
            .GroupBy(fact => fact.Label.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value.Trim(), StringComparer.OrdinalIgnoreCase);

        var ordered = request.GroundedData.Entities
            .Where(static entity => !string.IsNullOrWhiteSpace(entity.Label))
            .OrderBy(entity => entity.Rank <= 0 ? int.MaxValue : entity.Rank)
            .ThenBy(entity => entity.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length == 0)
        {
            return "I found grounded results, but I couldn't safely format the shortlist.";
        }

        var intro = ordered.Length == 1
            ? "Here is one grounded option:"
            : "Here are grounded options near you:";
        var lines = new List<string>(ordered.Length * 2 + 3)
        {
            intro
        };

        for (var index = 0; index < ordered.Length; index++)
        {
            var entity = ordered[index];
            lines.Add($"{index + 1}. {entity.Label}");
            if (factLookup.TryGetValue(entity.Label, out var fact) && !string.IsNullOrWhiteSpace(fact))
            {
                lines.Add($"   Details: {fact}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Would you like me to narrow these by distance, parking, or seating?");
        return string.Join(Environment.NewLine, lines);
    }
}

public static class ConversationSignalAnalyzer
{
    public static ConversationSignals Analyze(string? userMessage, TurnInterpretationV2? interpretation = null)
    {
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(userMessage);
        var interpretedPlaceTypes = interpretation?.PlacePlan.IncludeTypes ?? [];
        var interpretedExploration = interpretation?.IntentFamily is TurnInterpretationIntentFamily.PlaceDiscovery or TurnInterpretationIntentFamily.Mixed;
        var interpretedFinancial = interpretation?.IntentFamily == TurnInterpretationIntentFamily.FinancialGuidance;
        var interpretedLocation = interpretation?.LocationPlan.NearMeSemantic == true
                                 || !string.IsNullOrWhiteSpace(interpretation?.LocationPlan.ResolvedAreaHint)
                                 || !string.IsNullOrWhiteSpace(interpretation?.LocationPlan.ExplicitAreaText);
        var hasFinancialFocusSelection = ConversationPolicyHelpers.ResolveFinancialFocus(normalized) is not null
                                         && (ContainsAny(normalized, "review", "look at", "focus on", "show me", "help me", "yes", "please")
                                             || normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length <= 3);
        var hasFinancialSignal = interpretedFinancial
                                 || ContainsAny(normalized, "budget", "subscriptions", "spending", "spend", "afford", "expense", "save");
        var hasExplorationSignal = interpretedExploration
                                   || extraction.IsLocalDiscoveryCandidate
                                   || ContainsAny(normalized, "walk", "beach", "view", "quiet place", "nice place", "lighting", "late walk");
        var hasConcretePlaceSignal = extraction.PlaceTypeHints.Count > 0
                                     || interpretedPlaceTypes.Count > 0
                                     || interpretation?.PlacePlan.BrandOrEntityTerms.Count > 0
                                     || ContainsAny(normalized, "beach", "waterfront");
        var hasExplicitLocation = extraction.HasNearMeLanguage
                                  || extraction.HasExplicitLocality
                                  || interpretedLocation
                                  || extraction.TimeHints.Count > 0;
        var hasEmotionalFraming = ContainsAny(normalized, "i think", "i feel", "i'm worried", "i am worried", "stressed", "overwhelmed", "too high");
        var hasSubjectiveLanguage = ContainsAny(normalized, "nice", "quiet", "safe", "good", "better", "best", "feel");
        var hasCorrectionSignal = HasCorrectionSignal(normalized);
        var hasTopicSwitchSignal = ContainsAny(normalized, "new topic", "something else", "different question");
        var hasFactualQuestion = (normalized.StartsWith("what ", StringComparison.Ordinal)
                                  && !normalized.StartsWith("what about", StringComparison.Ordinal))
                                 || normalized.StartsWith("when ", StringComparison.Ordinal)
                                 || normalized.StartsWith("where ", StringComparison.Ordinal)
                                 || normalized.StartsWith("who ", StringComparison.Ordinal)
                                 || normalized.StartsWith("how ", StringComparison.Ordinal);
        var hasCompleteQuestion = normalized.EndsWith("?", StringComparison.Ordinal)
                                  || normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length >= 5;
        var hasExplicitConfirmation = normalized is "yes" or "yeah" or "yep" or "sure"
                                      || ContainsAny(normalized, "yes ", "go ahead", "please do", "let's do that", "sounds good", "do that");
        var hasAtmosphericExplorationIntent = ContainsAny(
            normalized,
            "quiet",
            "calm",
            "safe",
            "lighting",
            "late walk",
            "beach view",
            "view",
            "vibe",
            "atmosphere");
        var hasSafetyExplorationIntent = ContainsAny(normalized, "safe", "lighting", "well lit", "late walk", "night");
        var hasStructuredExplorationIntent = ConversationPolicyHelpers.HasStrongStructuredExplorationIntent(extraction, new ConversationSignals(
            HasEmotionalFraming: hasEmotionalFraming,
            HasSubjectiveLanguage: hasSubjectiveLanguage,
            HasCorrectionSignal: hasCorrectionSignal,
            HasTopicSwitchSignal: hasTopicSwitchSignal,
            HasFinancialSignal: hasFinancialSignal,
            HasExplorationSignal: hasExplorationSignal,
            HasFactualQuestion: hasFactualQuestion,
            HasCompleteQuestion: hasCompleteQuestion,
            HasConcretePlaceSignal: hasConcretePlaceSignal,
            HasExplicitLocation: hasExplicitLocation,
            HasExplicitConfirmation: hasExplicitConfirmation,
            HasFinancialFocusSelection: hasFinancialFocusSelection,
            HasAtmosphericExplorationIntent: hasAtmosphericExplorationIntent,
            HasSafetyExplorationIntent: hasSafetyExplorationIntent));
        var hasPronounFollowUpSignal = normalized.StartsWith("do they ", StringComparison.Ordinal)
                                       || normalized.StartsWith("are they ", StringComparison.Ordinal)
                                       || normalized.StartsWith("does it ", StringComparison.Ordinal)
                                       || normalized.StartsWith("is it ", StringComparison.Ordinal);
        var hasResultReferenceSignal = ContainsAny(normalized, "that one", "those", "them", "they", "the first", "the second", "this list", "that list")
                                       || hasPronounFollowUpSignal;
        var hasMutationRewriteSignal = HasMutationRewriteSignal(userMessage);
        var hasBranchingSignal = (hasMutationRewriteSignal && (extraction.HasExplicitLocality || extraction.HasNearMeLanguage))
                                 || ContainsAny(normalized, "another", "rather than")
                                 || normalized.StartsWith("try ", StringComparison.Ordinal);
        var hasComparisonSignal = ContainsAny(normalized, "compare", "versus", "vs", "against", "first list");

        return new ConversationSignals(
            HasEmotionalFraming: hasEmotionalFraming,
            HasSubjectiveLanguage: hasSubjectiveLanguage,
            HasCorrectionSignal: hasCorrectionSignal,
            HasTopicSwitchSignal: hasTopicSwitchSignal,
            HasFinancialSignal: hasFinancialSignal,
            HasExplorationSignal: hasExplorationSignal,
            HasFactualQuestion: hasFactualQuestion,
            HasCompleteQuestion: hasCompleteQuestion,
            HasConcretePlaceSignal: hasConcretePlaceSignal,
            HasExplicitLocation: hasExplicitLocation,
            HasExplicitConfirmation: hasExplicitConfirmation,
            HasFinancialFocusSelection: hasFinancialFocusSelection,
            HasAtmosphericExplorationIntent: hasAtmosphericExplorationIntent,
            HasSafetyExplorationIntent: hasSafetyExplorationIntent,
            HasStructuredExplorationIntent: hasStructuredExplorationIntent,
            HasResultReferenceSignal: hasResultReferenceSignal,
            HasBranchingSignal: hasBranchingSignal,
            HasComparisonSignal: hasComparisonSignal);
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }

    private static bool HasCorrectionSignal(string normalized)
    {
        return ContainsAny(
            normalized,
            "actually",
            "instead",
            "wrong",
            "forget",
            "change of plan",
            "change it",
            "scratch that")
               || normalized.Contains("not near me", StringComparison.Ordinal);
    }

    private static bool HasMutationRewriteSignal(string? userMessage)
    {
        var original = (userMessage ?? string.Empty).Trim();
        if (original.Length == 0)
        {
            return false;
        }

        var mutation = ConversationPolicyHelpers.ResolveMutationSegment(original).Trim();
        return mutation.Length > 0
               && !string.Equals(mutation, original, StringComparison.OrdinalIgnoreCase);
    }
}
