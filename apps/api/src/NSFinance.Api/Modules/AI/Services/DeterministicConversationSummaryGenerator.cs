using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class DeterministicConversationSummaryGenerator : IConversationSummaryGenerator
{
    private static readonly string[] LowValueTokens = ["hi", "hello", "hey", "thanks", "thank you", "ok", "okay", "cool"];

    public string GenerateSummary(
        IReadOnlyList<ConversationMessage> messages,
        string? previousSummary,
        int maxSummaryLengthChars)
    {
        if (messages.Count == 0 && string.IsNullOrWhiteSpace(previousSummary))
        {
            return "No meaningful conversation history yet.";
        }

        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            summaryParts.Add(previousSummary.Trim());
        }

        var userPrompts = messages
            .Where(x => x.Role == ConversationMessageRole.User && IsHighValueText(x.Content))
            .OrderByDescending(x => x.MessageOrder)
            .Take(4)
            .Reverse()
            .Select(x => x.Content.Trim())
            .ToArray();
        if (userPrompts.Length > 0)
        {
            summaryParts.Add($"User focus: {string.Join(" | ", userPrompts)}");
        }

        var assistantGuidance = messages
            .Where(x => x.Role == ConversationMessageRole.Assistant && IsHighValueText(x.Content))
            .OrderByDescending(x => x.MessageOrder)
            .Take(4)
            .Reverse()
            .Select(x => x.Content.Trim())
            .ToArray();
        if (assistantGuidance.Length > 0)
        {
            summaryParts.Add($"Assistant guidance: {string.Join(" | ", assistantGuidance)}");
        }

        var latestTopic = messages
            .Where(x => !string.IsNullOrWhiteSpace(x.Topic))
            .OrderByDescending(x => x.MessageOrder)
            .Select(x => x.Topic!.Trim())
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(latestTopic))
        {
            summaryParts.Add($"Active topic: {latestTopic}");
        }

        summaryParts.Add($"Messages summarized: {messages.Count}.");

        var joined = string.Join(" ", summaryParts.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (joined.Length <= maxSummaryLengthChars)
        {
            return joined;
        }

        return joined[..maxSummaryLengthChars].TrimEnd();
    }

    private static bool IsHighValueText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalized = string.Join(' ', content.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 12 && LowValueTokens.Any(x => normalized.Equals(x, StringComparison.Ordinal)))
        {
            return false;
        }

        return normalized.Length >= 8;
    }
}
