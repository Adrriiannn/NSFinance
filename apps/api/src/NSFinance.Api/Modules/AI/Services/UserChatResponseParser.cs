using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record UserChatStructuredResponse(
    string ReplyText,
    string? ReferencedContextSummary,
    IReadOnlyDictionary<string, string>? SuggestedStructuredStateUpdates,
    IReadOnlyList<string>? Warnings,
    IReadOnlyList<string>? FollowUpIntentHints);

public interface IUserChatResponseParser
{
    bool TryParse(AIResponse response, AIModelRoute route, out UserChatResponse parsedResponse, out IReadOnlyList<string> reasonCodes);
}

public interface IConversationDecisionParser
{
    bool TryParse(
        AIResponse response,
        AIModelRoute route,
        ConversationStateSnapshot currentState,
        out ConversationTurnStrategyDecision? decision,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason);
}

public interface IExplorationSubtypeDecisionParser
{
    bool TryParse(
        AIResponse response,
        AIModelRoute route,
        out ExplorationSubtypeDecision? decision,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason);
}

public sealed class UserChatResponseParser : IUserChatResponseParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool TryParse(AIResponse response, AIModelRoute route, out UserChatResponse parsedResponse, out IReadOnlyList<string> reasonCodes)
    {
        var localReasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            parsedResponse = new UserChatResponse(
                ReplyText: "I couldn't process that request right now.",
                ModelUsed: route.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: null,
                Warnings: [response.FailureReason ?? "ai_request_failed"],
                FollowUpIntentHints: [],
                Succeeded: false,
                FailureReason: response.FailureReason ?? "ai_request_failed");
            reasonCodes = ["ai_response_failed"];
            return false;
        }

        var payload = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(payload))
        {
            parsedResponse = new UserChatResponse(
                ReplyText: "I couldn't generate a response.",
                ModelUsed: route.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: null,
                Warnings: ["empty_payload"],
                FollowUpIntentHints: [],
                Succeeded: false,
                FailureReason: "empty_payload");
            reasonCodes = ["empty_payload"];
            return false;
        }

        JsonDocument? parsedDocument = null;
        UserChatStructuredResponse? structured;
        try
        {
            parsedDocument = JsonDocument.Parse(payload);
            structured = JsonSerializer.Deserialize<UserChatStructuredResponse>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            structured = null;
        }

        if (structured is null)
        {
            localReasonCodes.Add("fallback_to_raw_content");
            var rawContent = (response.Content ?? string.Empty).Trim();
            parsedResponse = new UserChatResponse(
                ReplyText: rawContent.Length == 0 ? "I couldn't generate a response." : rawContent,
                ModelUsed: response.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: null,
                Warnings: ["structured_parse_failed"],
                FollowUpIntentHints: [],
                Succeeded: rawContent.Length > 0,
                FailureReason: rawContent.Length > 0 ? null : "structured_parse_failed");
            reasonCodes = localReasonCodes;
            return rawContent.Length > 0;
        }

        var resolvedReplyText = ResolveReplyText(structured, parsedDocument?.RootElement);
        if (string.IsNullOrWhiteSpace(resolvedReplyText))
        {
            localReasonCodes.Add("structured_reply_text_missing");
            parsedResponse = new UserChatResponse(
                ReplyText: "I couldn't generate a response.",
                ModelUsed: response.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: structured.ReferencedContextSummary,
                Warnings: ["structured_reply_text_missing"],
                FollowUpIntentHints: structured.FollowUpIntentHints ?? [],
                Succeeded: false,
                FailureReason: "structured_reply_text_missing");
            reasonCodes = localReasonCodes;
            return false;
        }

        parsedResponse = new UserChatResponse(
            ReplyText: resolvedReplyText,
            ModelUsed: response.Model,
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: structured.SuggestedStructuredStateUpdates is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(structured.SuggestedStructuredStateUpdates, StringComparer.OrdinalIgnoreCase),
            ReferencedContextSummary: structured.ReferencedContextSummary,
            Warnings: structured.Warnings ?? [],
            FollowUpIntentHints: structured.FollowUpIntentHints ?? [],
            Succeeded: true,
            FailureReason: null);

        localReasonCodes.Add("structured_parse_success");
        if (string.IsNullOrWhiteSpace(structured.ReplyText)
            || !string.Equals(structured.ReplyText.Trim(), resolvedReplyText, StringComparison.Ordinal))
        {
            localReasonCodes.Add("structured_reply_text_recovered_from_alias");
        }

        reasonCodes = localReasonCodes;
        return true;
    }

    private static string? ResolveReplyText(UserChatStructuredResponse structured, JsonElement? root)
    {
        if (!string.IsNullOrWhiteSpace(structured.ReplyText))
        {
            return structured.ReplyText.Trim();
        }

        if (root is null || root.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var aliases = new[] { "replyText", "reply_text", "reply", "message", "content", "text" };
        foreach (var alias in aliases)
        {
            if (!TryReadString(root.Value, alias, out var value))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString();
                return true;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                value = property.Value.GetRawText();
                return true;
            }

            value = property.Value.ToString();
            return true;
        }

        return false;
    }
}

public sealed class ConversationDecisionParser : IConversationDecisionParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool TryParse(
        AIResponse response,
        AIModelRoute route,
        ConversationStateSnapshot currentState,
        out ConversationTurnStrategyDecision? decision,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason)
    {
        decision = null;
        failureReason = null;
        var localReasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            failureReason = response.FailureReason ?? "conversation_decision_ai_failed";
            localReasonCodes.Add("conversation_decision_ai_failed");
            reasonCodes = localReasonCodes;
            return false;
        }

        if (!TryParsePayload(response, out ConversationDecisionPayload? payload, out var payloadFailure))
        {
            failureReason = payloadFailure;
            localReasonCodes.Add(payloadFailure ?? "conversation_decision_invalid_payload");
            reasonCodes = localReasonCodes;
            return false;
        }

        if (!TryMapEnum(payload!.Strategy, out ConversationBehaviorStrategy strategy)
            || !TryMapEnum(payload.ModeCandidate, out ConversationMode modeCandidate)
            || !TryMapEnum(payload.FollowUpBindingType, out FollowUpBindingType bindingType)
            || !TryMapEnum(payload.ToolExecutionPermission, out ToolExecutionPermission toolPermission)
            || payload.Readiness is null
            || !TryMapEnum(payload.Readiness.From, out ConversationReadinessLevel readinessFrom)
            || !TryMapEnum(payload.Readiness.To, out ConversationReadinessLevel readinessTo))
        {
            failureReason = "conversation_decision_invalid_enum";
            localReasonCodes.Add(failureReason);
            reasonCodes = localReasonCodes;
            return false;
        }

        if (payload.Confidence is < 0d or > 1d)
        {
            failureReason = "conversation_decision_invalid_confidence";
            localReasonCodes.Add(failureReason);
            reasonCodes = localReasonCodes;
            return false;
        }

        decision = new ConversationTurnStrategyDecision(
            Strategy: strategy,
            ModeCandidate: modeCandidate,
            Readiness: new ReadinessTransition(
                From: currentState.ReadinessLevel == default && currentState.SchemaVersion < 2
                    ? readinessFrom
                    : currentState.ReadinessLevel,
                To: readinessTo),
            Confidence: payload.Confidence,
            FollowUpBindingType: bindingType,
            ClarificationQuestion: Normalize(payload.ClarificationQuestion),
            SuggestedOptions: NormalizeList(payload.SuggestedOptions),
            ToolExecutionPermission: toolPermission,
            ReasonCodes: NormalizeList(payload.ReasonCodes));

        localReasonCodes.Add("conversation_decision_parse_success");
        reasonCodes = localReasonCodes;
        return true;
    }

    private static bool TryParsePayload(
        AIResponse response,
        out ConversationDecisionPayload? payload,
        out string? failureReason)
    {
        payload = null;
        failureReason = null;

        var raw = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            failureReason = "conversation_decision_empty_payload";
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<ConversationDecisionPayload>(raw, SerializerOptions);
        }
        catch (JsonException)
        {
            failureReason = "conversation_decision_invalid_json";
            return false;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Strategy)
            || string.IsNullOrWhiteSpace(payload.ModeCandidate)
            || string.IsNullOrWhiteSpace(payload.FollowUpBindingType)
            || string.IsNullOrWhiteSpace(payload.ToolExecutionPermission))
        {
            failureReason = "conversation_decision_missing_required_fields";
            return false;
        }

        return true;
    }

    private static bool TryMapEnum<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out parsed);
    }

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ConversationDecisionPayload(
        string Strategy,
        string ModeCandidate,
        ConversationDecisionReadinessPayload? Readiness,
        double Confidence,
        string FollowUpBindingType,
        string? ClarificationQuestion,
        IReadOnlyList<string>? SuggestedOptions,
        string ToolExecutionPermission,
        IReadOnlyList<string>? ReasonCodes);

    private sealed record ConversationDecisionReadinessPayload(
        string From,
        string To);
}

public sealed class ExplorationSubtypeDecisionParser : IExplorationSubtypeDecisionParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool TryParse(
        AIResponse response,
        AIModelRoute route,
        out ExplorationSubtypeDecision? decision,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason)
    {
        decision = null;
        failureReason = null;
        var localReasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            failureReason = response.FailureReason ?? "exploration_subtype_ai_failed";
            localReasonCodes.Add(failureReason);
            reasonCodes = localReasonCodes;
            return false;
        }

        var raw = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            failureReason = "exploration_subtype_empty_payload";
            localReasonCodes.Add(failureReason);
            reasonCodes = localReasonCodes;
            return false;
        }

        ExplorationSubtypePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ExplorationSubtypePayload>(raw, SerializerOptions);
        }
        catch (JsonException)
        {
            failureReason = "exploration_subtype_invalid_json";
            localReasonCodes.Add(failureReason);
            reasonCodes = localReasonCodes;
            return false;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Subtype)
            || string.IsNullOrWhiteSpace(payload.PrimaryWhy)
            || payload.Confidence is < 0d or > 1d
            || !Enum.TryParse(payload.Subtype, ignoreCase: true, out ExplorationSubtype subtype))
        {
            failureReason = "exploration_subtype_invalid_payload";
            localReasonCodes.Add(failureReason);
            reasonCodes = localReasonCodes;
            return false;
        }

        decision = new ExplorationSubtypeDecision(
            Subtype: subtype,
            Confidence: payload.Confidence,
            ToolPathEligible: payload.ToolPathEligible,
            PrimaryWhy: payload.PrimaryWhy.Trim(),
            MissingConstraints: NormalizeList(payload.MissingConstraints),
            ReasonCodes: NormalizeList(payload.ReasonCodes));

        localReasonCodes.Add("exploration_subtype_parse_success");
        reasonCodes = localReasonCodes;
        return true;
    }

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private sealed record ExplorationSubtypePayload(
        string Subtype,
        double Confidence,
        bool ToolPathEligible,
        string PrimaryWhy,
        IReadOnlyList<string>? MissingConstraints,
        IReadOnlyList<string>? ReasonCodes);
}
