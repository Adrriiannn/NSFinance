using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using System.Diagnostics;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationSummaryService(
    AppDbContext dbContext,
    IOptions<AIIntegrationOptions> options,
    IConversationSummaryGenerator summaryGenerator,
    ILogger<ConversationSummaryService>? logger = null) : IConversationSummaryService
{
    private readonly ILogger<ConversationSummaryService> _logger =
        logger ?? NullLogger<ConversationSummaryService>.Instance;

    public async Task<ConversationSummary?> GetLatestSummaryAsync(Guid userId, Guid conversationThreadId, CancellationToken cancellationToken)
    {
        await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);
        return await dbContext.ConversationSummaries
            .AsNoTracking()
            .Where(x => x.ConversationThreadId == conversationThreadId)
            .OrderByDescending(x => x.SummaryVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ConversationSummaryRefreshResult> RefreshSummaryIfNeededAsync(
        Guid userId,
        Guid conversationThreadId,
        AITaskType taskType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var stage = "start";
        _logger.LogInformation(
            "Conversation summary refresh start correlationId={CorrelationId} threadId={ThreadId} taskType={TaskType} cancellationRequested={CancellationRequested}",
            correlationId,
            conversationThreadId,
            taskType,
            cancellationToken.IsCancellationRequested);

        try
        {
            stage = "ensure_thread_ownership";
            await EnsureThreadOwnershipAsync(userId, conversationThreadId, cancellationToken);

            var reasonCodes = new List<string>();
            var memory = options.Value.Memory;

            stage = "load_latest_summary";
            var latestSummary = await dbContext.ConversationSummaries
                .Where(x => x.ConversationThreadId == conversationThreadId)
                .OrderByDescending(x => x.SummaryVersion)
                .FirstOrDefaultAsync(cancellationToken);

            stage = "load_message_max_order";
            var maxOrder = await dbContext.ConversationMessages
                .Where(x => x.ConversationThreadId == conversationThreadId)
                .Select(x => (int?)x.MessageOrder)
                .MaxAsync(cancellationToken) ?? 0;

            stage = "load_message_total_count";
            var totalCount = await dbContext.ConversationMessages
                .CountAsync(x => x.ConversationThreadId == conversationThreadId, cancellationToken);

            var sinceLastCount = latestSummary is null
                ? totalCount
                : Math.Max(0, maxOrder - latestSummary.MessageEndOrder);

            stage = "load_messages_since_last_summary";
            var sinceLastMessages = sinceLastCount == 0
                ? []
                : await dbContext.ConversationMessages
                    .AsNoTracking()
                    .Where(x => x.ConversationThreadId == conversationThreadId)
                    .Where(x => x.MessageOrder > (latestSummary == null ? 0 : latestSummary.MessageEndOrder))
                    .OrderBy(x => x.MessageOrder)
                    .Take(400)
                    .ToListAsync(cancellationToken);

            var estimatedTokens = EstimateTokens(sinceLastMessages);
            var shouldRefresh = false;
            if (latestSummary is null && totalCount >= memory.SummaryRefreshMessageThreshold)
            {
                shouldRefresh = true;
                reasonCodes.Add("refresh_missing_initial_summary");
            }
            else if (sinceLastCount >= memory.SummaryRefreshMessageDeltaThreshold)
            {
                shouldRefresh = true;
                reasonCodes.Add("refresh_message_delta_threshold");
            }
            else if (estimatedTokens >= memory.SummaryRefreshTokenEstimateThreshold)
            {
                shouldRefresh = true;
                reasonCodes.Add("refresh_token_threshold");
            }

            if (!shouldRefresh)
            {
                reasonCodes.Add("summary_refresh_not_required");
                stopwatch.Stop();
                _logger.LogInformation(
                    "Conversation summary refresh skipped correlationId={CorrelationId} threadId={ThreadId} stage={Stage} elapsedMs={ElapsedMs} reasonCodes={ReasonCodes}",
                    correlationId,
                    conversationThreadId,
                    stage,
                    stopwatch.ElapsedMilliseconds,
                    string.Join(',', reasonCodes));
                return new ConversationSummaryRefreshResult(
                    Refreshed: false,
                    MessageCount: totalCount,
                    MessagesSinceLastSummary: sinceLastCount,
                    EstimatedTokenCount: estimatedTokens,
                    LatestSummary: latestSummary,
                    ReasonCodes: reasonCodes);
            }

            if (sinceLastMessages.Count == 0)
            {
                reasonCodes.Add("summary_refresh_skipped_no_new_messages");
                stopwatch.Stop();
                _logger.LogInformation(
                    "Conversation summary refresh skipped_no_new_messages correlationId={CorrelationId} threadId={ThreadId} stage={Stage} elapsedMs={ElapsedMs}",
                    correlationId,
                    conversationThreadId,
                    stage,
                    stopwatch.ElapsedMilliseconds);
                return new ConversationSummaryRefreshResult(
                    Refreshed: false,
                    MessageCount: totalCount,
                    MessagesSinceLastSummary: sinceLastCount,
                    EstimatedTokenCount: estimatedTokens,
                    LatestSummary: latestSummary,
                    ReasonCodes: reasonCodes);
            }

            stage = "generate_summary";
            var summaryText = summaryGenerator.GenerateSummary(
                sinceLastMessages,
                latestSummary?.SummaryText,
                memory.MaxSummaryLengthChars);

            var now = DateTime.UtcNow;
            var summary = new ConversationSummary
            {
                Id = Guid.NewGuid(),
                ConversationThreadId = conversationThreadId,
                SummaryText = summaryText,
                SummaryScope = latestSummary is null ? ConversationSummaryScope.FullThreadToPoint : ConversationSummaryScope.PartialWindow,
                MessageStartOrder = latestSummary?.MessageEndOrder + 1 ?? 1,
                MessageEndOrder = maxOrder,
                SummaryVersion = (latestSummary?.SummaryVersion ?? 0) + 1,
                CreatedUtc = now
            };

            dbContext.ConversationSummaries.Add(summary);

            stage = "load_thread_for_summary_update";
            var thread = await dbContext.ConversationThreads
                .SingleAsync(x => x.Id == conversationThreadId && x.UserId == userId, cancellationToken);
            thread.ActiveSummaryVersion = summary.SummaryVersion;
            thread.LastContextRefreshUtc = now;
            thread.UpdatedUtc = now;

            stage = "persist_summary_changes";
            await dbContext.SaveChangesAsync(cancellationToken);
            reasonCodes.Add("summary_refreshed");
            stopwatch.Stop();
            _logger.LogInformation(
                "Conversation summary refresh completed correlationId={CorrelationId} threadId={ThreadId} stage={Stage} elapsedMs={ElapsedMs} summaryVersion={SummaryVersion}",
                correlationId,
                conversationThreadId,
                stage,
                stopwatch.ElapsedMilliseconds,
                summary.SummaryVersion);

            return new ConversationSummaryRefreshResult(
                Refreshed: true,
                MessageCount: totalCount,
                MessagesSinceLastSummary: sinceLastCount,
                EstimatedTokenCount: estimatedTokens,
                LatestSummary: summary,
                ReasonCodes: reasonCodes);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Conversation summary refresh cancelled correlationId={CorrelationId} threadId={ThreadId} stage={Stage} elapsedMs={ElapsedMs} cancellationRequested={CancellationRequested}",
                correlationId,
                conversationThreadId,
                stage,
                stopwatch.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    private static int EstimateTokens(IReadOnlyList<ConversationMessage> messages)
    {
        var charCount = messages.Sum(x => x.Content.Length);
        var messageOverhead = messages.Count * 8;
        return Math.Max(1, (charCount / 4) + messageOverhead);
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
