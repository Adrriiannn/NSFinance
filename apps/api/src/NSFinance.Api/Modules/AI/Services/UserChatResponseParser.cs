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
    bool TryParse(
        AIResponse response,
        AIModelRoute route,
        out UserChatResponse parsedResponse,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason);
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

    public bool TryParse(
        AIResponse response,
        AIModelRoute route,
        out UserChatResponse parsedResponse,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason)
    {
        failureReason = null;
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
            failureReason = response.FailureReason ?? "ai_request_failed";
            return false;
        }

        var payloadCandidates = EnumeratePayloadCandidates(response).ToArray();
        if (payloadCandidates.Length == 0)
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
            failureReason = "empty_payload";
            return false;
        }

        UserChatStructuredResponse? structured = null;
        JsonElement? parsedRoot = null;
        foreach (var candidate in payloadCandidates)
        {
            if (!TryParseStructuredPayloadCandidate(
                    candidate,
                    out structured,
                    out parsedRoot,
                    out var recoveryReasonCodes))
            {
                continue;
            }

            localReasonCodes.AddRange(recoveryReasonCodes);
            break;
        }

        if (structured is null)
        {
            localReasonCodes.Add("structured_parse_failed");
            parsedResponse = new UserChatResponse(
                ReplyText: "I couldn't generate a response.",
                ModelUsed: response.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: null,
                Warnings: ["structured_parse_failed"],
                FollowUpIntentHints: [],
                Succeeded: false,
                FailureReason: "structured_parse_failed");
            reasonCodes = localReasonCodes;
            failureReason = "structured_parse_failed";
            return false;
        }

        var resolvedReplyText = ResolveReplyText(structured, parsedRoot);
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
            failureReason = "structured_reply_text_missing";
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

    private static IEnumerable<string> EnumeratePayloadCandidates(AIResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.StructuredPayloadJson))
        {
            yield return response.StructuredPayloadJson!;
        }

        if (!string.IsNullOrWhiteSpace(response.Content)
            && !string.Equals(response.Content, response.StructuredPayloadJson, StringComparison.Ordinal))
        {
            yield return response.Content;
        }
    }

    private static bool TryParseStructuredPayloadCandidate(
        string payload,
        out UserChatStructuredResponse? structured,
        out JsonElement? parsedRoot,
        out IReadOnlyList<string> recoveryReasonCodes)
    {
        var localReasonCodes = new List<string>();

        if (TryParseStructuredPayload(payload, out structured, out parsedRoot))
        {
            recoveryReasonCodes = localReasonCodes;
            return true;
        }

        if (TryStripMarkdownFence(payload, out var unfenced)
            && TryParseStructuredPayload(unfenced, out structured, out parsedRoot))
        {
            localReasonCodes.Add("structured_payload_recovered_from_markdown_fence");
            recoveryReasonCodes = localReasonCodes;
            return true;
        }

        foreach (var extracted in EnumerateJsonObjectCandidates(payload))
        {
            if (!TryParseStructuredPayload(extracted, out structured, out parsedRoot))
            {
                continue;
            }

            localReasonCodes.Add("structured_payload_recovered_from_wrapper_text");
            recoveryReasonCodes = localReasonCodes;
            return true;
        }

        if (TryStripMarkdownFence(payload, out unfenced))
        {
            foreach (var extracted in EnumerateJsonObjectCandidates(unfenced))
            {
                if (!TryParseStructuredPayload(extracted, out structured, out parsedRoot))
                {
                    continue;
                }

                localReasonCodes.Add("structured_payload_recovered_from_markdown_fence");
                localReasonCodes.Add("structured_payload_recovered_from_wrapper_text");
                recoveryReasonCodes = localReasonCodes;
                return true;
            }
        }

        structured = null;
        parsedRoot = null;
        recoveryReasonCodes = localReasonCodes;
        return false;
    }

    private static bool TryParseStructuredPayload(
        string payload,
        out UserChatStructuredResponse? structured,
        out JsonElement? parsedRoot)
    {
        structured = null;
        parsedRoot = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            structured = JsonSerializer.Deserialize<UserChatStructuredResponse>(payload, SerializerOptions);
            if (structured is null)
            {
                return false;
            }

            parsedRoot = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryStripMarkdownFence(string payload, out string unfenced)
    {
        unfenced = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var trimmed = payload.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        var firstLineBreak = trimmed.IndexOf('\n');
        if (firstLineBreak < 0)
        {
            return false;
        }

        var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence <= firstLineBreak)
        {
            return false;
        }

        unfenced = trimmed[(firstLineBreak + 1)..closingFence].Trim();
        return !string.IsNullOrWhiteSpace(unfenced);
    }

    private static IEnumerable<string> EnumerateJsonObjectCandidates(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        var text = payload.Trim();
        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{')
            {
                continue;
            }

            var depth = 0;
            var inString = false;
            var escapeNext = false;
            for (var index = start; index < text.Length; index++)
            {
                var ch = text[index];
                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (ch == '\\' && inString)
                {
                    escapeNext = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch != '}')
                {
                    continue;
                }

                depth--;
                if (depth != 0)
                {
                    continue;
                }

                yield return text[start..(index + 1)];
                break;
            }
        }
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
