using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationContextService(
    IOptions<AIIntegrationOptions> options,
    ILogger<ConversationContextService> logger) : IConversationContextService
{
    private static readonly string[] GreetingTokens = ["hi", "hello", "hey", "thanks", "thank you", "ok", "okay", "cool"];

    public ConversationContextBuildResult BuildContext(ConversationContextBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = options.Value.Execution;
        var reasonCodes = new List<string>();

        var deduped = request.RecentTurns
            .Where(turn => !string.IsNullOrWhiteSpace(turn.Content))
            .OrderBy(turn => turn.TimestampUtc)
            .GroupBy(turn => (turn.Role, NormalizeForDedup(turn.Content)))
            .Select(group => group.Last())
            .ToList();

        var excludedTurns = new List<UserChatTurn>();
        var filtered = new List<UserChatTurn>(deduped.Count);
        foreach (var turn in deduped)
        {
            if (ShouldExcludeTurn(turn))
            {
                excludedTurns.Add(turn);
                continue;
            }

            filtered.Add(turn);
        }

        if (excludedTurns.Count > 0)
        {
            reasonCodes.Add("excluded_irrelevant_turns");
        }

        var maxTurns = Math.Max(2, config.MaxContextTurns);
        var includedTurns = filtered.Count > maxTurns
            ? filtered[^maxTurns..]
            : filtered;

        if (filtered.Count > maxTurns)
        {
            reasonCodes.Add("context_turn_limit_applied");
        }

        var structuredState = BuildStructuredState(request.State, config.MaxSummaryEntries);
        if (structuredState.Count > 0)
        {
            reasonCodes.Add("structured_state_injected");
        }

        var contextSummary = BuildSummary(filtered, includedTurns, request.State, config.MaxSummaryEntries);
        if (!string.IsNullOrWhiteSpace(contextSummary))
        {
            reasonCodes.Add("context_summary_generated");
        }

        var contextMessages = new List<AIMessage>();
        if (!string.IsNullOrWhiteSpace(contextSummary))
        {
            contextMessages.Add(AIMessage.Developer($"Conversation summary: {contextSummary}"));
        }

        if (structuredState.Count > 0)
        {
            var formattedState = string.Join("; ", structuredState.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            contextMessages.Add(AIMessage.Developer($"Structured state: {formattedState}"));
        }

        contextMessages.AddRange(includedTurns.Select(turn => new AIMessage(turn.Role, turn.Content.Trim(), turn.TimestampUtc)));

        if (!string.IsNullOrWhiteSpace(request.CurrentUserMessage))
        {
            contextMessages.Add(AIMessage.User(request.CurrentUserMessage.Trim()));
        }

        logger.LogDebug(
            "Conversation context built correlationId={CorrelationId} task={TaskType} includedTurns={IncludedTurns} excludedTurns={ExcludedTurns} structuredStateKeys={StructuredStateKeys}",
            request.CorrelationId,
            request.TaskType,
            includedTurns.Count,
            excludedTurns.Count,
            structuredState.Count);

        if (reasonCodes.Count == 0)
        {
            reasonCodes.Add("context_pass_through");
        }

        return new ConversationContextBuildResult(
            ContextMessages: contextMessages,
            IncludedTurns: includedTurns,
            ExcludedTurns: excludedTurns,
            ContextSummary: contextSummary,
            StructuredState: structuredState,
            ReasonCodes: reasonCodes);
    }

    private static bool ShouldExcludeTurn(UserChatTurn turn)
    {
        if (turn.IsResolved && turn.Role != AIMessageRole.User)
        {
            return true;
        }

        var normalized = NormalizeForDedup(turn.Content);
        if (normalized.Length <= 12 && GreetingTokens.Any(token => normalized.Equals(token, StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeForDedup(string content)
    {
        return string.Join(' ', content.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyDictionary<string, string> BuildStructuredState(ConversationStateSnapshot? state, int maxSummaryEntries)
    {
        if (state is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(state.ActiveTopic))
        {
            result["active_topic"] = state.ActiveTopic.Trim();
        }

        if (!string.IsNullOrWhiteSpace(state.UserIntent))
        {
            result["user_intent"] = state.UserIntent.Trim();
        }

        if (!string.IsNullOrWhiteSpace(state.BudgetPreference))
        {
            result["budget_preference"] = state.BudgetPreference.Trim();
        }

        if (!string.IsNullOrWhiteSpace(state.LocationPreference))
        {
            result["location_preference"] = state.LocationPreference.Trim();
        }

        if (!string.IsNullOrWhiteSpace(state.MerchantInvestigationSubject))
        {
            result["merchant_subject"] = state.MerchantInvestigationSubject.Trim();
        }

        foreach (var constraint in state.Constraints.Take(6))
        {
            if (!string.IsNullOrWhiteSpace(constraint.Key) && !string.IsNullOrWhiteSpace(constraint.Value))
            {
                result[$"constraint_{constraint.Key.Trim()}"] = constraint.Value.Trim();
            }
        }

        if (state.RecentConclusions.Count > 0)
        {
            result["recent_conclusions"] = string.Join(" | ", state.RecentConclusions.Take(3));
        }

        if (state.Summaries.Count > 0)
        {
            result["conversation_summaries"] = string.Join(" | ", state.Summaries.Take(Math.Max(1, maxSummaryEntries)));
        }

        return result;
    }

    private static string? BuildSummary(
        IReadOnlyList<UserChatTurn> filteredTurns,
        IReadOnlyList<UserChatTurn> includedTurns,
        ConversationStateSnapshot? state,
        int maxSummaryEntries)
    {
        if (filteredTurns.Count <= includedTurns.Count && (state?.Summaries.Count ?? 0) == 0)
        {
            return null;
        }

        var summaryParts = new List<string>();
        if (filteredTurns.Count > includedTurns.Count)
        {
            var omittedCount = filteredTurns.Count - includedTurns.Count;
            summaryParts.Add($"Trimmed {omittedCount} older turns.");
        }

        if (state is not null && state.Summaries.Count > 0)
        {
            summaryParts.AddRange(state.Summaries.Take(Math.Max(1, maxSummaryEntries)));
        }

        return summaryParts.Count == 0 ? null : string.Join(" ", summaryParts);
    }
}
