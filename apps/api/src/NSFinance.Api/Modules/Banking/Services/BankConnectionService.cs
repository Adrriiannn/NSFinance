using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankConnectionService(
    AppDbContext dbContext,
    IAuditService auditService,
    IBankDisconnectQueue disconnectQueue,
    ILogger<BankConnectionService> logger)
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
        BankConnectionStatuses.Expired,
        BankConnectionStatuses.DisconnectPending,
        BankConnectionStatuses.DisconnectFailed
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
        try
        {
            return await ListConnectionsWithBrandingAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy projection without branding metadata for userId={UserId}.",
                userId);

            return await ListConnectionsWithoutBrandingAsync(userId, cancellationToken);
        }
    }

    public async Task<BankConnectionDto?> GetConnectionSummaryAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetConnectionSummaryWithBrandingAsync(userId, connectionId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy connection summary projection for userId={UserId} connectionId={ConnectionId}.",
                userId,
                connectionId);

            return await GetConnectionSummaryWithoutBrandingAsync(userId, connectionId, cancellationToken);
        }
    }

    public async Task<ConnectedBanksOverviewDto> ListUserVisibleConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ListUserVisibleConnectionsWithBrandingAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy connected-banks projection for userId={UserId}.",
                userId);

            return await ListUserVisibleConnectionsWithoutBrandingAsync(userId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<LinkedBankAccountDto>> ListLinkedAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ListLinkedAccountsWithBrandingAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy linked-accounts projection for userId={UserId}.",
                userId);

            return await ListLinkedAccountsWithoutBrandingAsync(userId, cancellationToken);
        }
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

    public async Task UpdateAuthStateAsync(
        OpenBankingConnection connection,
        string authStateNonce,
        DateTime? authStateExpiresUtc,
        CancellationToken cancellationToken)
    {
        connection.AuthStateNonce = authStateNonce;
        connection.AuthStateExpiresUtc = authStateExpiresUtc;
        connection.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
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
        if (IsSyncOrConsentState(status))
        {
            var persistedStatus = await dbContext.OpenBankingConnections
                .AsNoTracking()
                .Where(x => x.Id == connection.Id)
                .Select(x => x.Status)
                .SingleOrDefaultAsync(cancellationToken);

            if (IsDisconnectLifecycleState(persistedStatus))
            {
                logger.LogInformation(
                    "Skipped connection status update to {TargetStatus} because disconnect lifecycle state {PersistedStatus} is active for connectionId={ConnectionId}",
                    status,
                    persistedStatus,
                    connection.Id);
                return;
            }
        }

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
        var startedAt = Stopwatch.StartNew();
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

        if (connection.Status == BankConnectionStatuses.Revoked)
        {
            var hasRemainingConnectionData = await dbContext.LinkedBankAccounts
                .AsNoTracking()
                .AnyAsync(x => x.ConnectionId == connection.Id, cancellationToken);

            if (!hasRemainingConnectionData)
            {
                logger.LogInformation(
                    "Disconnect requested for already-revoked connectionId={ConnectionId}; no cleanup work remained.",
                    connection.Id);
                return ServiceResult.Ok();
            }
        }

        var previousStatus = connection.Status;
        if (connection.Status != BankConnectionStatuses.DisconnectPending)
        {
            var now = DateTime.UtcNow;
            connection.Status = BankConnectionStatuses.DisconnectPending;
            connection.LastErrorCode = null;
            connection.LastErrorReason = null;
            connection.UpdatedUtc = now;
            RevokeConnectionToken(connection, now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Disconnect request accepted and persisted as pending for connectionId={ConnectionId} userId={UserId} previousStatus={PreviousStatus} elapsedMs={ElapsedMs}",
            connection.Id,
            userId,
            previousStatus,
            startedAt.ElapsedMilliseconds);

        await WriteAuditSafeAsync(
            category: "banking",
            eventName: "disconnect_requested",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                previousStatus,
                status = connection.Status
            },
            cancellationToken);

        try
        {
            await disconnectQueue.QueueDisconnectCleanupAsync(userId, connection.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to enqueue disconnect cleanup for connectionId={ConnectionId}",
                connection.Id);

            connection.Status = BankConnectionStatuses.DisconnectFailed;
            connection.LastErrorCode = "disconnect_cleanup_queue_failed";
            connection.LastErrorReason = "Could not queue disconnect cleanup. Try disconnecting again.";
            connection.UpdatedUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await WriteAuditSafeAsync(
                category: "banking",
                eventName: "disconnect_queue_failed",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: userId,
                actorType: "user",
                metadata: new
                {
                    status = connection.Status,
                    code = connection.LastErrorCode
                },
                cancellationToken);

            return ServiceResult.Fail(
                "Could not queue disconnect cleanup. Try again.",
                "disconnect_cleanup_queue_failed",
                StatusCodes.Status500InternalServerError);
        }

        await WriteAuditSafeAsync(
            category: "banking",
            eventName: "disconnect_cleanup_queued",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                status = connection.Status
            },
            cancellationToken);

        return ServiceResult.Ok();
    }

    public async Task RunDisconnectCleanupAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        OpenBankingConnection? connection = null;
        try
        {
            connection = await dbContext.OpenBankingConnections
                .Include(x => x.Token)
                .SingleOrDefaultAsync(
                    x => x.Id == connectionId && x.UserId == userId,
                    cancellationToken);

            if (connection is null)
            {
                logger.LogWarning(
                    "Disconnect cleanup skipped because connection was not found connectionId={ConnectionId} userId={UserId}",
                    connectionId,
                    userId);
                return;
            }

            if (connection.Status == BankConnectionStatuses.Revoked)
            {
                logger.LogInformation(
                    "Disconnect cleanup skipped because connection was already revoked connectionId={ConnectionId}",
                    connectionId);
                return;
            }

            if (connection.Status != BankConnectionStatuses.DisconnectPending)
            {
                logger.LogInformation(
                    "Disconnect cleanup skipped because connection status was {Status} instead of disconnect_pending connectionId={ConnectionId}",
                    connection.Status,
                    connectionId);
                return;
            }

            var linkedAccountIdsQuery = dbContext.LinkedBankAccounts
                .Where(x => x.ConnectionId == connectionId)
                .Select(x => x.Id);

            var projectedFinancialAccountIdsQuery = dbContext.LinkedBankAccounts
                .Where(x => x.ConnectionId == connectionId && x.FinancialAccountId.HasValue)
                .Select(x => x.FinancialAccountId!.Value)
                .Distinct();

            var linkedAccountsTargeted = await dbContext.LinkedBankAccounts
                .Where(x => x.ConnectionId == connectionId)
                .CountAsync(cancellationToken);
            var projectedAccountsTargeted = await dbContext.FinancialAccounts
                .Where(x => x.UserId == userId && projectedFinancialAccountIdsQuery.Contains(x.Id))
                .CountAsync(cancellationToken);
            var rawTransactionsTargeted = await dbContext.RawBankTransactions
                .Where(x => linkedAccountIdsQuery.Contains(x.LinkedBankAccountId))
                .CountAsync(cancellationToken);
            var balanceSnapshotsTargeted = await dbContext.BankBalanceSnapshots
                .Where(x => linkedAccountIdsQuery.Contains(x.LinkedBankAccountId))
                .CountAsync(cancellationToken);
            var projectionTransactionsTargeted = await dbContext.Transactions
                .Where(x => projectedFinancialAccountIdsQuery.Contains(x.FinancialAccountId))
                .CountAsync(cancellationToken);

            logger.LogInformation(
                "Disconnect cleanup started for connectionId={ConnectionId} linkedAccountsTargeted={LinkedAccountsTargeted} projectedAccountsTargeted={ProjectedAccountsTargeted} rawTransactionsTargeted={RawTransactionsTargeted} balancesTargeted={BalanceSnapshotsTargeted} projectionTransactionsTargeted={ProjectionTransactionsTargeted}",
                connectionId,
                linkedAccountsTargeted,
                projectedAccountsTargeted,
                rawTransactionsTargeted,
                balanceSnapshotsTargeted,
                projectionTransactionsTargeted);

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var projectedAccountsDeleted = await dbContext.FinancialAccounts
                .Where(x => x.UserId == userId && projectedFinancialAccountIdsQuery.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
            var linkedAccountsDeleted = await dbContext.LinkedBankAccounts
                .Where(x => x.ConnectionId == connectionId)
                .ExecuteDeleteAsync(cancellationToken);

            var now = DateTime.UtcNow;
            connection.Status = BankConnectionStatuses.Revoked;
            connection.LastErrorCode = null;
            connection.LastErrorReason = null;
            connection.UpdatedUtc = now;
            RevokeConnectionToken(connection, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Disconnect cleanup completed for connectionId={ConnectionId} linkedAccountsDeleted={LinkedAccountsDeleted} projectedAccountsDeleted={ProjectedAccountsDeleted} targetedRawTransactions={RawTransactionsTargeted} targetedBalanceSnapshots={BalanceSnapshotsTargeted} targetedProjectionTransactions={ProjectionTransactionsTargeted} elapsedMs={ElapsedMs}",
                connectionId,
                linkedAccountsDeleted,
                projectedAccountsDeleted,
                rawTransactionsTargeted,
                balanceSnapshotsTargeted,
                projectionTransactionsTargeted,
                startedAt.ElapsedMilliseconds);

            await WriteAuditSafeAsync(
                category: "banking",
                eventName: "disconnect_completed",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: userId,
                actorType: "system",
                metadata: new
                {
                    status = connection.Status,
                    linkedAccountsDeleted,
                    projectedAccountsDeleted,
                    rawTransactionsTargeted,
                    balanceSnapshotsTargeted,
                    projectionTransactionsTargeted
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Disconnect cleanup failed for connectionId={ConnectionId}",
                connectionId);

            await MarkDisconnectFailedAsync(
                userId,
                connectionId,
                "disconnect_cleanup_failed",
                "Disconnect cleanup failed. Try disconnecting again.",
                cancellationToken);

            await WriteAuditSafeAsync(
                category: "banking",
                eventName: "disconnect_failed",
                targetEntityType: "open_banking_connection",
                targetEntityId: connectionId.ToString(),
                actorId: userId,
                actorType: "system",
                metadata: new
                {
                    code = "disconnect_cleanup_failed"
                },
                cancellationToken);
        }
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

    private sealed record LinkedBankAccountProjection(
        Guid Id,
        Guid ConnectionId,
        string ProviderAccountId,
        string? ProviderId,
        string? ProviderDisplayName,
        string? ProviderIconUri,
        string? ProviderLogoUri,
        string? ProviderBrandBgColor,
        string DisplayName,
        string? AccountType,
        string? AccountSubType,
        string Currency,
        string CurrentConnectionHealth,
        DateTime CreatedUtc,
        DateTime UpdatedUtc);

    private async Task<IReadOnlyList<BankConnectionDto>> ListConnectionsWithBrandingAsync(
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
                x.ProviderId,
                x.ProviderEnvironment,
                x.ProviderDisplayName,
                x.ProviderIconUri,
                x.ProviderLogoUri,
                x.ProviderBrandBgColor,
                x.BrandingLastSyncedAtUtc,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BankConnectionDto>> ListConnectionsWithoutBrandingAsync(
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
                null,
                x.ProviderEnvironment,
                x.ProviderDisplayName,
                null,
                null,
                null,
                null,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode))
            .ToListAsync(cancellationToken);
    }

    private async Task<BankConnectionDto?> GetConnectionSummaryWithBrandingAsync(
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
                x.ProviderId,
                x.ProviderEnvironment,
                x.ProviderDisplayName,
                x.ProviderIconUri,
                x.ProviderLogoUri,
                x.ProviderBrandBgColor,
                x.BrandingLastSyncedAtUtc,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<BankConnectionDto?> GetConnectionSummaryWithoutBrandingAsync(
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
                null,
                x.ProviderEnvironment,
                x.ProviderDisplayName,
                null,
                null,
                null,
                null,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<ConnectedBanksOverviewDto> ListUserVisibleConnectionsWithBrandingAsync(
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
                    x.ProviderId,
                    x.ProviderEnvironment,
                    x.ProviderDisplayName,
                    x.ProviderIconUri,
                    x.ProviderLogoUri,
                    x.ProviderBrandBgColor,
                    x.BrandingLastSyncedAtUtc,
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

        return BuildConnectedBanksOverview(projected);
    }

    private async Task<ConnectedBanksOverviewDto> ListUserVisibleConnectionsWithoutBrandingAsync(
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
                    null,
                    x.ProviderEnvironment,
                    x.ProviderDisplayName,
                    null,
                    null,
                    null,
                    null,
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

        return BuildConnectedBanksOverview(projected);
    }

    private static ConnectedBanksOverviewDto BuildConnectedBanksOverview(
        List<UserVisibleConnectionCandidate> projected)
    {
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

    private async Task<IReadOnlyList<LinkedBankAccountDto>> ListLinkedAccountsWithBrandingAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedAccounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => x.Connection != null && x.Connection.UserId == userId)
            .OrderBy(x => x.DisplayName)
            .Select(x => new LinkedBankAccountProjection(
                x.Id,
                x.ConnectionId,
                x.ProviderAccountId,
                x.Connection != null ? x.Connection.ProviderId : null,
                x.Connection != null ? x.Connection.ProviderDisplayName : null,
                x.Connection != null ? x.Connection.ProviderIconUri : null,
                x.Connection != null ? x.Connection.ProviderLogoUri : null,
                x.Connection != null ? x.Connection.ProviderBrandBgColor : null,
                x.DisplayName,
                x.AccountType,
                x.AccountSubType,
                x.Currency,
                x.CurrentConnectionHealth,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return await BuildLinkedAccountsResponseAsync(linkedAccounts, cancellationToken);
    }

    private async Task<IReadOnlyList<LinkedBankAccountDto>> ListLinkedAccountsWithoutBrandingAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedAccounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => x.Connection != null && x.Connection.UserId == userId)
            .OrderBy(x => x.DisplayName)
            .Select(x => new LinkedBankAccountProjection(
                x.Id,
                x.ConnectionId,
                x.ProviderAccountId,
                null,
                x.Connection != null ? x.Connection.ProviderDisplayName : null,
                null,
                null,
                null,
                x.DisplayName,
                x.AccountType,
                x.AccountSubType,
                x.Currency,
                x.CurrentConnectionHealth,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return await BuildLinkedAccountsResponseAsync(linkedAccounts, cancellationToken);
    }

    private async Task<IReadOnlyList<LinkedBankAccountDto>> BuildLinkedAccountsResponseAsync(
        List<LinkedBankAccountProjection> linkedAccounts,
        CancellationToken cancellationToken)
    {
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
                    account.ProviderId,
                    account.ProviderDisplayName,
                    account.ProviderIconUri,
                    account.ProviderLogoUri,
                    account.ProviderBrandBgColor,
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

    private async Task MarkDisconnectFailedAsync(
        Guid userId,
        Guid connectionId,
        string errorCode,
        string errorReason,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await dbContext.OpenBankingConnections
                .Include(x => x.Token)
                .SingleOrDefaultAsync(
                    x => x.Id == connectionId && x.UserId == userId,
                    cancellationToken);

            if (connection is null)
            {
                return;
            }

            connection.Status = BankConnectionStatuses.DisconnectFailed;
            connection.LastErrorCode = errorCode;
            connection.LastErrorReason = errorReason;
            connection.UpdatedUtc = DateTime.UtcNow;
            RevokeConnectionToken(connection, DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist disconnect_failed status for connectionId={ConnectionId}",
                connectionId);
        }
    }

    private async Task WriteAuditSafeAsync(
        string category,
        string eventName,
        string targetEntityType,
        string targetEntityId,
        Guid actorId,
        string actorType,
        object? metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.WriteEventAsync(
                category: category,
                eventName: eventName,
                targetEntityType: targetEntityType,
                targetEntityId: targetEntityId,
                actorId: actorId,
                actorType: actorType,
                metadata: metadata,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Audit write failed for event={EventName} targetEntityType={TargetEntityType} targetEntityId={TargetEntityId}",
                eventName,
                targetEntityType,
                targetEntityId);
        }
    }

    private static void RevokeConnectionToken(OpenBankingConnection connection, DateTime now)
    {
        if (connection.Token is null)
        {
            return;
        }

        connection.Token.EncryptedRefreshToken = null;
        connection.Token.AccessTokenExpiresUtc = null;
        connection.Token.IsRevoked = true;
        connection.Token.RevokedUtc = now;
    }

    private static bool IsDisconnectLifecycleState(string? status)
    {
        return status is BankConnectionStatuses.DisconnectPending
            or BankConnectionStatuses.DisconnectFailed
            or BankConnectionStatuses.Revoked;
    }

    private static bool IsSyncOrConsentState(string status)
    {
        return status is BankConnectionStatuses.ConnectionStarted
            or BankConnectionStatuses.ConsentInProgress
            or BankConnectionStatuses.ConnectedPendingSync
            or BankConnectionStatuses.Connected
            or BankConnectionStatuses.SyncPending
            or BankConnectionStatuses.Synced
            or BankConnectionStatuses.ReauthRequired
            or BankConnectionStatuses.Expired
            or BankConnectionStatuses.Failed;
    }

    private static bool IsProviderBrandingSchemaMissing(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.UndefinedColumn)
            {
                var columnName = postgresException.ColumnName ?? string.Empty;
                if (columnName.Equals("ProviderId", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("ProviderIconUri", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("ProviderLogoUri", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("ProviderBrandBgColor", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("BrandingLastSyncedAtUtc", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var message = postgresException.MessageText ?? string.Empty;
                if (message.Contains("\"ProviderId\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"ProviderIconUri\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"ProviderLogoUri\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"ProviderBrandBgColor\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BrandingLastSyncedAtUtc\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }

    private static string CreateStateNonce()
    {
        return Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    }
}




