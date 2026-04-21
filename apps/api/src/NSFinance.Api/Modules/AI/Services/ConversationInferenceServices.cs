using System.Globalization;

namespace NSFinance.Api.Modules.AI.Services;

public interface IConversationDecisionEngine
{
    Task<ConversationTurnStrategyDecision> EvaluateAsync(
        ConversationBehaviorRequest request,
        CancellationToken cancellationToken);

    Task<ExplorationSubtypeDecision> DetermineExplorationSubtypeAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken);
}

public sealed class ConversationDecisionEngine(
    IConversationDecisionPromptBuilder decisionPromptBuilder,
    IExplorationSubtypePromptBuilder explorationSubtypePromptBuilder,
    IConversationDecisionParser decisionParser,
    IExplorationSubtypeDecisionParser explorationSubtypeDecisionParser,
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IChatTelemetry telemetry,
    ILogger<ConversationDecisionEngine> logger) : IConversationDecisionEngine
{
    public async Task<ConversationTurnStrategyDecision> EvaluateAsync(
        ConversationBehaviorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            AIModelClass.HeavyReasoning,
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

        await TrackInvocationAsync("chat.model.invocation", route, request.Request.CorrelationId, cancellationToken);
        var response = await aiClient.SendAsync(
            AIRequest.Create(
                taskType: AITaskType.ConversationDecision,
                preferredModelClass: AIModelClass.HeavyReasoning,
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
            return parsedDecision with
            {
                ReasonCodes = parsedDecision.ReasonCodes
                    .Concat(reasonCodes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        logger.LogWarning(
            "Conversation decision parse fallback correlationId={CorrelationId} failureReason={FailureReason}",
            request.Request.CorrelationId,
            failureReason ?? "unknown");
        await telemetry.TrackAsync(
            "chat.response.fallback",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.Request.CorrelationId,
                ["failureReason"] = failureReason,
                ["stage"] = "conversation_decision"
            },
            cancellationToken);

        return BuildFallbackDecision(request);
    }

    public async Task<ExplorationSubtypeDecision> DetermineExplorationSubtypeAsync(
        ConversationModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            AIModelClass.HeavyReasoning,
            complexityHint: "exploration_subtype");
        var prompt = explorationSubtypePromptBuilder.BuildPrompt(
            new ExplorationSubtypePromptInput(
                ChatRequest: request.Request,
                State: request.State,
                ResultContext: request.ResultContext,
                ClientMetadata: request.ClientMetadata));

        await TrackInvocationAsync("chat.model.invocation", route, request.Request.CorrelationId, cancellationToken);
        var response = await aiClient.SendAsync(
            AIRequest.Create(
                taskType: AITaskType.ConversationDecision,
                preferredModelClass: AIModelClass.HeavyReasoning,
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
            return parsedDecision with
            {
                ReasonCodes = parsedDecision.ReasonCodes
                    .Concat(reasonCodes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        logger.LogWarning(
            "Exploration subtype parse fallback correlationId={CorrelationId} failureReason={FailureReason}",
            request.Request.CorrelationId,
            failureReason ?? "unknown");
        return BuildFallbackExplorationSubtypeDecision(request.Request.UserMessage);
    }

    private async Task TrackInvocationAsync(
        string eventName,
        AIModelRoute route,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            eventName,
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["taskType"] = route.TaskType.ToString(),
                ["modelClass"] = route.ModelClass.ToString(),
                ["model"] = route.Model,
                ["deployment"] = route.Deployment
            },
            cancellationToken);
    }

    private static ConversationTurnStrategyDecision BuildFallbackDecision(ConversationBehaviorRequest request)
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
                    "fallback_decision",
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
                    "fallback_decision",
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
                    "fallback_decision",
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
                "fallback_decision",
                "fallback_general_guidance"
            ]);
    }

    private static ExplorationSubtypeDecision BuildFallbackExplorationSubtypeDecision(string userMessage)
    {
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var structured = normalized.Contains("near me", StringComparison.Ordinal)
                         || normalized.Contains("open now", StringComparison.Ordinal)
                         || normalized.Contains("restaurant", StringComparison.Ordinal)
                         || normalized.Contains("park", StringComparison.Ordinal)
                         || normalized.Contains("cafe", StringComparison.Ordinal)
                         || normalized.Contains("museum", StringComparison.Ordinal);

        if (structured)
        {
            return new ExplorationSubtypeDecision(
                Subtype: ExplorationSubtype.Structured,
                Confidence: 0.73d,
                ToolPathEligible: true,
                PrimaryWhy: "The request includes a concrete place/domain search pattern.",
                MissingConstraints: [],
                ReasonCodes: ["fallback_exploration_subtype_structured"]);
        }

        return new ExplorationSubtypeDecision(
            Subtype: ExplorationSubtype.Open,
            Confidence: 0.78d,
            ToolPathEligible: false,
            PrimaryWhy: "The request is experiential or atmospheric rather than a concrete place search.",
            MissingConstraints: ["location_or_area"],
            ReasonCodes: ["fallback_exploration_subtype_open"]);
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
            return BuildDeterministicResponse(request);
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
                ["deployment"] = route.Deployment
            },
            cancellationToken);

        var response = await aiClient.SendAsync(
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

        if (userChatResponseParser.TryParse(response, route, out var parsedResponse, out _)
            && parsedResponse.Succeeded)
        {
            return new ResponseCompositionResult(
                ReplyText: parsedResponse.ReplyText,
                SuggestedStructuredStateUpdates: parsedResponse.SuggestedStructuredStateUpdates,
                ModelUsed: parsedResponse.ModelUsed,
                DeploymentUsed: route.Deployment,
                ReasoningClass: parsedResponse.ReasoningClass,
                Warnings: parsedResponse.Warnings,
                FollowUpIntentHints: parsedResponse.FollowUpIntentHints);
        }

        logger.LogWarning(
            "Response composition fallback correlationId={CorrelationId} responseType={ResponseType}",
            correlationId,
            request.ResponseType);
        await telemetry.TrackAsync(
            "chat.response.fallback",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["stage"] = "response_composer",
                ["responseType"] = request.ResponseType.ToString()
            },
            cancellationToken);

        return BuildDeterministicResponse(request);
    }

    private static bool ShouldUseDeterministicTemplate(ResponseCompositionRequest request)
    {
        return request.ResponseType is ResponseCompositionType.Fallback or ResponseCompositionType.Placeholder
               || (request.ResponseType == ResponseCompositionType.Clarify && request.GroundedData.Entities.Count == 0);
    }

    private static ResponseCompositionResult BuildDeterministicResponse(ResponseCompositionRequest request)
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

        return new ResponseCompositionResult(
            ReplyText: replyText.Trim(),
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ModelUsed: "deterministic-response-composer",
            DeploymentUsed: "deterministic-response-composer",
            ReasoningClass: AIModelClass.Fast,
            Warnings: [],
            FollowUpIntentHints: request.MissingConstraints.Count > 0
                ? ["clarify_intent"]
                : []);
    }
}

public static class ConversationSignalAnalyzer
{
    public static ConversationSignals Analyze(string? userMessage)
    {
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var hasFinancialSignal = ContainsAny(normalized, "budget", "subscriptions", "spending", "spend", "afford", "expense", "save");
        var hasExplorationSignal = ContainsAny(normalized, "near me", "nearby", "park", "restaurant", "cafe", "walk", "beach", "view", "quiet place", "nice place");
        var hasConcretePlaceSignal = ContainsAny(normalized, "restaurant", "restaurants", "cafe", "cafes", "park", "parks", "museum", "museums", "playground", "beach");
        var hasExplicitLocation = ContainsAny(normalized, "near me", "nearby", "in ", "around ", "open now");
        var hasEmotionalFraming = ContainsAny(normalized, "i think", "i feel", "i'm worried", "i am worried", "stressed", "overwhelmed", "too high");
        var hasSubjectiveLanguage = ContainsAny(normalized, "nice", "quiet", "safe", "good", "better", "best", "feel");
        var hasCorrectionSignal = ContainsAny(normalized, "actually", "instead", "not", "wrong");
        var hasTopicSwitchSignal = ContainsAny(normalized, "new topic", "something else", "different question");
        var hasFactualQuestion = normalized.StartsWith("what ", StringComparison.Ordinal)
                                 || normalized.StartsWith("when ", StringComparison.Ordinal)
                                 || normalized.StartsWith("where ", StringComparison.Ordinal)
                                 || normalized.StartsWith("who ", StringComparison.Ordinal)
                                 || normalized.StartsWith("how ", StringComparison.Ordinal);
        var hasCompleteQuestion = normalized.EndsWith("?", StringComparison.Ordinal)
                                  || normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length >= 5;

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
            HasExplicitLocation: hasExplicitLocation);
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }
}
