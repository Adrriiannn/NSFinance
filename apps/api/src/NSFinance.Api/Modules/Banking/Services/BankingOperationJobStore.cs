using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public static class BankingOperationTypes
{
    public const string InitialSync = "initial_sync";
    public const string DisconnectCleanup = "disconnect_cleanup";
}

public static class BankingOperationJobStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retry = "retry";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed record BankingOperationJobLease(
    Guid JobId,
    Guid UserId,
    Guid ConnectionId,
    string OperationType,
    string LeaseId,
    int AttemptCount,
    int MaxAttempts);

public sealed record BankingOperationJobFailureOutcome(
    bool Found,
    bool WillRetry,
    int AttemptCount,
    DateTime? NextAttemptUtc);

public sealed record BankingOperationJobHealthSnapshot(
    int PendingCount,
    int ProcessingCount,
    int RetryCount,
    int FailedCount,
    int ExpiredLeaseCount,
    int OverdueCount,
    DateTime? OldestDueUtc);

public sealed class BankingOperationJobStore(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<BankingSyncOptions> options,
    ILogger<BankingOperationJobStore> logger)
{
    private const int MaximumBatchSize = 64;
    private readonly int _maxAttempts = Math.Clamp(options.Value.DurableJobMaxAttempts, 1, 20);
    private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(
        Math.Clamp(options.Value.DurableJobLeaseSeconds, 30, 30 * 60));

    public TimeSpan LeaseDuration => _leaseDuration;

    public async Task<bool> EnqueueAsync(
        Guid userId,
        Guid connectionId,
        string operationType,
        CancellationToken cancellationToken)
    {
        var ownsConnection = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .AnyAsync(
                connection => connection.Id == connectionId && connection.UserId == userId,
                cancellationToken);
        if (!ownsConnection)
        {
            logger.LogWarning(
                "Rejected banking operation enqueue because the connection was not owned operation={Operation}",
                operationType);
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await dbContext.BankingOperationJobs.SingleOrDefaultAsync(
            job => job.ConnectionId == connectionId && job.OperationType == operationType,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is BankingOperationJobStatuses.Pending
                or BankingOperationJobStatuses.Processing
                or BankingOperationJobStatuses.Retry)
            {
                if (existing.UserId != userId || existing.MaxAttempts != _maxAttempts)
                {
                    existing.UserId = userId;
                    existing.MaxAttempts = _maxAttempts;
                    existing.UpdatedUtc = now;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return true;
            }

            existing.UserId = userId;
            existing.Status = BankingOperationJobStatuses.Pending;
            existing.AttemptCount = 0;
            existing.MaxAttempts = _maxAttempts;
            existing.NextAttemptUtc = now;
            existing.LeaseId = null;
            existing.LeaseExpiresUtc = null;
            existing.LastFailureCode = null;
            existing.UpdatedUtc = now;
            existing.CompletedUtc = null;
            existing.FailedUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var job = new BankingOperationJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = connectionId,
            OperationType = operationType,
            Status = BankingOperationJobStatuses.Pending,
            AttemptCount = 0,
            MaxAttempts = _maxAttempts,
            NextAttemptUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        dbContext.BankingOperationJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.Entry(job).State = EntityState.Detached;
        }

        return true;
    }

    public async Task<IReadOnlyList<Guid>> ListDueJobIdsAsync(
        string operationType,
        int limit,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await BuildDueQuery(operationType, now)
            .Select(job => job.Id)
            .Take(Math.Clamp(limit, 1, MaximumBatchSize))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankingOperationJobHealthSnapshot> GetHealthSnapshotAsync(
        TimeSpan overdueAfter,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var overdueThresholdUtc = now - overdueAfter;
        var snapshot = await BuildHealthQuery(now, overdueThresholdUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return snapshot ?? new BankingOperationJobHealthSnapshot(0, 0, 0, 0, 0, 0, null);
    }

    internal IQueryable<BankingOperationJobHealthSnapshot> BuildHealthQuery(
        DateTime nowUtc,
        DateTime overdueThresholdUtc)
    {
        return dbContext.BankingOperationJobs
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new BankingOperationJobHealthSnapshot(
                group.Count(job => job.Status == BankingOperationJobStatuses.Pending),
                group.Count(job => job.Status == BankingOperationJobStatuses.Processing),
                group.Count(job => job.Status == BankingOperationJobStatuses.Retry),
                group.Count(job => job.Status == BankingOperationJobStatuses.Failed),
                group.Count(job => job.Status == BankingOperationJobStatuses.Processing
                    && job.LeaseExpiresUtc != null
                    && job.LeaseExpiresUtc <= nowUtc),
                group.Count(job =>
                    (job.Status == BankingOperationJobStatuses.Pending
                        || job.Status == BankingOperationJobStatuses.Retry)
                    && job.NextAttemptUtc <= overdueThresholdUtc),
                group.Where(job => job.Status == BankingOperationJobStatuses.Pending
                        || job.Status == BankingOperationJobStatuses.Retry)
                    .Select(job => (DateTime?)job.NextAttemptUtc)
                    .Min()));
    }

    internal IQueryable<BankingOperationJob> BuildDueQuery(string operationType, DateTime nowUtc)
    {
        return dbContext.BankingOperationJobs
            .AsNoTracking()
            .Where(job => job.OperationType == operationType)
            .Where(job => dbContext.OpenBankingConnections.Any(connection =>
                connection.Id == job.ConnectionId && connection.UserId == job.UserId))
            .Where(job =>
                ((job.Status == BankingOperationJobStatuses.Pending
                        || job.Status == BankingOperationJobStatuses.Retry)
                    && job.NextAttemptUtc <= nowUtc)
                || (job.Status == BankingOperationJobStatuses.Processing
                    && job.LeaseExpiresUtc != null
                    && job.LeaseExpiresUtc <= nowUtc))
            .OrderBy(job => job.NextAttemptUtc)
            .ThenBy(job => job.CreatedUtc)
            .ThenBy(job => job.Id);
    }

    public async Task<BankingOperationJobLease?> TryClaimAsync(
        Guid jobId,
        string operationType,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseExpiresUtc = now.Add(_leaseDuration);

        if (IsInMemoryProvider())
        {
            var tracked = await dbContext.BankingOperationJobs.SingleOrDefaultAsync(
                job => job.Id == jobId && job.OperationType == operationType,
                cancellationToken);
            if (tracked is null || !IsClaimable(tracked, now))
            {
                return null;
            }

            tracked.Status = BankingOperationJobStatuses.Processing;
            tracked.LeaseId = leaseId;
            tracked.LeaseExpiresUtc = leaseExpiresUtc;
            tracked.AttemptCount++;
            tracked.UpdatedUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToLease(tracked);
        }

        var claimed = await dbContext.BankingOperationJobs
            .Where(job => job.Id == jobId && job.OperationType == operationType)
            .Where(job =>
                ((job.Status == BankingOperationJobStatuses.Pending
                        || job.Status == BankingOperationJobStatuses.Retry)
                    && job.NextAttemptUtc <= now)
                || (job.Status == BankingOperationJobStatuses.Processing
                    && job.LeaseExpiresUtc != null
                    && job.LeaseExpiresUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, BankingOperationJobStatuses.Processing)
                .SetProperty(job => job.LeaseId, leaseId)
                .SetProperty(job => job.LeaseExpiresUtc, leaseExpiresUtc)
                .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                .SetProperty(job => job.UpdatedUtc, now),
                cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        var leased = await dbContext.BankingOperationJobs
            .AsNoTracking()
            .SingleAsync(job => job.Id == jobId && job.LeaseId == leaseId, cancellationToken);
        return ToLease(leased);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid jobId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseExpiresUtc = now.Add(_leaseDuration);
        if (IsInMemoryProvider())
        {
            var tracked = await dbContext.BankingOperationJobs.SingleOrDefaultAsync(
                job => job.Id == jobId
                    && job.Status == BankingOperationJobStatuses.Processing
                    && job.LeaseId == leaseId,
                cancellationToken);
            if (tracked is null)
            {
                return false;
            }

            tracked.LeaseExpiresUtc = leaseExpiresUtc;
            tracked.UpdatedUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var renewed = await dbContext.BankingOperationJobs
            .Where(job => job.Id == jobId
                && job.Status == BankingOperationJobStatuses.Processing
                && job.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.LeaseExpiresUtc, leaseExpiresUtc)
                .SetProperty(job => job.UpdatedUtc, now),
                cancellationToken);
        return renewed == 1;
    }

    public async Task<bool> MarkSucceededAsync(
        Guid jobId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        return await MarkTerminalAsync(
            jobId,
            leaseId,
            BankingOperationJobStatuses.Completed,
            null,
            cancellationToken);
    }

    public async Task<bool> MarkCancelledAsync(
        Guid jobId,
        string leaseId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        return await MarkTerminalAsync(
            jobId,
            leaseId,
            BankingOperationJobStatuses.Cancelled,
            failureCode,
            cancellationToken);
    }

    public async Task<BankingOperationJobFailureOutcome> MarkFailedAsync(
        Guid jobId,
        string leaseId,
        string failureCode,
        bool retryable,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.BankingOperationJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == jobId
                && candidate.Status == BankingOperationJobStatuses.Processing
                && candidate.LeaseId == leaseId,
            cancellationToken);
        if (job is null)
        {
            return new BankingOperationJobFailureOutcome(false, false, 0, null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var willRetry = retryable && job.AttemptCount < job.MaxAttempts;
        job.Status = willRetry
            ? BankingOperationJobStatuses.Retry
            : BankingOperationJobStatuses.Failed;
        job.NextAttemptUtc = willRetry
            ? now.Add(ComputeBackoff(job.AttemptCount))
            : now;
        job.LastFailureCode = NormalizeFailureCode(failureCode);
        job.LeaseId = null;
        job.LeaseExpiresUtc = null;
        job.UpdatedUtc = now;
        job.FailedUtc = willRetry ? null : now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new BankingOperationJobFailureOutcome(
            true,
            willRetry,
            job.AttemptCount,
            willRetry ? job.NextAttemptUtc : null);
    }

    internal static TimeSpan ComputeBackoff(int attemptCount)
    {
        var seconds = Math.Min(15 * 60, 15 * Math.Pow(2, Math.Max(0, attemptCount - 1)));
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<bool> MarkTerminalAsync(
        Guid jobId,
        string leaseId,
        string terminalStatus,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.BankingOperationJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == jobId
                && candidate.Status == BankingOperationJobStatuses.Processing
                && candidate.LeaseId == leaseId,
            cancellationToken);
        if (job is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        job.Status = terminalStatus;
        job.NextAttemptUtc = now;
        job.LeaseId = null;
        job.LeaseExpiresUtc = null;
        job.LastFailureCode = failureCode is null ? null : NormalizeFailureCode(failureCode);
        job.UpdatedUtc = now;
        job.CompletedUtc = terminalStatus == BankingOperationJobStatuses.Completed ? now : null;
        job.FailedUtc = terminalStatus == BankingOperationJobStatuses.Cancelled ? now : null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private bool IsInMemoryProvider()
    {
        return string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);
    }

    private static bool IsClaimable(BankingOperationJob job, DateTime nowUtc)
    {
        return ((job.Status == BankingOperationJobStatuses.Pending
                    || job.Status == BankingOperationJobStatuses.Retry)
                && job.NextAttemptUtc <= nowUtc)
            || (job.Status == BankingOperationJobStatuses.Processing
                && job.LeaseExpiresUtc.HasValue
                && job.LeaseExpiresUtc.Value <= nowUtc);
    }

    private static BankingOperationJobLease ToLease(BankingOperationJob job)
    {
        return new BankingOperationJobLease(
            job.Id,
            job.UserId,
            job.ConnectionId,
            job.OperationType,
            job.LeaseId!,
            job.AttemptCount,
            job.MaxAttempts);
    }

    private static string NormalizeFailureCode(string failureCode)
    {
        var normalized = string.IsNullOrWhiteSpace(failureCode)
            ? "banking_operation_failed"
            : failureCode.Trim().ToLowerInvariant();
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }
}
