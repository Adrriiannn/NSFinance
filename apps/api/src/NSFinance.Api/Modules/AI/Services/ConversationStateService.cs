using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationStateService(
    AppDbContext dbContext,
    IOptions<AIIntegrationOptions> options) : IConversationStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<PersistedConversationState?> GetLatestStateAsync(
        Guid userId,
        Guid conversationThreadId,
        CancellationToken cancellationToken)
    {
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);

        var persisted = await dbContext.ConversationStateSnapshots
            .AsNoTracking()
            .Where(x => x.ConversationThreadId == conversationThreadId)
            .OrderByDescending(x => x.StateVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (persisted is null)
        {
            return null;
        }

        var state = DeserializeState(persisted.StateJson);
        return new PersistedConversationState(state, persisted.StateVersion, persisted.CreatedUtc);
    }

    public async Task<PersistedConversationState> SaveSnapshotAsync(
        Guid userId,
        Guid conversationThreadId,
        ConversationStateSnapshot state,
        ConversationStateSnapshotReason reason,
        CancellationToken cancellationToken)
    {
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);
        var nextVersion = (await dbContext.ConversationStateSnapshots
                               .Where(x => x.ConversationThreadId == conversationThreadId)
                               .Select(x => (int?)x.StateVersion)
                               .MaxAsync(cancellationToken) ?? 0) + 1;

        var now = DateTime.UtcNow;
        var entity = new Persistence.Entities.ConversationStateSnapshot
        {
            Id = Guid.NewGuid(),
            ConversationThreadId = conversationThreadId,
            StateJson = SerializeState(NormalizeState(state)),
            StateVersion = nextVersion,
            Reason = reason,
            CreatedUtc = now
        };

        dbContext.ConversationStateSnapshots.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PersistedConversationState(DeserializeState(entity.StateJson), entity.StateVersion, entity.CreatedUtc);
    }

    public async Task<PersistedConversationState> MergeStateUpdatesAsync(
        Guid userId,
        Guid conversationThreadId,
        IReadOnlyDictionary<string, string> updates,
        ConversationStateSnapshotReason reason,
        CancellationToken cancellationToken)
    {
        var latest = await GetLatestStateAsync(userId, conversationThreadId, cancellationToken);
        var baseState = latest?.State ?? new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: null,
            MerchantInvestigationSubject: null,
            RecentConclusions: []);

        var merged = MergeState(baseState, updates, options.Value.Memory);
        return await SaveSnapshotAsync(userId, conversationThreadId, merged, reason, cancellationToken);
    }

    private static ConversationStateSnapshot MergeState(
        ConversationStateSnapshot baseState,
        IReadOnlyDictionary<string, string> updates,
        ConversationMemoryOptions memoryOptions)
    {
        var constraints = new Dictionary<string, string>(baseState.Constraints, StringComparer.OrdinalIgnoreCase);
        var conclusions = baseState.RecentConclusions.ToList();
        var summaries = baseState.Summaries.ToList();

        string? activeTopic = baseState.ActiveTopic;
        string? userIntent = baseState.UserIntent;
        string? budgetPreference = baseState.BudgetPreference;
        string? locationPreference = baseState.LocationPreference;
        string? merchantSubject = baseState.MerchantInvestigationSubject;

        foreach (var kvp in updates)
        {
            var key = kvp.Key.Trim();
            var value = kvp.Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (key.Equals("active_topic", StringComparison.OrdinalIgnoreCase))
            {
                activeTopic = value;
            }
            else if (key.Equals("user_intent", StringComparison.OrdinalIgnoreCase))
            {
                userIntent = value;
            }
            else if (key.Equals("budget_preference", StringComparison.OrdinalIgnoreCase))
            {
                budgetPreference = value;
            }
            else if (key.Equals("location_preference", StringComparison.OrdinalIgnoreCase))
            {
                locationPreference = value;
            }
            else if (key.Equals("merchant_subject", StringComparison.OrdinalIgnoreCase))
            {
                merchantSubject = value;
            }
            else if (key.StartsWith("constraint_", StringComparison.OrdinalIgnoreCase))
            {
                var constraintKey = key["constraint_".Length..];
                if (!string.IsNullOrWhiteSpace(constraintKey))
                {
                    constraints[constraintKey] = value;
                }
            }
            else if (key.Equals("recent_conclusions", StringComparison.OrdinalIgnoreCase))
            {
                conclusions = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            else if (key.Equals("conversation_summaries", StringComparison.OrdinalIgnoreCase))
            {
                summaries = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        return NormalizeState(new ConversationStateSnapshot(
            ActiveTopic: activeTopic,
            UserIntent: userIntent,
            Constraints: constraints.Take(memoryOptions.MaxStateEntries).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
            Summaries: summaries.Take(Math.Max(1, memoryOptions.MaxStateEntries)).ToArray(),
            BudgetPreference: budgetPreference,
            LocationPreference: locationPreference,
            MerchantInvestigationSubject: merchantSubject,
            RecentConclusions: conclusions.Take(8).ToArray()));
    }

    private static string SerializeState(ConversationStateSnapshot state)
        => JsonSerializer.Serialize(state, JsonOptions);

    private static ConversationStateSnapshot DeserializeState(string stateJson)
    {
        return JsonSerializer.Deserialize<ConversationStateSnapshot>(stateJson, JsonOptions)
            ?? new ConversationStateSnapshot(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], null, null, null, []);
    }

    private static ConversationStateSnapshot NormalizeState(ConversationStateSnapshot snapshot)
    {
        var constraints = snapshot.Constraints
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        var summaries = snapshot.Summaries
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var conclusions = snapshot.RecentConclusions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ConversationStateSnapshot(
            ActiveTopic: string.IsNullOrWhiteSpace(snapshot.ActiveTopic) ? null : snapshot.ActiveTopic.Trim(),
            UserIntent: string.IsNullOrWhiteSpace(snapshot.UserIntent) ? null : snapshot.UserIntent.Trim(),
            Constraints: constraints,
            Summaries: summaries,
            BudgetPreference: string.IsNullOrWhiteSpace(snapshot.BudgetPreference) ? null : snapshot.BudgetPreference.Trim(),
            LocationPreference: string.IsNullOrWhiteSpace(snapshot.LocationPreference) ? null : snapshot.LocationPreference.Trim(),
            MerchantInvestigationSubject: string.IsNullOrWhiteSpace(snapshot.MerchantInvestigationSubject) ? null : snapshot.MerchantInvestigationSubject.Trim(),
            RecentConclusions: conclusions);
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
}
