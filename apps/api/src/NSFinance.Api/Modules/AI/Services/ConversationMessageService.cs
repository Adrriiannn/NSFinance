using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationMessageService(
    AppDbContext dbContext) : IConversationMessageService
{
    public async Task<ConversationMessage> AppendMessageAsync(
        Guid userId,
        Guid conversationThreadId,
        ConversationMessageAppendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var thread = await dbContext.ConversationThreads
            .SingleOrDefaultAsync(
                x => x.Id == conversationThreadId && x.UserId == userId,
                cancellationToken);

        if (thread is null)
        {
            throw new InvalidOperationException("Conversation thread not found for user.");
        }

        if (thread.Status == ConversationThreadStatus.Closed)
        {
            throw new InvalidOperationException("Cannot append messages to closed conversation thread.");
        }

        var nextOrder = await dbContext.ConversationMessages
            .Where(x => x.ConversationThreadId == conversationThreadId)
            .Select(x => (int?)x.MessageOrder)
            .MaxAsync(cancellationToken) ?? 0;
        nextOrder += 1;

        var now = DateTime.UtcNow;
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationThreadId = conversationThreadId,
            Role = request.Role,
            Content = request.Content.Trim(),
            MessageOrder = nextOrder,
            Topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim(),
            ModelUsed = string.IsNullOrWhiteSpace(request.ModelUsed) ? null : request.ModelUsed.Trim(),
            TaskType = string.IsNullOrWhiteSpace(request.TaskType) ? null : request.TaskType.Trim(),
            IsResolved = request.IsResolved,
            WasTrimEligible = request.WasTrimEligible,
            WasSummaryDerived = request.WasSummaryDerived,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId.Trim(),
            CreatedUtc = now
        };

        dbContext.ConversationMessages.Add(message);
        thread.LastMessageUtc = now;
        thread.UpdatedUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid userId,
        Guid conversationThreadId,
        int take,
        CancellationToken cancellationToken)
    {
        var safeTake = Math.Clamp(take, 1, 400);
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);

        var recent = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(x => x.ConversationThreadId == conversationThreadId)
            .OrderByDescending(x => x.MessageOrder)
            .Take(safeTake)
            .ToListAsync(cancellationToken);

        return recent.OrderBy(x => x.MessageOrder).ToArray();
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesRangeAsync(
        Guid userId,
        Guid conversationThreadId,
        int startOrder,
        int endOrder,
        CancellationToken cancellationToken)
    {
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);
        var safeStart = Math.Max(1, startOrder);
        var safeEnd = Math.Max(safeStart, endOrder);

        return await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(x => x.ConversationThreadId == conversationThreadId
                        && x.MessageOrder >= safeStart
                        && x.MessageOrder <= safeEnd)
            .OrderBy(x => x.MessageOrder)
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureThreadOwnershipAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.ConversationThreads
            .AsNoTracking()
            .AnyAsync(x => x.Id == threadId && x.UserId == userId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Conversation thread not found for user.");
        }
    }
}
