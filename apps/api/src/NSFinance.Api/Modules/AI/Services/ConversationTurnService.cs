using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationTurnService(
    AppDbContext dbContext,
    ILogger<ConversationTurnService> logger) : IConversationTurnService
{
    public async Task<ConversationTurnStartResult> StartOrGetAsync(
        Guid userId,
        Guid conversationThreadId,
        string clientRequestId,
        string correlationId,
        AITaskType taskType,
        AIModelClass modelClass,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId))
        {
            throw new ArgumentException("Client request id is required for idempotent turn handling.", nameof(clientRequestId));
        }

        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);

        var normalizedClientRequestId = clientRequestId.Trim();
        var existing = await dbContext.ConversationTurns
            .SingleOrDefaultAsync(
                x => x.ConversationThreadId == conversationThreadId
                     && x.ClientRequestId == normalizedClientRequestId,
                cancellationToken);

        if (existing is not null)
        {
            existing.WasDeduplicated = true;
            existing.AttemptCount += 1;
            existing.UpdatedUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Conversation turn deduped threadId={ThreadId} turnId={TurnId} status={Status} clientRequestId={ClientRequestId}",
                conversationThreadId,
                existing.Id,
                existing.Status,
                normalizedClientRequestId);

            return new ConversationTurnStartResult(existing, IsDuplicateRequest: true, IsNewTurn: false);
        }

        var now = DateTime.UtcNow;
        var turn = new ConversationTurn
        {
            Id = Guid.NewGuid(),
            ConversationThreadId = conversationThreadId,
            ClientRequestId = normalizedClientRequestId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? normalizedClientRequestId : correlationId.Trim(),
            TaskType = taskType.ToString(),
            ModelClass = modelClass.ToString(),
            Status = ConversationTurnStatus.Received,
            ContextSource = "none",
            StartedUtc = now,
            UpdatedUtc = now,
            AttemptCount = 1
        };

        dbContext.ConversationTurns.Add(turn);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsClientRequestUniqueViolation(ex))
        {
            var deduped = await dbContext.ConversationTurns
                .SingleAsync(
                    x => x.ConversationThreadId == conversationThreadId
                         && x.ClientRequestId == normalizedClientRequestId,
                    cancellationToken);

            deduped.WasDeduplicated = true;
            deduped.AttemptCount += 1;
            deduped.UpdatedUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                ex,
                "Conversation turn unique race handled via dedupe threadId={ThreadId} turnId={TurnId}",
                conversationThreadId,
                deduped.Id);

            return new ConversationTurnStartResult(deduped, IsDuplicateRequest: true, IsNewTurn: false);
        }

        return new ConversationTurnStartResult(turn, IsDuplicateRequest: false, IsNewTurn: true);
    }

    public async Task<ConversationTurn?> GetTurnAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);
        return await dbContext.ConversationTurns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == turnId && x.ConversationThreadId == conversationThreadId,
                cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkPersistedUserTurnAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        Guid userMessageId,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.PersistedUserTurn,
            turn =>
            {
                turn.UserMessageId = userMessageId;
            },
            cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkContextBuiltAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string contextSource,
        int estimatedPromptTokens,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.ContextBuilt,
            turn =>
            {
                turn.ContextSource = string.IsNullOrWhiteSpace(contextSource) ? "unknown" : contextSource.Trim();
                turn.EstimatedPromptTokenCount = Math.Max(1, estimatedPromptTokens);
            },
            cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkAIInProgressAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        AIModelRoute route,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.AIInProgress,
            turn =>
            {
                turn.ModelClass = route.ModelClass.ToString();
                turn.ModelUsed = route.Model;
                turn.ModelDeployment = route.Deployment;
            },
            cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkAICompletedAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        long responseLatencyMs,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.AICompleted,
            turn =>
            {
                turn.ResponseLatencyMs = Math.Max(0, responseLatencyMs);
            },
            cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkPersistedAssistantTurnAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        Guid assistantMessageId,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.PersistedAssistantTurn,
            turn =>
            {
                turn.AssistantMessageId = assistantMessageId;
            },
            cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkCompletedAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.Completed,
            _ => { },
            cancellationToken);
    }

    public Task<ConversationTurnTransitionResult> MarkFailedAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.Failed,
            turn =>
            {
                turn.FailureCode = NormalizeFailureCode(failureCode);
                turn.FailureReason = NormalizeFailureReason(failureReason);
            },
            cancellationToken,
            allowFromTerminal: false);
    }

    public Task<ConversationTurnTransitionResult> MarkTimedOutAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.TimedOut,
            turn =>
            {
                turn.FailureCode = NormalizeFailureCode(failureCode);
                turn.FailureReason = NormalizeFailureReason(failureReason);
            },
            cancellationToken,
            allowFromTerminal: false);
    }

    public Task<ConversationTurnTransitionResult> MarkCancelledAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            userId,
            conversationThreadId,
            turnId,
            ConversationTurnStatus.Cancelled,
            turn =>
            {
                turn.FailureCode = NormalizeFailureCode(failureCode);
                turn.FailureReason = NormalizeFailureReason(failureReason);
            },
            cancellationToken,
            allowFromTerminal: false);
    }

    private async Task<ConversationTurnTransitionResult> TransitionAsync(
        Guid userId,
        Guid conversationThreadId,
        Guid turnId,
        ConversationTurnStatus targetStatus,
        Action<ConversationTurn> mutate,
        CancellationToken cancellationToken,
        bool allowFromTerminal = true)
    {
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);

        var turn = await dbContext.ConversationTurns
            .SingleOrDefaultAsync(
                x => x.Id == turnId && x.ConversationThreadId == conversationThreadId,
                cancellationToken);

        if (turn is null)
        {
            throw new InvalidOperationException("Conversation turn not found.");
        }

        var previousStatus = turn.Status;
        if (previousStatus == targetStatus)
        {
            return new ConversationTurnTransitionResult(turn, previousStatus, turn.Status);
        }

        if (IsTerminal(previousStatus) && !allowFromTerminal)
        {
            return new ConversationTurnTransitionResult(turn, previousStatus, turn.Status);
        }

        if (!IsTransitionAllowed(previousStatus, targetStatus))
        {
            throw new InvalidOperationException($"Invalid conversation turn transition {previousStatus} -> {targetStatus}.");
        }

        mutate(turn);
        var now = DateTime.UtcNow;
        turn.Status = targetStatus;
        turn.UpdatedUtc = now;
        if (targetStatus == ConversationTurnStatus.Completed)
        {
            turn.CompletedUtc = now;
        }
        else if (targetStatus == ConversationTurnStatus.Cancelled)
        {
            turn.CancelledUtc = now;
        }
        else if (targetStatus == ConversationTurnStatus.TimedOut)
        {
            turn.TimedOutUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Conversation turn transition threadId={ThreadId} turnId={TurnId} from={FromStatus} to={ToStatus}",
            conversationThreadId,
            turnId,
            previousStatus,
            targetStatus);

        return new ConversationTurnTransitionResult(turn, previousStatus, turn.Status);
    }

    private static bool IsTerminal(ConversationTurnStatus status)
    {
        return status is ConversationTurnStatus.Completed
            or ConversationTurnStatus.Cancelled
            or ConversationTurnStatus.Failed
            or ConversationTurnStatus.TimedOut;
    }

    private static bool IsTransitionAllowed(ConversationTurnStatus current, ConversationTurnStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return current switch
        {
            ConversationTurnStatus.Received => target is ConversationTurnStatus.PersistedUserTurn
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.TimedOut,
            ConversationTurnStatus.PersistedUserTurn => target is ConversationTurnStatus.ContextBuilt
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.TimedOut,
            ConversationTurnStatus.ContextBuilt => target is ConversationTurnStatus.AIInProgress
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.TimedOut,
            ConversationTurnStatus.AIInProgress => target is ConversationTurnStatus.AICompleted
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.TimedOut,
            ConversationTurnStatus.AICompleted => target is ConversationTurnStatus.PersistedAssistantTurn
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.TimedOut,
            ConversationTurnStatus.PersistedAssistantTurn => target is ConversationTurnStatus.Completed
                or ConversationTurnStatus.Cancelled
                or ConversationTurnStatus.Failed
                or ConversationTurnStatus.TimedOut,
            ConversationTurnStatus.Completed => false,
            ConversationTurnStatus.Cancelled => false,
            ConversationTurnStatus.Failed => false,
            ConversationTurnStatus.TimedOut => false,
            _ => false
        };
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

    private static bool IsClientRequestUniqueViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_ConversationTurns_ConversationThreadId_ClientRequestId", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFailureCode(string failureCode)
        => string.IsNullOrWhiteSpace(failureCode) ? "unknown_failure" : failureCode.Trim()[..Math.Min(80, failureCode.Trim().Length)];

    private static string NormalizeFailureReason(string failureReason)
        => string.IsNullOrWhiteSpace(failureReason) ? "No failure reason provided." : failureReason.Trim()[..Math.Min(512, failureReason.Trim().Length)];
}
