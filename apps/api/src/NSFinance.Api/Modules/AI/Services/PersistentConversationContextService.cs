using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class PersistentConversationContextService(
    AppDbContext dbContext,
    IConversationStateService conversationStateService,
    IConversationSummaryService conversationSummaryService,
    IOptions<AIIntegrationOptions> options,
    ILogger<PersistentConversationContextService> logger) : IPersistentConversationContextService
{
    private static readonly string[] GreetingTokens = ["hi", "hello", "hey", "thanks", "thank you", "ok", "okay", "cool"];

    public async Task<PersistentConversationContextBuildResult> BuildContextAsync(
        PersistentConversationContextBuildRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var stage = "start";
        logger.LogInformation(
            "PersistentConversationContext build start correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} task={TaskType} cancellationRequested={CancellationRequested}",
            request.CorrelationId,
            request.ConversationThreadId,
            request.ConversationTurnId,
            request.TaskType,
            cancellationToken.IsCancellationRequested);

        var memoryOptions = options.Value.Memory;
        var budget = ResolveBudget(memoryOptions, request.TaskType);
        var reasonCodes = new List<string>();

        try
        {
            stage = "ensure_thread_ownership";
            await EnsureThreadOwnershipAsync(request.UserId, request.ConversationThreadId, cancellationToken);

            stage = "refresh_summary_if_needed";
            var refreshResult = await conversationSummaryService.RefreshSummaryIfNeededAsync(
                request.UserId,
                request.ConversationThreadId,
                request.TaskType,
                request.CorrelationId,
                cancellationToken);
            reasonCodes.AddRange(refreshResult.ReasonCodes);

            stage = "load_latest_state";
            var latestState = await conversationStateService.GetLatestStateAsync(
                request.UserId,
                request.ConversationThreadId,
                cancellationToken);
            if (latestState is not null)
            {
                reasonCodes.Add("state_snapshot_loaded");
            }

            var fetchCount = Math.Clamp(
                budget.MaxRecentMessages * Math.Max(1, memoryOptions.RecentMessageFetchMultiplier),
                budget.MaxRecentMessages,
                500);

            stage = "fetch_recent_messages";
            var fetchedMessages = await dbContext.ConversationMessages
                .AsNoTracking()
                .Include(x => x.ConversationTurn)
                .Where(x => x.ConversationThreadId == request.ConversationThreadId)
                .OrderByDescending(x => x.MessageOrder)
                .Take(fetchCount)
                .ToListAsync(cancellationToken);
            fetchedMessages = fetchedMessages.OrderBy(x => x.MessageOrder).ToList();

            var filteredResult = FilterMessages(fetchedMessages, latestState?.State, budget.MaxRecentMessages);
            reasonCodes.AddRange(filteredResult.ReasonCodes);

            var summaryText = TrimSummary(refreshResult.LatestSummary?.SummaryText, budget.MaxSummaryChars);
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                reasonCodes.Add("summary_included");
            }

            var structuredState = BuildStructuredState(latestState?.State, budget, memoryOptions.MaxStateValueLength);
            if (structuredState.Count > 0)
            {
                reasonCodes.Add("structured_state_included");
            }

            var contextMessages = new List<AIMessage>(filteredResult.Included.Count + 1);
            contextMessages.AddRange(filteredResult.Included.Select(ToAIMessage));

            if (request.IncludeCurrentUserMessage && !string.IsNullOrWhiteSpace(request.CurrentUserMessage))
            {
                contextMessages.Add(AIMessage.User(request.CurrentUserMessage.Trim()));
                reasonCodes.Add("current_user_message_included");
            }

            var maxPromptTokens = request.MaxPromptTokensOverride ?? budget.MaxPromptTokens;
            var trimmedByBudget = TrimMessagesToBudget(contextMessages, filteredResult.Included, maxPromptTokens, out var trimReason);
            if (!string.IsNullOrWhiteSpace(trimReason))
            {
                reasonCodes.Add(trimReason);
            }

            var finalContextMessages = trimmedByBudget.ContextMessages;
            var includedMessageIds = trimmedByBudget.IncludedMessages.Select(x => x.Id).ToArray();
            var excludedMessageIds = filteredResult.Excluded.Select(x => x.Id)
                .Concat(trimmedByBudget.ExcludedByBudget.Select(x => x.Id))
                .Distinct()
                .ToArray();

            var estimatedPromptTokens = EstimateTokens(finalContextMessages);
            reasonCodes.Add("context_built");

            stage = "persist_build_log";
            await PersistBuildLogIfEnabledAsync(
                request,
                estimatedPromptTokens,
                trimReason,
                includedMessageIds.Length,
                refreshResult.LatestSummary?.SummaryVersion,
                latestState?.StateVersion,
                memoryOptions,
                cancellationToken);

            stopwatch.Stop();
            logger.LogInformation(
                "Persistent conversation context built correlationId={CorrelationId} threadId={ThreadId} task={TaskType} stage={Stage} included={IncludedCount} excluded={ExcludedCount} estTokens={EstimatedTokens} trimReason={TrimReason} elapsedMs={ElapsedMs}",
                request.CorrelationId,
                request.ConversationThreadId,
                request.TaskType,
                stage,
                includedMessageIds.Length,
                excludedMessageIds.Length,
                estimatedPromptTokens,
                trimReason ?? "none",
                stopwatch.ElapsedMilliseconds);

            return new PersistentConversationContextBuildResult(
                ContextMessages: finalContextMessages,
                IncludedMessageIds: includedMessageIds,
                ExcludedMessageIds: excludedMessageIds,
                ContextSummary: summaryText,
                IncludedSummaryVersion: refreshResult.LatestSummary?.SummaryVersion,
                IncludedStateVersion: latestState?.StateVersion,
                StructuredState: structuredState,
                EstimatedPromptTokenCount: estimatedPromptTokens,
                TrimReason: trimReason,
                ReasonCodes: reasonCodes);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "Persistent conversation context build cancelled correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} stage={Stage} elapsedMs={ElapsedMs} cancellationRequested={CancellationRequested}",
                request.CorrelationId,
                request.ConversationThreadId,
                request.ConversationTurnId,
                stage,
                stopwatch.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    private static TaskContextBudgetOptions ResolveBudget(ConversationMemoryOptions memory, AITaskType taskType)
    {
        return taskType switch
        {
            AITaskType.UserChatSimple => memory.SimpleChat,
            AITaskType.UserChatComplex => memory.ComplexChat,
            AITaskType.MerchantInvestigation => memory.MerchantInvestigation,
            AITaskType.FinancialReasoning => memory.FinancialReasoning,
            _ => memory.Default
        };
    }

    private static FilterMessagesResult FilterMessages(
        IReadOnlyList<ConversationMessage> messages,
        ConversationStateSnapshot? state,
        int maxRecentMessages)
    {
        var reasonCodes = new List<string>();
        var activeTopic = state?.ActiveTopic;
        var excluded = new List<ConversationMessage>();
        var filtered = new List<ConversationMessage>(messages.Count);
        foreach (var message in messages.OrderBy(x => x.MessageOrder))
        {
            if (ShouldExcludeMessage(message, activeTopic))
            {
                excluded.Add(message);
                continue;
            }

            filtered.Add(message);
        }

        if (filtered.Count < messages.Count)
        {
            reasonCodes.Add("trimmed_low_value_messages");
        }

        var duplicateByRequest = filtered
            .Where(x => x.Role == ConversationMessageRole.User
                        && x.ConversationTurn is not null
                        && !string.IsNullOrWhiteSpace(x.ConversationTurn.ClientRequestId))
            .GroupBy(x => x.ConversationTurn!.ClientRequestId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        if (duplicateByRequest.Length > 0)
        {
            foreach (var group in duplicateByRequest)
            {
                foreach (var duplicate in group.OrderBy(x => x.MessageOrder).Skip(1))
                {
                    filtered.Remove(duplicate);
                    excluded.Add(duplicate);
                }
            }

            reasonCodes.Add("deduped_duplicate_request_messages");
        }

        var included = filtered.Count > maxRecentMessages
            ? filtered[^maxRecentMessages..]
            : filtered;
        if (filtered.Count > maxRecentMessages)
        {
            reasonCodes.Add("recent_window_applied");
            var removed = filtered.Take(filtered.Count - maxRecentMessages);
            excluded.AddRange(removed);
        }

        return new FilterMessagesResult(
            Included: included,
            Excluded: excluded.DistinctBy(x => x.Id).OrderBy(x => x.MessageOrder).ToArray(),
            ReasonCodes: reasonCodes);
    }

    private static bool ShouldExcludeMessage(ConversationMessage message, string? activeTopic)
    {
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return true;
        }

        if (!message.WasTrimEligible)
        {
            return false;
        }

        var normalized = NormalizeForDedup(message.Content);
        if (normalized.Length <= 12 && GreetingTokens.Any(x => normalized.Equals(x, StringComparison.Ordinal)))
        {
            return true;
        }

        if (message.IsResolved && message.Role != ConversationMessageRole.User)
        {
            return true;
        }

        if (message.Role == ConversationMessageRole.Assistant
            && message.ConversationTurn is not null
            && message.ConversationTurn.Status is ConversationTurnStatus.Received
                or ConversationTurnStatus.PersistedUserTurn
                or ConversationTurnStatus.ContextBuilt
                or ConversationTurnStatus.AIInProgress
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.TimedOut)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(activeTopic)
            && !string.IsNullOrWhiteSpace(message.Topic)
            && !message.Topic.Equals(activeTopic, StringComparison.OrdinalIgnoreCase)
            && message.IsResolved)
        {
            return true;
        }

        return false;
    }

    private static string NormalizeForDedup(string content)
    {
        return string.Join(' ', content.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyDictionary<string, string> BuildStructuredState(
        ConversationStateSnapshot? state,
        TaskContextBudgetOptions budget,
        int maxStateValueLength)
    {
        if (state is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddStateEntry(result, "active_topic", state.ActiveTopic, maxStateValueLength);
        AddStateEntry(result, "user_intent", state.UserIntent, maxStateValueLength);
        AddStateEntry(result, "budget_preference", state.BudgetPreference, maxStateValueLength);
        AddStateEntry(result, "location_preference", state.LocationPreference, maxStateValueLength);
        AddStateEntry(result, "merchant_subject", state.MerchantInvestigationSubject, maxStateValueLength);

        foreach (var constraint in state.Constraints.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Count >= budget.MaxStateEntries)
            {
                break;
            }

            AddStateEntry(result, $"constraint_{constraint.Key}", constraint.Value, maxStateValueLength);
        }

        foreach (var conclusion in state.RecentConclusions)
        {
            if (result.Count >= budget.MaxStateEntries)
            {
                break;
            }

            AddStateEntry(result, $"conclusion_{result.Count}", conclusion, maxStateValueLength);
        }

        return result;
    }

    private static void AddStateEntry(
        IDictionary<string, string> state,
        string key,
        string? value,
        int maxStateValueLength)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmedKey = key.Trim();
        var trimmedValue = value.Trim();
        if (trimmedValue.Length > maxStateValueLength)
        {
            trimmedValue = trimmedValue[..maxStateValueLength];
        }

        state[trimmedKey] = trimmedValue;
    }

    private static string? TrimSummary(string? summary, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var normalized = summary.Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..Math.Max(8, maxChars)].TrimEnd();
    }

    private static BudgetTrimResult TrimMessagesToBudget(
        IReadOnlyList<AIMessage> contextMessages,
        IReadOnlyList<ConversationMessage> persistedIncludedMessages,
        int maxPromptTokens,
        out string? trimReason)
    {
        var mutableMessages = contextMessages.ToList();
        var mutablePersisted = persistedIncludedMessages.ToList();
        var excludedByBudget = new List<ConversationMessage>();
        trimReason = null;

        if (maxPromptTokens <= 0)
        {
            return new BudgetTrimResult(mutableMessages, mutablePersisted, excludedByBudget);
        }

        while (EstimateTokens(mutableMessages) > maxPromptTokens && mutablePersisted.Count > 2)
        {
            var removableIndex = FindRemovableMessageIndex(mutablePersisted);
            if (removableIndex < 0)
            {
                break;
            }

            var removed = mutablePersisted[removableIndex];
            mutablePersisted.RemoveAt(removableIndex);
            excludedByBudget.Add(removed);

            var aiIndex = mutableMessages.FindIndex(x =>
                x.TimestampUtc == removed.CreatedUtc
                && x.Content.Equals(removed.Content, StringComparison.Ordinal)
                && x.Role == ToAIMessageRole(removed.Role));

            if (aiIndex >= 0)
            {
                mutableMessages.RemoveAt(aiIndex);
            }

            trimReason = "token_budget_trim_applied";
        }

        return new BudgetTrimResult(mutableMessages, mutablePersisted, excludedByBudget);
    }

    private static int FindRemovableMessageIndex(IReadOnlyList<ConversationMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].WasTrimEligible && messages[i].Role != ConversationMessageRole.User)
            {
                return i;
            }
        }

        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].WasTrimEligible)
            {
                return i;
            }
        }

        return messages.Count > 2 ? 0 : -1;
    }

    private static AIMessage ToAIMessage(ConversationMessage message)
        => new(ToAIMessageRole(message.Role), message.Content, message.CreatedUtc);

    private static AIMessageRole ToAIMessageRole(ConversationMessageRole role)
    {
        return role switch
        {
            ConversationMessageRole.System => AIMessageRole.System,
            ConversationMessageRole.User => AIMessageRole.User,
            ConversationMessageRole.Assistant => AIMessageRole.Assistant,
            ConversationMessageRole.Tool => AIMessageRole.Tool,
            _ => AIMessageRole.User
        };
    }

    private static int EstimateTokens(IReadOnlyList<AIMessage> messages)
    {
        var charCount = messages.Sum(x => x.Content.Length);
        var messageOverhead = messages.Count * 10;
        return Math.Max(1, (charCount / 4) + messageOverhead);
    }

    private async Task PersistBuildLogIfEnabledAsync(
        PersistentConversationContextBuildRequest request,
        int estimatedPromptTokenCount,
        string? trimReason,
        int includedRecentMessageCount,
        int? includedSummaryVersion,
        int? includedStateVersion,
        ConversationMemoryOptions memoryOptions,
        CancellationToken cancellationToken)
    {
        if (!memoryOptions.BuildContextLogsEnabled)
        {
            return;
        }

        var buildLog = new ConversationContextBuildLog
        {
            Id = Guid.NewGuid(),
            ConversationThreadId = request.ConversationThreadId,
            ConversationTurnId = request.ConversationTurnId,
            CorrelationId = request.CorrelationId,
            TaskType = request.TaskType.ToString(),
            ModelClass = request.ModelClass.ToString(),
            IncludedRecentMessageCount = includedRecentMessageCount,
            IncludedSummaryVersion = includedSummaryVersion,
            IncludedStateVersion = includedStateVersion,
            EstimatedPromptTokenCount = estimatedPromptTokenCount,
            TrimReason = trimReason,
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.ConversationContextBuildLogs.Add(buildLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureThreadOwnershipAsync(Guid userId, Guid conversationThreadId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.ConversationThreads
            .AsNoTracking()
            .AnyAsync(x => x.Id == conversationThreadId && x.UserId == userId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Conversation thread not found for user.");
        }
    }

    private sealed record FilterMessagesResult(
        IReadOnlyList<ConversationMessage> Included,
        IReadOnlyList<ConversationMessage> Excluded,
        IReadOnlyList<string> ReasonCodes);

    private sealed record BudgetTrimResult(
        IReadOnlyList<AIMessage> ContextMessages,
        IReadOnlyList<ConversationMessage> IncludedMessages,
        IReadOnlyList<ConversationMessage> ExcludedByBudget);
}
