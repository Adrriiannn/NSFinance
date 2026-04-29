using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface IConversationIntelligenceService
{
    Task<ConversationIntelligenceEvaluationResult> EvaluateAsync(
        ConversationIntelligencePromptInput input,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface IConversationIntelligenceParser
{
    bool TryParse(
        AIResponse response,
        AIModelRoute route,
        out ConversationIntelligenceResult? intelligence,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason);
}

public sealed class ConversationIntelligenceService(
    IConversationIntelligencePromptBuilder promptBuilder,
    IConversationIntelligenceParser parser,
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IChatTelemetry telemetry,
    ILogger<ConversationIntelligenceService> logger) : IConversationIntelligenceService
{
    public async Task<ConversationIntelligenceEvaluationResult> EvaluateAsync(
        ConversationIntelligencePromptInput input,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var route = modelRouter.Resolve(
            AITaskType.ConversationDecision,
            AIModelClass.Fast,
            complexityHint: "conversation_intelligence_v1");
        var prompt = promptBuilder.BuildPrompt(input);

        await telemetry.TrackAsync(
            "chat.model.invocation",
            new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["taskType"] = route.TaskType.ToString(),
                ["modelClass"] = route.ModelClass.ToString(),
                ["model"] = route.Model,
                ["deployment"] = route.Deployment,
                ["invocationStage"] = "conversation_intelligence",
                ["routeReason"] = route.Reason,
                ["routeNotes"] = route.Notes.ToArray(),
                ["selectionKind"] = ConversationModelSelectionKind.Fast.ToString(),
                ["selectionReason"] = "conversation_intelligence_fast",
                ["usedModelInvocation"] = true
            },
            cancellationToken);

        AIResponse response;
        try
        {
            response = await aiClient.SendAsync(
                AIRequest.Create(
                    taskType: AITaskType.ConversationDecision,
                    preferredModelClass: AIModelClass.Fast,
                    messages: prompt.Messages,
                    correlationId: correlationId,
                    systemInstructions: prompt.SystemInstructions,
                    structuredOutputSchemaName: prompt.StructuredSchemaName,
                    temperature: 0.1d,
                    maxOutputTokens: 450,
                    metadata: input.ChatRequest.Metadata),
                route,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Conversation intelligence model invocation failed correlationId={CorrelationId}",
                correlationId);
            var fallback = BuildFallback(input, "conversation_intelligence_model_call_failed");
            return new ConversationIntelligenceEvaluationResult(
                fallback,
                route,
                UsedModelInvocation: true,
                FallbackUsed: true,
                Warnings: ["conversation_intelligence_model_call_failed"]);
        }

        if (parser.TryParse(
                response,
                route,
                out var parsed,
                out var parserReasonCodes,
                out var failureReason)
            && parsed is not null)
        {
            await telemetry.TrackAsync(
                "chat.conversation_intelligence.selected",
                BuildTelemetryPayload(correlationId, parsed, parserReasonCodes, fallbackUsed: false),
                cancellationToken);

            return new ConversationIntelligenceEvaluationResult(
                parsed,
                route,
                UsedModelInvocation: true,
                FallbackUsed: false,
                Warnings: []);
        }

        logger.LogWarning(
            "Conversation intelligence parse fallback correlationId={CorrelationId} failureReason={FailureReason}",
            correlationId,
            failureReason ?? "conversation_intelligence_parse_failed");
        var recovered = BuildFallback(input, failureReason ?? "conversation_intelligence_parse_failed");
        await telemetry.TrackAsync(
            "chat.conversation_intelligence.selected",
            BuildTelemetryPayload(correlationId, recovered, parserReasonCodes, fallbackUsed: true),
            cancellationToken);

        return new ConversationIntelligenceEvaluationResult(
            recovered,
            route,
            UsedModelInvocation: true,
            FallbackUsed: true,
            Warnings:
            [
                failureReason ?? "conversation_intelligence_parse_failed",
                "conversation_intelligence_fallback"
            ]);
    }

    private static IReadOnlyDictionary<string, object?> BuildTelemetryPayload(
        string correlationId,
        ConversationIntelligenceResult intelligence,
        IReadOnlyList<string> reasonCodes,
        bool fallbackUsed)
    {
        return new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId,
            ["conversationPhase"] = intelligence.ConversationPhase,
            ["emotionalState"] = intelligence.UserEmotionalState,
            ["nextAction"] = intelligence.NextAction.Type,
            ["shouldClarify"] = intelligence.ShouldClarify,
            ["shouldExecuteTool"] = intelligence.ShouldExecuteTool,
            ["shouldContinueTask"] = intelligence.ShouldContinueTask,
            ["fallbackUsed"] = fallbackUsed,
            ["reasonCodes"] = intelligence.ReasonCodes.Concat(reasonCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static ConversationIntelligenceResult BuildFallback(
        ConversationIntelligencePromptInput input,
        string reasonCode)
    {
        var hasPriorResults = input.ResultContext is not null
                              || input.ResultContextReadResult?.ActiveResultContext is not null;
        var interpretation = input.TurnInterpretation;
        var actionType = interpretation?.ActionType;
        var shouldExecute = actionType == TurnInterpretationActionType.ReadyForSearch;
        var shouldClarify = actionType is TurnInterpretationActionType.MissingLocation or TurnInterpretationActionType.MissingTarget
                            && !hasPriorResults;
        var isFollowUp = hasPriorResults && actionType != TurnInterpretationActionType.ReadyForSearch;
        var nextAction = shouldExecute
            ? "execute_search"
            : isFollowUp
                ? "filter_previous_results"
                : shouldClarify
                    ? "ask_clarification"
                    : "answer_directly";

        return new ConversationIntelligenceResult(
            ConversationPhase: isFollowUp ? "refinement" : "active_task",
            UserEmotionalState: "neutral",
            UserIntentConfidence: Math.Clamp(interpretation?.Confidence ?? 0.55d, 0d, 1d),
            ShouldContinueTask: isFollowUp || shouldExecute,
            ShouldClarify: shouldClarify,
            ShouldExecuteTool: shouldExecute,
            ShouldAcknowledgeIssue: false,
            ResponseStyle: new ConversationResponseStyle(
                Tone: "helpful",
                Verbosity: "medium",
                AvoidRepetition: true),
            TaskState: new ConversationTaskState(
                IsNewTask: !isFollowUp,
                IsFollowUp: isFollowUp,
                IsRefinement: isFollowUp,
                IsUserCorrection: false,
                TargetPreviousResults: isFollowUp),
            NextAction: new ConversationNextAction(
                Type: nextAction,
                Reason: "Recovered from structured turn interpretation and active result context.",
                Target: hasPriorResults ? "active_result_set" : null,
                Requirement: null),
            ReasonCodes: [reasonCode, "conversation_intelligence_fallback"]);
    }
}

public sealed class ConversationIntelligenceParser : IConversationIntelligenceParser
{
    public bool TryParse(
        AIResponse response,
        AIModelRoute route,
        out ConversationIntelligenceResult? intelligence,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason)
    {
        intelligence = null;
        failureReason = null;
        var localReasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            failureReason = response.FailureReason ?? "conversation_intelligence_ai_failed";
            reasonCodes = [failureReason];
            return false;
        }

        var raw = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            failureReason = "conversation_intelligence_empty_payload";
            reasonCodes = [failureReason];
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            failureReason = "conversation_intelligence_invalid_json";
            reasonCodes = [failureReason];
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryReadString(root, "conversation_phase", out var phase)
                || !TryReadString(root, "user_emotional_state", out var emotionalState)
                || !TryReadDouble(root, "user_intent_confidence", out var confidence)
                || !TryReadBool(root, "should_continue_task", out var shouldContinue)
                || !TryReadBool(root, "should_clarify", out var shouldClarify)
                || !TryReadBool(root, "should_execute_tool", out var shouldExecute)
                || !TryReadBool(root, "should_acknowledge_issue", out var shouldAcknowledge)
                || !TryReadObject(root, "response_style", out var styleRoot)
                || !TryReadObject(root, "task_state", out var taskRoot)
                || !TryReadObject(root, "next_action", out var actionRoot))
            {
                failureReason = "conversation_intelligence_missing_required_fields";
                reasonCodes = [failureReason];
                return false;
            }

            if (!Allowed.Phases.Contains(phase)
                || !Allowed.EmotionalStates.Contains(emotionalState)
                || confidence is < 0d or > 1d
                || !TryReadString(styleRoot, "tone", out var tone)
                || !Allowed.Tones.Contains(tone)
                || !TryReadString(styleRoot, "verbosity", out var verbosity)
                || !Allowed.Verbosities.Contains(verbosity)
                || !TryReadBool(styleRoot, "avoid_repetition", out var avoidRepetition)
                || !TryReadBool(taskRoot, "is_new_task", out var isNewTask)
                || !TryReadBool(taskRoot, "is_follow_up", out var isFollowUp)
                || !TryReadBool(taskRoot, "is_refinement", out var isRefinement)
                || !TryReadBool(taskRoot, "is_user_correction", out var isCorrection)
                || !TryReadBool(taskRoot, "target_previous_results", out var targetPreviousResults)
                || !TryReadString(actionRoot, "type", out var actionType)
                || !Allowed.NextActions.Contains(actionType)
                || !TryReadString(actionRoot, "reason", out var actionReason)
                || string.IsNullOrWhiteSpace(actionReason))
            {
                failureReason = "conversation_intelligence_invalid_payload";
                reasonCodes = [failureReason];
                return false;
            }

            intelligence = new ConversationIntelligenceResult(
                ConversationPhase: phase,
                UserEmotionalState: emotionalState,
                UserIntentConfidence: confidence,
                ShouldContinueTask: shouldContinue,
                ShouldClarify: shouldClarify,
                ShouldExecuteTool: shouldExecute,
                ShouldAcknowledgeIssue: shouldAcknowledge,
                ResponseStyle: new ConversationResponseStyle(
                    Tone: tone,
                    Verbosity: verbosity,
                    AvoidRepetition: avoidRepetition),
                TaskState: new ConversationTaskState(
                    IsNewTask: isNewTask,
                    IsFollowUp: isFollowUp,
                    IsRefinement: isRefinement,
                    IsUserCorrection: isCorrection,
                    TargetPreviousResults: targetPreviousResults),
                NextAction: new ConversationNextAction(
                    Type: actionType,
                    Reason: actionReason.Trim(),
                    Target: TryReadNullableString(actionRoot, "target"),
                    Requirement: TryReadNullableString(actionRoot, "requirement")),
                ReasonCodes: ReadStringList(root, "reason_codes"));

            localReasonCodes.Add("conversation_intelligence_parse_success");
            reasonCodes = localReasonCodes;
            return true;
        }
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryReadNullableString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;
    }

    private static bool TryReadDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0d;
        return TryGetProperty(element, propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetDouble(out value);
    }

    private static bool TryReadBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!TryGetProperty(element, propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadObject(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        return TryGetProperty(element, propertyName, out value)
               && value.ValueKind == JsonValueKind.Object;
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()?.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static class Allowed
    {
        public static readonly HashSet<string> Phases =
        [
            "start",
            "active_task",
            "refinement",
            "correction",
            "comparison",
            "frustration",
            "closing",
            "off_topic"
        ];

        public static readonly HashSet<string> EmotionalStates =
        [
            "neutral",
            "curious",
            "rushed",
            "confused",
            "frustrated",
            "annoyed",
            "satisfied"
        ];

        public static readonly HashSet<string> Tones =
        [
            "direct",
            "helpful",
            "apologetic",
            "concise",
            "warm",
            "reassuring"
        ];

        public static readonly HashSet<string> Verbosities =
        [
            "short",
            "medium",
            "detailed"
        ];

        public static readonly HashSet<string> NextActions =
        [
            "execute_search",
            "filter_previous_results",
            "sort_previous_results",
            "enrich_details",
            "ask_clarification",
            "answer_directly",
            "soft_redirect"
        ];
    }
}
