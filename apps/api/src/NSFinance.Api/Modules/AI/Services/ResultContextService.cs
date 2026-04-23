using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ResultContextService(
    AppDbContext dbContext,
    IOptions<AIIntegrationOptions> options,
    ILogger<ResultContextService> logger) : IResultContextService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<ResultContextReadResult> ReadAsync(
        ResultContextReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureThreadOwnershipAsync(request.UserId, request.ConversationThreadId, cancellationToken);

        var expiredBindingCleared = await DeleteExpiredAsync(request.ConversationThreadId, cancellationToken);
        var requestedResultSetId = ResolveRequestedResultSetId(request.ClientMetadata, request.State);
        ConversationResultContext? persisted = null;

        if (requestedResultSetId.HasValue)
        {
            persisted = await dbContext.ConversationResultContexts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ConversationThreadId == request.ConversationThreadId
                         && x.Id == requestedResultSetId.Value,
                    cancellationToken);
        }

        persisted ??= await dbContext.ConversationResultContexts
            .AsNoTracking()
            .Where(x => x.ConversationThreadId == request.ConversationThreadId)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var snapshot = persisted is null
            ? null
            : Deserialize(persisted.SnapshotJson);

        if (snapshot is not null && snapshot.ExpiresUtc <= DateTime.UtcNow)
        {
            snapshot = null;
            expiredBindingCleared = true;
        }

        var classification = ClassifyBinding(request.UserMessage, snapshot);
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requestedResultSetId.HasValue)
        {
            reasonCodes.Add("result_context_request_result_set_id_present");
        }

        if (snapshot is not null)
        {
            reasonCodes.Add("result_context_read");
        }

        if (expiredBindingCleared)
        {
            reasonCodes.Add("result_context_expired_binding_cleared");
        }

        return new ResultContextReadResult(
            ActiveResultContext: snapshot,
            BindingClassification: classification,
            UsedClientResultSetId: requestedResultSetId.HasValue && snapshot?.ResultSetId == requestedResultSetId.Value,
            ExpiredBindingCleared: expiredBindingCleared,
            ReasonCodes: reasonCodes.ToArray());
    }

    public async Task<ResultContextWriteResult> WriteAsync(
        ResultContextWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureThreadOwnershipAsync(request.UserId, request.ConversationThreadId, cancellationToken);
        await DeleteExpiredAsync(request.ConversationThreadId, cancellationToken);

        var nowUtc = request.CreatedUtc == default ? DateTime.UtcNow : request.CreatedUtc;
        var resultSetId = Guid.NewGuid();
        var branchRootId = request.BranchRootResultSetId
                           ?? request.ParentResultSetId
                           ?? resultSetId;
        var activeUntilUtc = nowUtc.AddMinutes(Math.Max(1, options.Value.Architecture.ResultContextActiveMinutes));
        var expiresUtc = nowUtc.AddHours(Math.Max(1, options.Value.Architecture.ResultContextPersistedHours));
        var snapshot = new ResultContextSnapshot(
            ResultSetId: resultSetId,
            ParentResultSetId: request.ParentResultSetId,
            BranchRootResultSetId: branchRootId,
            SourceMode: request.SourceMode,
            SourceSubtype: request.SourceSubtype,
            QueryFingerprint: request.QueryFingerprint,
            NormalizedConstraints: request.NormalizedConstraints,
            SuggestedEntities: request.SuggestedEntities,
            SelectedEntityId: request.SelectedEntityId,
            ActiveUntilUtc: activeUntilUtc,
            ExpiresUtc: expiresUtc,
            IsExpired: false,
            IsActiveWindowExpired: false);

        var entity = new ConversationResultContext
        {
            Id = resultSetId,
            ConversationThreadId = request.ConversationThreadId,
            ParentResultSetId = request.ParentResultSetId,
            BranchRootResultSetId = branchRootId,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            ActiveUntilUtc = activeUntilUtc,
            ExpiresUtc = expiresUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };

        dbContext.ConversationResultContexts.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Result context written threadId={ThreadId} resultSetId={ResultSetId} parentResultSetId={ParentResultSetId}",
            request.ConversationThreadId,
            resultSetId,
            request.ParentResultSetId);

        return new ResultContextWriteResult(
            Snapshot: snapshot,
            Reference: new ConversationResultContextReference(
                ActiveResultSetId: snapshot.ResultSetId,
                BranchRootResultSetId: snapshot.BranchRootResultSetId,
                ActiveUntilUtc: snapshot.ActiveUntilUtc,
                ExpiresUtc: snapshot.ExpiresUtc),
            ReasonCodes: ["result_context_write"]);
    }

    public async Task<ResultContextWriteResult?> TrySelectEntityAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid resultSetId,
        string selectedEntityId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);

        var entity = await dbContext.ConversationResultContexts
            .SingleOrDefaultAsync(
                x => x.ConversationThreadId == conversationThreadId
                     && x.Id == resultSetId,
                cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var snapshot = Deserialize(entity.SnapshotJson) with
        {
            SelectedEntityId = string.IsNullOrWhiteSpace(selectedEntityId) ? null : selectedEntityId.Trim(),
            IsExpired = entity.ExpiresUtc <= DateTime.UtcNow,
            IsActiveWindowExpired = entity.ActiveUntilUtc <= DateTime.UtcNow
        };
        entity.SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        entity.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ResultContextWriteResult(
            Snapshot: snapshot,
            Reference: new ConversationResultContextReference(
                ActiveResultSetId: snapshot.ResultSetId,
                BranchRootResultSetId: snapshot.BranchRootResultSetId,
                ActiveUntilUtc: snapshot.ActiveUntilUtc,
                ExpiresUtc: snapshot.ExpiresUtc),
            ReasonCodes: ["result_context_selected_entity_updated"]);
    }

    public async Task ClearExpiredBindingsAsync(
        Guid conversationThreadId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DeleteExpiredAsync(conversationThreadId, cancellationToken);
    }

    private async Task<bool> DeleteExpiredAsync(
        Guid conversationThreadId,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var expired = await dbContext.ConversationResultContexts
            .Where(x => x.ConversationThreadId == conversationThreadId && x.ExpiresUtc <= nowUtc)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return false;
        }

        dbContext.ConversationResultContexts.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Guid? ResolveRequestedResultSetId(
        IReadOnlyDictionary<string, string> metadata,
        ConversationStateSnapshot state)
    {
        var keys = new[] { "chat_result_set_id", "chat_ui_branch_id" };
        foreach (var key in keys)
        {
            if (TryReadGuid(metadata, key, out var parsed))
            {
                return parsed;
            }
        }

        return state.ResultContextRef?.ActiveResultSetId;
    }

    private static ResultContextBindingClassification ClassifyBinding(
        string userMessage,
        ResultContextSnapshot? snapshot)
    {
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var signals = ConversationSignalAnalyzer.Analyze(userMessage);
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(userMessage);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return snapshot is null
                ? ResultContextBindingClassification.None
                : ResultContextBindingClassification.BindPrior;
        }

        if (signals.HasTopicSwitchSignal)
        {
            return ResultContextBindingClassification.NewTopic;
        }

        if (snapshot is null)
        {
            return ResultContextBindingClassification.None;
        }

        if (signals.HasBranchingSignal)
        {
            return ResultContextBindingClassification.NewBranch;
        }

        if (signals.HasComparisonSignal)
        {
            return ResultContextBindingClassification.Refine;
        }

        var hasConstraintCue = extraction.PreferenceHints.Count > 0
                               || extraction.TimeHints.Count > 0
                               || ContainsAny(normalized, "shortlist", "short list", "top options", "filter");
        var wordCount = CountWords(normalized);
        var isCompactFollowUp = wordCount is > 0 and <= 8;
        if (hasConstraintCue
            && (signals.HasResultReferenceSignal
                || (isCompactFollowUp && extraction.PlaceTypeHints.Count == 0)))
        {
            return ResultContextBindingClassification.Refine;
        }

        if (signals.HasResultReferenceSignal)
        {
            return ResultContextBindingClassification.BindPrior;
        }

        return ResultContextBindingClassification.None;
    }

    private static int CountWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }

    private static ResultContextSnapshot Deserialize(string snapshotJson)
    {
        var snapshot = JsonSerializer.Deserialize<ResultContextSnapshot>(snapshotJson, JsonOptions)
                       ?? throw new InvalidOperationException("Invalid result context snapshot payload.");
        var nowUtc = DateTime.UtcNow;
        return snapshot with
        {
            IsExpired = snapshot.ExpiresUtc <= nowUtc,
            IsActiveWindowExpired = snapshot.ActiveUntilUtc <= nowUtc
        };
    }

    private static bool TryReadGuid(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out Guid result)
    {
        result = Guid.Empty;
        if (metadata.TryGetValue(key, out var exactValue)
            && Guid.TryParse(exactValue, out result))
        {
            return true;
        }

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(pair.Value, out result))
            {
                return true;
            }
        }

        return false;
    }

    private async Task EnsureThreadOwnershipAsync(
        Guid userId,
        Guid conversationThreadId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.ConversationThreads
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == conversationThreadId && x.UserId == userId,
                cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Conversation thread not found for user.");
        }
    }
}
