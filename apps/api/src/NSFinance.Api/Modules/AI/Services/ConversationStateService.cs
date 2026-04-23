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

        var state = NormalizeState(DeserializeState(persisted.StateJson));
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
        var baseState = latest?.State ?? CreateDefaultState();

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
        var schemaVersion = Math.Max(2, baseState.SchemaVersion);
        var activeMode = baseState.ActiveMode;
        var modeCandidate = baseState.ModeCandidate;
        var readinessLevel = baseState.ReadinessLevel;
        var inferredIntent = baseState.InferredIntent;
        var missingConstraints = (baseState.MissingConstraints ?? []).ToList();
        var conversationSignals = baseState.ConversationSignals;
        var lastClarificationPrompt = baseState.LastClarificationPrompt;
        var lastSuggestedOptions = (baseState.LastSuggestedOptions ?? []).ToList();
        var selectedEntityId = baseState.SelectedEntityId;
        var lastExecutionFingerprint = baseState.LastExecutionFingerprint;
        var topicStatus = baseState.TopicStatus;
        var transitionIntent = baseState.TransitionIntent;
        var confidence = baseState.Confidence;
        var needsFollowUp = baseState.NeedsFollowUp;
        var followUpBindingType = baseState.FollowUpBindingType;
        var resultContextRef = baseState.ResultContextRef;
        var loopGuards = baseState.LoopGuards ?? new ConversationLoopGuards();
        var pendingClarification = baseState.PendingClarification;

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
            else if (key.Equals("active_mode", StringComparison.OrdinalIgnoreCase)
                     && Enum.TryParse<ConversationMode>(value, true, out var parsedActiveMode))
            {
                activeMode = parsedActiveMode;
            }
            else if (key.Equals("mode_candidate", StringComparison.OrdinalIgnoreCase)
                     && Enum.TryParse<ConversationMode>(value, true, out var parsedModeCandidate))
            {
                modeCandidate = parsedModeCandidate;
            }
            else if (key.Equals("readiness_level", StringComparison.OrdinalIgnoreCase)
                     && Enum.TryParse<ConversationReadinessLevel>(value, true, out var parsedReadiness))
            {
                readinessLevel = parsedReadiness;
            }
            else if (key.Equals("inferred_intent", StringComparison.OrdinalIgnoreCase))
            {
                inferredIntent = value;
            }
            else if (key.Equals("missing_constraints", StringComparison.OrdinalIgnoreCase))
            {
                missingConstraints = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            else if (key.Equals("last_clarification_prompt", StringComparison.OrdinalIgnoreCase))
            {
                lastClarificationPrompt = value;
            }
            else if (key.Equals("last_suggested_options", StringComparison.OrdinalIgnoreCase))
            {
                lastSuggestedOptions = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            else if (key.Equals("selected_entity_id", StringComparison.OrdinalIgnoreCase))
            {
                selectedEntityId = value;
            }
            else if (key.Equals("last_execution_fingerprint", StringComparison.OrdinalIgnoreCase))
            {
                lastExecutionFingerprint = value;
            }
            else if (key.Equals("topic_status", StringComparison.OrdinalIgnoreCase))
            {
                topicStatus = value;
            }
            else if (key.Equals("transition_intent", StringComparison.OrdinalIgnoreCase))
            {
                transitionIntent = value;
            }
            else if (key.Equals("confidence", StringComparison.OrdinalIgnoreCase)
                     && double.TryParse(value, out var parsedConfidence))
            {
                confidence = parsedConfidence;
            }
            else if (key.Equals("needs_follow_up", StringComparison.OrdinalIgnoreCase)
                     && bool.TryParse(value, out var parsedNeedsFollowUp))
            {
                needsFollowUp = parsedNeedsFollowUp;
            }
            else if (key.Equals("follow_up_binding_type", StringComparison.OrdinalIgnoreCase)
                     && Enum.TryParse<FollowUpBindingType>(value, true, out var parsedBindingType))
            {
                followUpBindingType = parsedBindingType;
            }
            else if (key.Equals("result_context_active_result_set_id", StringComparison.OrdinalIgnoreCase)
                     && Guid.TryParse(value, out var resultSetId))
            {
                resultContextRef = (resultContextRef ?? new ConversationResultContextReference(null, null, null, null)) with
                {
                    ActiveResultSetId = resultSetId
                };
            }
            else if (key.Equals("result_context_branch_root_result_set_id", StringComparison.OrdinalIgnoreCase)
                     && Guid.TryParse(value, out var branchRootId))
            {
                resultContextRef = (resultContextRef ?? new ConversationResultContextReference(null, null, null, null)) with
                {
                    BranchRootResultSetId = branchRootId
                };
            }
            else if (key.Equals("loop_guard_same_clarification_count", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(value, out var clarificationCount))
            {
                loopGuards = loopGuards with
                {
                    SameClarificationIntentCount = clarificationCount
                };
            }
            else if (key.Equals("loop_guard_last_clarification_fingerprint", StringComparison.OrdinalIgnoreCase))
            {
                loopGuards = loopGuards with
                {
                    LastClarificationFingerprint = value
                };
            }
            else if (key.Equals("pending_clarification_slot", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<ClarificationSlot>(value, true, out var parsedSlot)
                    && parsedSlot != ClarificationSlot.None)
                {
                    pendingClarification = (pendingClarification ?? new PendingClarificationState(parsedSlot)) with
                    {
                        Slot = parsedSlot
                    };
                }
                else
                {
                    pendingClarification = null;
                }
            }
            else if (key.Equals("pending_clarification_prompt_intent", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingClarification is not null)
                {
                    pendingClarification = pendingClarification with
                    {
                        PromptIntent = value
                    };
                }
            }
            else if (key.Equals("pending_clarification_known_place_types", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingClarification is not null)
                {
                    pendingClarification = pendingClarification with
                    {
                        KnownPlaceTypes = value
                    };
                }
            }
            else if (key.Equals("pending_clarification_known_area", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingClarification is not null)
                {
                    pendingClarification = pendingClarification with
                    {
                        KnownArea = value
                    };
                }
            }
            else if (key.Equals("pending_clarification_known_time", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingClarification is not null)
                {
                    pendingClarification = pendingClarification with
                    {
                        KnownTime = value
                    };
                }
            }
            else if (key.Equals("pending_clarification_created_utc", StringComparison.OrdinalIgnoreCase)
                     && DateTimeOffset.TryParse(value, out var parsedCreatedAtUtc))
            {
                if (pendingClarification is not null)
                {
                    pendingClarification = pendingClarification with
                    {
                        CreatedAtUtc = parsedCreatedAtUtc
                    };
                }
            }
            else if (key.Equals("pending_clarification_clear", StringComparison.OrdinalIgnoreCase)
                     && bool.TryParse(value, out var clearPendingClarification)
                     && clearPendingClarification)
            {
                pendingClarification = null;
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
            RecentConclusions: conclusions.Take(8).ToArray(),
            SchemaVersion: schemaVersion,
            ActiveMode: activeMode,
            ModeCandidate: modeCandidate,
            ReadinessLevel: readinessLevel,
            InferredIntent: inferredIntent,
            MissingConstraints: missingConstraints,
            ConversationSignals: conversationSignals,
            LastClarificationPrompt: lastClarificationPrompt,
            LastSuggestedOptions: lastSuggestedOptions,
            LastSuggestedEntities: baseState.LastSuggestedEntities,
            SelectedEntityId: selectedEntityId,
            LastExecutionFingerprint: lastExecutionFingerprint,
            TopicStatus: topicStatus,
            TransitionIntent: transitionIntent,
            Confidence: confidence,
            NeedsFollowUp: needsFollowUp,
            FollowUpBindingType: followUpBindingType,
            ResultContextRef: resultContextRef,
            LoopGuards: loopGuards,
            PendingClarification: pendingClarification));
    }

    private static string SerializeState(ConversationStateSnapshot state)
        => JsonSerializer.Serialize(state, JsonOptions);

    private static ConversationStateSnapshot DeserializeState(string stateJson)
    {
        return JsonSerializer.Deserialize<ConversationStateSnapshot>(stateJson, JsonOptions)
            ?? CreateDefaultState();
    }

    private static ConversationStateSnapshot NormalizeState(ConversationStateSnapshot snapshot)
    {
        var constraints = snapshot.Constraints
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .Where(x => !x.Key.Contains("chat_location_latitude", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Contains("chat_location_longitude", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Contains("chat_location_captured_at_utc", StringComparison.OrdinalIgnoreCase))
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

        var missingConstraints = (snapshot.MissingConstraints ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lastSuggestedOptions = (snapshot.LastSuggestedOptions ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lastSuggestedEntities = (snapshot.LastSuggestedEntities ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.EntityId) && !string.IsNullOrWhiteSpace(x.Label))
            .GroupBy(x => x.EntityId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(entity => entity.Rank).First())
            .ToArray();

        PendingClarificationState? pendingClarification = snapshot.PendingClarification;
        if (pendingClarification?.Slot == ClarificationSlot.None)
        {
            pendingClarification = null;
        }

        ConversationResultContextReference? resultContextRef = snapshot.ResultContextRef;
        if (resultContextRef?.ExpiresUtc is DateTime expiresUtc && expiresUtc <= DateTime.UtcNow)
        {
            resultContextRef = null;
        }

        double? confidence = snapshot.Confidence.HasValue
            ? Math.Round(Math.Clamp(snapshot.Confidence.Value, 0d, 1d), 4, MidpointRounding.AwayFromZero)
            : null;

        return new ConversationStateSnapshot(
            ActiveTopic: string.IsNullOrWhiteSpace(snapshot.ActiveTopic) ? null : snapshot.ActiveTopic.Trim(),
            UserIntent: string.IsNullOrWhiteSpace(snapshot.UserIntent) ? null : snapshot.UserIntent.Trim(),
            Constraints: constraints,
            Summaries: summaries,
            BudgetPreference: string.IsNullOrWhiteSpace(snapshot.BudgetPreference) ? null : snapshot.BudgetPreference.Trim(),
            LocationPreference: string.IsNullOrWhiteSpace(snapshot.LocationPreference) ? null : snapshot.LocationPreference.Trim(),
            MerchantInvestigationSubject: string.IsNullOrWhiteSpace(snapshot.MerchantInvestigationSubject) ? null : snapshot.MerchantInvestigationSubject.Trim(),
            RecentConclusions: conclusions,
            SchemaVersion: Math.Max(2, snapshot.SchemaVersion),
            ActiveMode: snapshot.ActiveMode ?? ConversationMode.Conversation,
            ModeCandidate: snapshot.ModeCandidate ?? ConversationMode.Conversation,
            ReadinessLevel: snapshot.ReadinessLevel,
            InferredIntent: string.IsNullOrWhiteSpace(snapshot.InferredIntent) ? null : snapshot.InferredIntent.Trim(),
            MissingConstraints: missingConstraints,
            ConversationSignals: snapshot.ConversationSignals,
            LastClarificationPrompt: string.IsNullOrWhiteSpace(snapshot.LastClarificationPrompt) ? null : snapshot.LastClarificationPrompt.Trim(),
            LastSuggestedOptions: lastSuggestedOptions,
            LastSuggestedEntities: lastSuggestedEntities,
            SelectedEntityId: string.IsNullOrWhiteSpace(snapshot.SelectedEntityId) ? null : snapshot.SelectedEntityId.Trim(),
            LastExecutionFingerprint: string.IsNullOrWhiteSpace(snapshot.LastExecutionFingerprint) ? null : snapshot.LastExecutionFingerprint.Trim(),
            TopicStatus: string.IsNullOrWhiteSpace(snapshot.TopicStatus) ? null : snapshot.TopicStatus.Trim(),
            TransitionIntent: string.IsNullOrWhiteSpace(snapshot.TransitionIntent) ? null : snapshot.TransitionIntent.Trim(),
            Confidence: confidence,
            NeedsFollowUp: snapshot.NeedsFollowUp,
            FollowUpBindingType: snapshot.FollowUpBindingType,
            ResultContextRef: resultContextRef,
            LoopGuards: snapshot.LoopGuards ?? new ConversationLoopGuards(),
            PendingClarification: pendingClarification);
    }

    private static ConversationStateSnapshot CreateDefaultState()
    {
        return new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: null,
            MerchantInvestigationSubject: null,
            RecentConclusions: [],
            SchemaVersion: 2,
            ActiveMode: ConversationMode.Conversation,
            ModeCandidate: ConversationMode.Conversation,
            ReadinessLevel: ConversationReadinessLevel.R0_Unknown,
            InferredIntent: null,
            MissingConstraints: [],
            ConversationSignals: null,
            LastClarificationPrompt: null,
            LastSuggestedOptions: [],
            LastSuggestedEntities: [],
            SelectedEntityId: null,
            LastExecutionFingerprint: null,
            TopicStatus: null,
            TransitionIntent: null,
            Confidence: null,
            NeedsFollowUp: false,
            FollowUpBindingType: FollowUpBindingType.None,
            ResultContextRef: null,
            LoopGuards: new ConversationLoopGuards(),
            PendingClarification: null);
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
