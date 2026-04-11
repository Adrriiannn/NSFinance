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

        UserChatStructuredResponse? structured;
        try
        {
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

        parsedResponse = new UserChatResponse(
            ReplyText: string.IsNullOrWhiteSpace(structured.ReplyText)
                ? "I couldn't generate a response."
                : structured.ReplyText.Trim(),
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
        reasonCodes = localReasonCodes;
        return true;
    }
}
