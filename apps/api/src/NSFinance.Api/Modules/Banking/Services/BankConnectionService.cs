using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankConnectionService(
    AppDbContext dbContext,
    IAuditService auditService)
{
    private static readonly string[] UserVisibleActiveStatuses =
    [
        BankConnectionStatuses.ConnectedPendingSync,
        BankConnectionStatuses.Connected,
        BankConnectionStatuses.SyncPending,
        BankConnectionStatuses.Synced
    ];

    private static readonly string[] UserVisibleAttentionStatuses =
    [
        BankConnectionStatuses.ReauthRequired,
        BankConnectionStatuses.Expired
    ];
    public async Task<OpenBankingConnection> CreateConnectionStartedAsync(
        Guid userId,
        string providerName,
        string providerEnvironment,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var connection = new OpenBankingConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProviderName = providerName,
            ProviderEnvironment = providerEnvironment,
            Status = BankConnectionStatuses.ConnectionStarted,
            AuthStateNonce = CreateStateNonce(),
            AuthStateExpiresUtc = now.AddMinutes(15),
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.OpenBankingConnections.Add(connection);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "bank_connection_started",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                provider = providerName,
                providerEnvironment,
                status = connection.Status
            },
            cancellationToken);

        return connection;
    }

    public async Task<OpenBankingConnection?> FindConnectionByStateAsync(
        string state,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await dbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleOrDefaultAsync(
                x => x.AuthStateNonce == state
                     && x.AuthStateExpiresUtc != null
                     && x.AuthStateExpiresUtc > now,
                cancellationToken);
    }

    public async Task<OpenBankingConnection?> FindConnectionForUserAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleOrDefaultAsync(
                x => x.Id == connectionId && x.UserId == userId,
                cancellationToken);
    }

    public async Task<ServiceResult<OpenBankingConnection>> GetConnectionForSyncAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.OpenBankingConnections
            .Include(x => x.Token)
            .Include(x => x.LinkedAccounts)
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return ServiceResult<OpenBankingConnection>.Fail(
                "Connection not found.",
                "bank_connection_not_found",
                StatusCodes.Status404NotFound);
        }

        return ServiceResult<OpenBankingConnection>.Ok(connection);
    }

    public async Task<IReadOnlyList<BankConnectionDto>> ListConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new BankConnectionDto(
                x.Id,
                x.ProviderName,
                x.ProviderEnvironment,
                x.ProviderDisplayName,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankConnectionDto?> GetConnectionSummaryAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Id == connectionId)
            .Select(x => new BankConnectionDto(
                x.Id,
                x.ProviderName,
                x.ProviderEnvironment,
                x.ProviderDisplayName,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ConnectedBanksOverviewDto> ListUserVisibleConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var includedStatuses = UserVisibleActiveStatuses
            .Concat(UserVisibleAttentionStatuses)
            .ToArray();

        var projected = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId && includedStatuses.Contains(x.Status))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new UserVisibleConnectionCandidate(
                new BankConnectionDto(
                    x.Id,
                    x.ProviderName,
                    x.ProviderEnvironment,
                    x.ProviderDisplayName,
                    x.Status,
                    x.CreatedUtc,
                    x.UpdatedUtc,
                    x.LastSuccessfulSyncUtc,
                    x.LastSyncAttemptedUtc,
                    x.LastErrorCode),
                x.ProviderConnectionReference,
                x.ProviderDisplayName,
                x.ProviderName,
                x.ProviderEnvironment))
            .ToListAsync(cancellationToken);

        var activeCandidates = projected
            .Where(x => UserVisibleActiveStatuses.Contains(x.Summary.Status))
            .OrderByDescending(x => x.Summary.UpdatedUtc)
            .ToList();
        var active = DeduplicateUserVisibleConnections(activeCandidates);
        var activeKeys = activeCandidates
            .Select(BuildUserVisibleDedupKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attention = DeduplicateUserVisibleConnections(
            projected.Where(x =>
                UserVisibleAttentionStatuses.Contains(x.Summary.Status)
                && !activeKeys.Contains(BuildUserVisibleDedupKey(x))));

        return new ConnectedBanksOverviewDto(active, attention);
    }

    public async Task<IReadOnlyList<LinkedBankAccountDto>> ListLinkedAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedAccounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Include(x => x.Connection)
            .Where(x => x.Connection != null && x.Connection.UserId == userId)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var accountIds = linkedAccounts.Select(x => x.Id).ToList();
        var latestBalances = await dbContext.BankBalanceSnapshots
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.LinkedBankAccountId))
            .GroupBy(x => x.LinkedBankAccountId)
            .Select(g => g.OrderByDescending(x => x.CapturedUtc).First())
            .ToDictionaryAsync(x => x.LinkedBankAccountId, cancellationToken);

        return linkedAccounts
            .Select(account =>
            {
                latestBalances.TryGetValue(account.Id, out var latestBalance);
                return new LinkedBankAccountDto(
                    account.Id,
                    account.ConnectionId,
                    account.ProviderAccountId,
                    account.DisplayName,
                    account.AccountType,
                    account.AccountSubType,
                    account.Currency,
                    account.CurrentConnectionHealth,
                    latestBalance?.Available,
                    latestBalance?.Current,
                    latestBalance?.Overdraft,
                    account.CreatedUtc,
                    account.UpdatedUtc);
            })
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<BankBalanceSnapshotDto>>> GetLatestBalancesAsync(
        Guid userId,
        Guid linkedBankAccountId,
        CancellationToken cancellationToken)
    {
        var ownsAccount = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Include(x => x.Connection)
            .AnyAsync(
                x => x.Id == linkedBankAccountId
                     && x.Connection != null
                     && x.Connection.UserId == userId,
                cancellationToken);

        if (!ownsAccount)
        {
            return ServiceResult<IReadOnlyList<BankBalanceSnapshotDto>>.Fail(
                "Account not found.",
                "bank_account_not_found",
                StatusCodes.Status404NotFound);
        }

        var balances = await dbContext.BankBalanceSnapshots
            .AsNoTracking()
            .Where(x => x.LinkedBankAccountId == linkedBankAccountId)
            .OrderByDescending(x => x.CapturedUtc)
            .Take(20)
            .Select(x => new BankBalanceSnapshotDto(
                x.Id,
                x.LinkedBankAccountId,
                x.Available,
                x.Current,
                x.Overdraft,
                x.Currency,
                x.CapturedUtc))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<BankBalanceSnapshotDto>>.Ok(balances);
    }

    public async Task<ServiceResult<PagedResponse<RawBankTransactionDto>>> GetRawTransactionsAsync(
        Guid userId,
        Guid linkedBankAccountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var ownsAccount = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Include(x => x.Connection)
            .AnyAsync(
                x => x.Id == linkedBankAccountId
                     && x.Connection != null
                     && x.Connection.UserId == userId,
                cancellationToken);

        if (!ownsAccount)
        {
            return ServiceResult<PagedResponse<RawBankTransactionDto>>.Fail(
                "Account not found.",
                "bank_account_not_found",
                StatusCodes.Status404NotFound);
        }

        var normalizedPage = page <= 0 ? 1 : page;
        var normalizedPageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 200);
        var skip = (normalizedPage - 1) * normalizedPageSize;

        var totalCount = await dbContext.RawBankTransactions
            .AsNoTracking()
            .CountAsync(x => x.LinkedBankAccountId == linkedBankAccountId, cancellationToken);

        var items = await dbContext.RawBankTransactions
            .AsNoTracking()
            .Where(x => x.LinkedBankAccountId == linkedBankAccountId)
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.ImportedUtc)
            .Skip(skip)
            .Take(normalizedPageSize)
            .Select(x => new RawBankTransactionDto(
                x.Id,
                x.LinkedBankAccountId,
                x.ProviderTransactionId,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                x.Description,
                x.TransactionType,
                x.TransactionStatus,
                x.ImportedUtc))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<RawBankTransactionDto>>.Ok(
            new PagedResponse<RawBankTransactionDto>(items, normalizedPage, normalizedPageSize, totalCount));
    }

    public async Task StoreTokenAsync(
        OpenBankingConnection connection,
        string encryptedRefreshToken,
        DateTime accessTokenExpiresUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (connection.Token is null)
        {
            connection.Token = new BankConnectionToken
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id
            };
            dbContext.BankConnectionTokens.Add(connection.Token);
        }

        connection.Token.EncryptedRefreshToken = encryptedRefreshToken;
        connection.Token.AccessTokenExpiresUtc = accessTokenExpiresUtc;
        connection.Token.TokenObtainedUtc = now;
        connection.Token.IsRevoked = false;
        connection.Token.RevokedUtc = null;
        connection.UpdatedUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkConnectionStateAsync(
        OpenBankingConnection connection,
        string status,
        string? errorCode,
        string? errorReason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        connection.Status = status;
        connection.LastErrorCode = errorCode;
        connection.LastErrorReason = errorReason;
        connection.UpdatedUtc = now;
        if (status == BankConnectionStatuses.SyncPending)
        {
            connection.LastSyncAttemptedUtc = now;
        }

        if (status == BankConnectionStatuses.Synced)
        {
            connection.LastSuccessfulSyncUtc = now;
            connection.LastSyncAttemptedUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ServiceResult> DisconnectAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return ServiceResult.Fail(
                "Connection not found.",
                "bank_connection_not_found",
                StatusCodes.Status404NotFound);
        }

        connection.Status = BankConnectionStatuses.Revoked;
        connection.UpdatedUtc = DateTime.UtcNow;

        if (connection.Token is not null)
        {
            connection.Token.EncryptedRefreshToken = null;
            connection.Token.AccessTokenExpiresUtc = null;
            connection.Token.IsRevoked = true;
            connection.Token.RevokedUtc = DateTime.UtcNow;
        }

        var linkedAccounts = await dbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == connection.Id)
            .ToListAsync(cancellationToken);

        var linkedAccountIds = linkedAccounts.Select(x => x.Id).ToList();
        var projectedFinancialAccountIds = linkedAccounts
            .Where(x => x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .ToList();

        if (linkedAccountIds.Count > 0)
        {
            var balanceSnapshots = await dbContext.BankBalanceSnapshots
                .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
                .ToListAsync(cancellationToken);

            var rawTransactions = await dbContext.RawBankTransactions
                .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
                .ToListAsync(cancellationToken);

            dbContext.BankBalanceSnapshots.RemoveRange(balanceSnapshots);
            dbContext.RawBankTransactions.RemoveRange(rawTransactions);
            dbContext.LinkedBankAccounts.RemoveRange(linkedAccounts);
        }

        if (projectedFinancialAccountIds.Count > 0)
        {
            var projectionTransactions = await dbContext.Transactions
                .Where(x => projectedFinancialAccountIds.Contains(x.FinancialAccountId))
                .ToListAsync(cancellationToken);
            var projectionAccounts = await dbContext.FinancialAccounts
                .Where(x => projectedFinancialAccountIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

            dbContext.Transactions.RemoveRange(projectionTransactions);
            dbContext.FinancialAccounts.RemoveRange(projectionAccounts);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "disconnect_completed",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                connection.Status,
                provider = connection.ProviderName,
                linkedAccountsRemoved = linkedAccountIds.Count,
                projectedAccountsRemoved = projectedFinancialAccountIds.Count
            },
            cancellationToken);

        return ServiceResult.Ok();
    }

    private static IReadOnlyList<BankConnectionDto> DeduplicateUserVisibleConnections(
        IEnumerable<UserVisibleConnectionCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<BankConnectionDto>();

        foreach (var candidate in candidates.OrderByDescending(x => x.Summary.UpdatedUtc))
        {
            var key = BuildUserVisibleDedupKey(candidate);
            if (!seen.Add(key))
            {
                continue;
            }

            results.Add(candidate.Summary);
        }

        return results;
    }

    private static string BuildUserVisibleDedupKey(UserVisibleConnectionCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ProviderConnectionReference))
        {
            return $"ref:{candidate.Provider}:{candidate.ProviderEnvironment}:{candidate.ProviderConnectionReference}";
        }

        if (!string.IsNullOrWhiteSpace(candidate.ProviderDisplayName))
        {
            return $"display:{candidate.Provider}:{candidate.ProviderEnvironment}:{candidate.ProviderDisplayName}";
        }

        return $"connection:{candidate.Summary.Id}";
    }

    private sealed record UserVisibleConnectionCandidate(
        BankConnectionDto Summary,
        string? ProviderConnectionReference,
        string? ProviderDisplayName,
        string Provider,
        string ProviderEnvironment);

    private static string CreateStateNonce()
    {
        return Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    }
}




