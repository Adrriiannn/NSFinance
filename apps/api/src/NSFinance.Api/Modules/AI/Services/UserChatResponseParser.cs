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
