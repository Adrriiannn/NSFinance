using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed record BankSyncExecutionLease(
    Guid UserId,
    Guid ConnectionId,
    string LeaseId);

public sealed class BankSyncExecutionLeaseStore(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<BankingSyncOptions> options)
{
    private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(
        Math.Clamp(options.Value.SyncExecutionLeaseSeconds, 30, 30 * 60));

    public TimeSpan LeaseDuration => _leaseDuration;

    public async Task<BankSyncExecutionLease?> TryAcquireAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseExpiresUtc = now.Add(_leaseDuration);

        if (IsInMemoryProvider())
        {
            var connection = await dbContext.OpenBankingConnections.SingleOrDefaultAsync(
                candidate => candidate.Id == connectionId && candidate.UserId == userId,
                cancellationToken);
            if (connection is null
                || (!string.IsNullOrWhiteSpace(connection.SyncLeaseId)
                    && connection.SyncLeaseExpiresUtc > now))
            {
                return null;
            }

            connection.SyncLeaseId = leaseId;
            connection.SyncLeaseExpiresUtc = leaseExpiresUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new BankSyncExecutionLease(userId, connectionId, leaseId);
        }

        var claimed = await BuildClaimQuery(userId, connectionId, now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(connection => connection.SyncLeaseId, leaseId)
                .SetProperty(connection => connection.SyncLeaseExpiresUtc, leaseExpiresUtc),
                cancellationToken);
        return claimed == 1
            ? new BankSyncExecutionLease(userId, connectionId, leaseId)
            : null;
    }

    public async Task<bool> RenewAsync(
        BankSyncExecutionLease lease,
        CancellationToken cancellationToken)
    {
        var leaseExpiresUtc = timeProvider.GetUtcNow().UtcDateTime.Add(_leaseDuration);
        if (IsInMemoryProvider())
        {
            var connection = await dbContext.OpenBankingConnections.SingleOrDefaultAsync(
                candidate => candidate.Id == lease.ConnectionId
                    && candidate.UserId == lease.UserId
                    && candidate.SyncLeaseId == lease.LeaseId,
                cancellationToken);
            if (connection is null)
            {
                return false;
            }

            connection.SyncLeaseExpiresUtc = leaseExpiresUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var renewed = await dbContext.OpenBankingConnections
            .Where(connection => connection.Id == lease.ConnectionId
                && connection.UserId == lease.UserId
                && connection.SyncLeaseId == lease.LeaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(connection => connection.SyncLeaseExpiresUtc, leaseExpiresUtc),
                cancellationToken);
        return renewed == 1;
    }

    public async Task<bool> ReleaseAsync(
        BankSyncExecutionLease lease,
        CancellationToken cancellationToken)
    {
        if (IsInMemoryProvider())
        {
            var connection = await dbContext.OpenBankingConnections.SingleOrDefaultAsync(
                candidate => candidate.Id == lease.ConnectionId
                    && candidate.UserId == lease.UserId
                    && candidate.SyncLeaseId == lease.LeaseId,
                cancellationToken);
            if (connection is null)
            {
                return false;
            }

            connection.SyncLeaseId = null;
            connection.SyncLeaseExpiresUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var released = await dbContext.OpenBankingConnections
            .Where(connection => connection.Id == lease.ConnectionId
                && connection.UserId == lease.UserId
                && connection.SyncLeaseId == lease.LeaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(connection => connection.SyncLeaseId, (string?)null)
                .SetProperty(connection => connection.SyncLeaseExpiresUtc, (DateTime?)null),
                cancellationToken);
        return released == 1;
    }

    internal IQueryable<OpenBankingConnection> BuildClaimQuery(
        Guid userId,
        Guid connectionId,
        DateTime nowUtc)
    {
        return dbContext.OpenBankingConnections
            .Where(connection => connection.Id == connectionId && connection.UserId == userId)
            .Where(connection => connection.SyncLeaseId == null
                || connection.SyncLeaseExpiresUtc == null
                || connection.SyncLeaseExpiresUtc <= nowUtc);
    }

    private bool IsInMemoryProvider()
    {
        return string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);
    }
}
