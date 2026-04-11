using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationThreadService(
    AppDbContext dbContext,
    ILogger<ConversationThreadService> logger) : IConversationThreadService
{
    public async Task<ConversationThread> CreateThreadAsync(Guid userId, string? title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;
        var thread = new ConversationThread
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Status = ConversationThreadStatus.Active,
            StartedUtc = now,
            LastMessageUtc = now,
            LastContextRefreshUtc = null,
            ActiveSummaryVersion = 0,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.ConversationThreads.Add(thread);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Conversation thread created threadId={ThreadId} userId={UserId}",
            thread.Id,
            userId);

        return thread;
    }

    public Task<ConversationThread?> GetThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
    {
        return dbContext.ConversationThreads
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == threadId && x.UserId == userId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationThread>> GetRecentThreadsAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        return await dbContext.ConversationThreads
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.LastMessageUtc)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task ArchiveThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await RequireOwnedThreadAsync(userId, threadId, cancellationToken);
        if (thread.Status == ConversationThreadStatus.Archived)
        {
            return;
        }

        thread.Status = ConversationThreadStatus.Archived;
        thread.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await RequireOwnedThreadAsync(userId, threadId, cancellationToken);
        if (thread.Status == ConversationThreadStatus.Closed)
        {
            return;
        }

        thread.Status = ConversationThreadStatus.Closed;
        thread.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchThreadAsync(Guid userId, Guid threadId, DateTime timestampUtc, CancellationToken cancellationToken)
    {
        var thread = await RequireOwnedThreadAsync(userId, threadId, cancellationToken);
        thread.LastMessageUtc = timestampUtc;
        thread.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ConversationThread> RequireOwnedThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await dbContext.ConversationThreads
            .SingleOrDefaultAsync(
                x => x.Id == threadId && x.UserId == userId,
                cancellationToken);

        if (thread is null)
        {
            throw new InvalidOperationException("Conversation thread not found for user.");
        }

        return thread;
    }
}
