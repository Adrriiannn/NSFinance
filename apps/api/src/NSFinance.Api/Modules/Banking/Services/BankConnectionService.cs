using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
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
    private const int DeterministicEnrichmentCurrentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

    private sealed record EnrichmentConnectionRow(
        Guid Id,
        string? ProviderDisplayName,
        string Status,
        DateTime UpdatedUtc,
        DateTime? LastSyncAttemptedUtc,
        DateTime? LastSuccessfulSyncUtc,
        DateTime? HistoricalEnrichmentStartedUtc,
        DateTime? HistoricalEnrichmentCompletedUtc,
        int? HistoricalEnrichmentVersion,
        bool NeedsHistoricalReclassification);

    private sealed record ConnectionTransactionEnrichmentStats(
        Guid ConnectionId,
        int TotalCount,
        int CurrentCount,
        int StaleCount,
        int DeferredWaitingForCounterpartyCount,
        int CurrentEnrichedAfterStartCount,
        DateTime? LastUpdatedUtc);
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

    public async Task<ServiceResult<OpenBankingConnection>> PrepareConnectionReconfirmAsync(
        Guid userId,
        Guid connectionId,
        string providerEnvironment,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleOrDefaultAsync(
                x => x.Id == connectionId && x.UserId == userId,
                cancellationToken);

        if (connection is null)
        {
            return ServiceResult<OpenBankingConnection>.Fail(
                "Connection not found.",
                "bank_connection_not_found",
                StatusCodes.Status404NotFound);
        }

        if (!string.Equals(connection.ProviderName, BankingProviders.TrueLayer, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<OpenBankingConnection>.Fail(
                "Only TrueLayer connections can be reconfirmed.",
                "bank_connection_provider_not_supported",
                StatusCodes.Status400BadRequest);
        }

        if (connection.Status is BankConnectionStatuses.DisconnectPending
            or BankConnectionStatuses.DisconnectFailed
            or BankConnectionStatuses.Revoked)
        {
            return ServiceResult<OpenBankingConnection>.Fail(
                "This connection is disconnected and cannot be reconfirmed.",
                "bank_connection_disconnected",
                StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        connection.ProviderEnvironment = providerEnvironment;
        connection.Status = BankConnectionStatuses.ConnectionStarted;
        connection.LastErrorCode = null;
        connection.LastErrorReason = null;
        connection.AuthStateNonce = CreateStateNonce();
        connection.AuthStateExpiresUtc = now.AddMinutes(15);
        connection.UpdatedUtc = now;
        RevokeConnectionToken(connection, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditSafeAsync(
            category: "banking",
            eventName: "bank_connection_reconfirm_started",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                status = connection.Status,
                providerEnvironment
            },
            cancellationToken);

        return ServiceResult<OpenBankingConnection>.Ok(connection);
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
            .Include(x => x.IdentityInfo)
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

    public async Task<BankEnrichmentProgressDto> GetEnrichmentProgressAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var candidateStatuses = new[]
        {
            BankConnectionStatuses.ConnectionStarted,
            BankConnectionStatuses.ConsentInProgress,
            BankConnectionStatuses.ConnectedPendingSync,
            BankConnectionStatuses.Connected,
            BankConnectionStatuses.SyncPending,
            BankConnectionStatuses.Synced,
            BankConnectionStatuses.ReauthRequired,
            BankConnectionStatuses.Expired,
            BankConnectionStatuses.Failed
        };

        var connections = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId && candidateStatuses.Contains(x.Status))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new EnrichmentConnectionRow(
                x.Id,
                x.ProviderDisplayName,
                x.Status,
                x.UpdatedUtc,
                x.LastSyncAttemptedUtc,
                x.LastSuccessfulSyncUtc,
                x.HistoricalEnrichmentStartedUtc,
                x.HistoricalEnrichmentCompletedUtc,
                x.HistoricalEnrichmentVersion,
                x.NeedsHistoricalReclassification))
            .ToListAsync(cancellationToken);

        if (connections.Count == 0)
        {
            return new BankEnrichmentProgressDto(
                InProgress: false,
                Completed: true,
                ProgressPercent: 100d,
                ProcessedCount: 0,
                TotalCount: 0,
                RemainingCount: 0,
                Stage: "idle",
                LastUpdatedUtc: null,
                NewestFirst: true,
                Connections: []);
        }

        var transactionStatsByConnectionId = await BuildConnectionEnrichmentStatsAsync(
            connections,
            cancellationToken);

        var connectionProgress = connections
            .Select(connection =>
            {
                transactionStatsByConnectionId.TryGetValue(connection.Id, out var stats);

                var completed = IsHistoricalEnrichmentCompleted(connection);
                var required = IsHistoricalEnrichmentRequired(connection);
                var awaitingSync = IsSyncAwaitingRunStatus(connection.Status);
                var totalRows = Math.Max(0, stats?.TotalCount ?? 0);
                var staleCount = Math.Max(0, Math.Min(stats?.StaleCount ?? 0, totalRows));
                var currentCount = Math.Max(0, Math.Min(stats?.CurrentCount ?? 0, totalRows));
                var deferredWaitingForCounterpartyCount = Math.Max(
                    0,
                    Math.Min(stats?.DeferredWaitingForCounterpartyCount ?? 0, totalRows));
                var currentEnrichedAfterStartCount = connection.HistoricalEnrichmentStartedUtc.HasValue
                    ? Math.Max(0, Math.Min(stats?.CurrentEnrichedAfterStartCount ?? 0, currentCount))
                    : 0;
                var hasEverSynced = connection.LastSyncAttemptedUtc.HasValue || connection.LastSuccessfulSyncUtc.HasValue;

                int totalCount;
                int processedCount;
                int remainingCount;

                if (awaitingSync && !hasEverSynced && !connection.HistoricalEnrichmentStartedUtc.HasValue)
                {
                    totalCount = 0;
                    processedCount = 0;
                    remainingCount = 0;
                }
                // During active/pending enrichment, progress should track work completed in
                // the current run scope rather than rows already current from older runs.
                else if (required || connection.HistoricalEnrichmentStartedUtc.HasValue)
                {
                    processedCount = currentEnrichedAfterStartCount;
                    remainingCount = staleCount;
                    totalCount = processedCount + remainingCount;
                }
                else
                {
                    processedCount = currentCount;
                    totalCount = totalRows;
                    remainingCount = Math.Max(0, totalCount - processedCount);
                }

                var inProgress = IsHistoricalEnrichmentInProgress(connection, remainingCount, awaitingSync, required);
                var stage = ResolveHistoricalEnrichmentStage(
                    connection.Status,
                    inProgress,
                    completed,
                    required,
                    totalCount,
                    processedCount,
                    remainingCount,
                    deferredWaitingForCounterpartyCount,
                    awaitingSync,
                    connection.LastSyncAttemptedUtc,
                    connection.LastSuccessfulSyncUtc,
                    connection.UpdatedUtc);
                var progressPercent = totalCount > 0
                    ? Math.Round((processedCount / (double)totalCount) * 100d, 2, MidpointRounding.AwayFromZero)
                    : completed
                        ? 100d
                        : 0d;
                var lastUpdatedUtc = MaxUtc(stats?.LastUpdatedUtc, connection.UpdatedUtc);

                return new BankEnrichmentConnectionProgressDto(
                    connection.Id,
                    connection.ProviderDisplayName,
                    inProgress,
                    completed,
                    progressPercent,
                    processedCount,
                    totalCount,
                    remainingCount,
                    stage,
                    lastUpdatedUtc);
            })
            .OrderByDescending(x => x.InProgress)
            .ThenBy(x => x.RemainingCount == 0 ? 1 : 0)
            .ThenByDescending(x => x.LastUpdatedUtc)
            .ToList();

        var activeConnections = connectionProgress
            .Where(x =>
                x.InProgress
                || x.Stage is "queued_for_sync"
                || x.Stage is "needs_reclassification"
                || x.Stage is "waiting_for_first_sync"
                || x.Stage is "waiting_for_counterparty"
                || x.Stage is "categorizing")
            .ToList();

        var inProgressOverall = activeConnections.Count > 0;
        var progressScope = inProgressOverall ? activeConnections : connectionProgress;

        var total = progressScope.Sum(x => x.TotalCount);
        var processed = progressScope.Sum(x => x.ProcessedCount);
        var remaining = progressScope.Sum(x => x.RemainingCount);
        var completedOverall = !inProgressOverall && connectionProgress.All(x => x.Completed || x.Stage is "completed" or "idle");
        var percentOverall = total > 0
            ? Math.Round((processed / (double)total) * 100d, 2, MidpointRounding.AwayFromZero)
            : inProgressOverall
                ? 0d
                : 100d;
        var lastUpdatedOverall = progressScope
            .Where(x => x.LastUpdatedUtc.HasValue)
            .Select(x => x.LastUpdatedUtc!.Value)
            .DefaultIfEmpty()
            .Max();

        var resolvedStage = ResolveOverallHistoricalEnrichmentStage(progressScope, inProgressOverall, completedOverall);

        return new BankEnrichmentProgressDto(
            InProgress: inProgressOverall,
            Completed: completedOverall,
            ProgressPercent: percentOverall,
            ProcessedCount: processed,
            TotalCount: total,
            RemainingCount: remaining,
            Stage: resolvedStage,
            LastUpdatedUtc: lastUpdatedOverall == default ? null : lastUpdatedOverall,
            NewestFirst: true,
            Connections: connectionProgress);
    }

    public async Task<ServiceResult<DeterministicCategorizationDiagnosticsDto>> GetDeterministicCategorizationDiagnosticsAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.ConnectionId == connectionId
                && x.Connection != null
                && x.Connection.UserId == userId
                && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (linkedFinancialAccountIds.Length == 0)
        {
            return ServiceResult<DeterministicCategorizationDiagnosticsDto>.Fail(
                "Connection not found.",
                "bank_connection_not_found",
                StatusCodes.Status404NotFound);
        }

        var rows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => linkedFinancialAccountIds.Contains(x.FinancialAccountId))
            .Select(x => new
            {
                x.Id,
                x.FinancialAccountId,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                x.DeterministicClassificationStatus,
                x.DeterministicClassificationTerminal,
                x.DeterministicDeferredRetryEligible,
                x.NeedsDeterministicReclassification,
                x.DeterministicClassificationVersion,
                x.DeterministicClassificationRuleKey,
                x.DeterministicReasonCode,
                x.DeterministicLinkedTransactionId,
                x.DeterministicRelationshipType,
                x.DeterministicRelationshipGroupId,
                x.DeterministicMatchScore,
                x.DeterministicClassificationEvaluatedUtc
            })
            .ToListAsync(cancellationToken);

        var statusCounts = rows
            .GroupBy(x => x.DeterministicClassificationStatus)
            .Select(group => new DeterministicCategorizationStatusCountDto(
                MapDeterministicClassificationStatus(group.Key),
                group.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Status, StringComparer.Ordinal)
            .ToList();

        var sampleDecisions = rows
            .OrderBy(x => x.DeterministicClassificationTerminal ? 1 : 0)
            .ThenByDescending(x => x.DeterministicClassificationEvaluatedUtc)
            .Take(120)
            .Select(x => new DeterministicTransactionDecisionDto(
                x.Id,
                x.FinancialAccountId,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                MapDeterministicClassificationStatus(x.DeterministicClassificationStatus),
                x.DeterministicClassificationTerminal,
                x.DeterministicDeferredRetryEligible,
                x.DeterministicClassificationVersion,
                x.DeterministicClassificationRuleKey,
                x.DeterministicReasonCode,
                x.DeterministicLinkedTransactionId,
                x.DeterministicRelationshipType,
                x.DeterministicRelationshipGroupId,
                x.DeterministicMatchScore,
                x.DeterministicClassificationEvaluatedUtc))
            .ToList();

        var terminalCount = rows.Count(x => x.DeterministicClassificationTerminal);
        var deferredMoreContextCount = rows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext);
        var deferredCounterpartyCount = rows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty);
        var actionableRemainingCount = rows.Count(x =>
        {
            var versionBehind = !x.DeterministicClassificationVersion.HasValue
                || x.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion;
            var remaining = x.NeedsDeterministicReclassification
                || versionBehind
                || !x.DeterministicClassificationTerminal
                || x.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired;
            if (!remaining)
            {
                return false;
            }

            var deferredCounterpartyCurrent = x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty
                && !versionBehind
                && !x.NeedsDeterministicReclassification;
            var deferredMoreContextCurrent = x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext
                && !versionBehind
                && !x.NeedsDeterministicReclassification;

            return !deferredCounterpartyCurrent && !deferredMoreContextCurrent;
        });

        var queueEligible = actionableRemainingCount > 0;
        var queueEligibilityReason = queueEligible
            ? "actionable_remaining_rows"
            : rows.Count > terminalCount
                ? "deferred_only_remaining_rows"
                : "no_remaining_rows";
        var continuationDecision = queueEligible ? "continue" : "stop";
        var continuationReason = queueEligible
            ? "actionable_remaining_rows"
            : rows.Count > terminalCount
                ? "deferred_only_remaining_rows"
                : "no_remaining_rows";

        return ServiceResult<DeterministicCategorizationDiagnosticsDto>.Ok(
            new DeterministicCategorizationDiagnosticsDto(
                connectionId,
                DeterministicEnrichmentCurrentVersion,
                rows.Count,
                terminalCount,
                Math.Max(0, rows.Count - terminalCount),
                actionableRemainingCount,
                deferredMoreContextCount,
                deferredCounterpartyCount,
                queueEligible,
                queueEligibilityReason,
                continuationDecision,
                continuationReason,
                statusCounts,
                sampleDecisions));
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
                x.NormalizedProviderTransactionId,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                x.ValueAtUtc,
                x.Description,
                x.TransactionType,
                x.TransactionStatus,
                x.SourceEndpoint,
                x.ProviderStatus,
                x.StatusNormalizationReason,
                x.ProviderTimestampRaw,
                x.ValueTimestampRaw,
                x.TimestampSource,
                x.TimestampPrecision,
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
            var linkedCardIdsQuery = dbContext.LinkedBankCards
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
            var linkedCardsTargeted = await dbContext.LinkedBankCards
                .Where(x => x.ConnectionId == connectionId)
                .CountAsync(cancellationToken);
            var cardTransactionsTargeted = await dbContext.RawBankCardTransactions
                .Where(x => linkedCardIdsQuery.Contains(x.LinkedBankCardId))
                .CountAsync(cancellationToken);
            var cardBalanceSnapshotsTargeted = await dbContext.BankCardBalanceSnapshots
                .Where(x => linkedCardIdsQuery.Contains(x.LinkedBankCardId))
                .CountAsync(cancellationToken);
            var directDebitsTargeted = await dbContext.BankDirectDebits
                .Where(x => linkedAccountIdsQuery.Contains(x.LinkedBankAccountId))
                .CountAsync(cancellationToken);
            var standingOrdersTargeted = await dbContext.BankStandingOrders
                .Where(x => linkedAccountIdsQuery.Contains(x.LinkedBankAccountId))
                .CountAsync(cancellationToken);
            var identityRowsTargeted = await dbContext.BankConnectionIdentityInfos
                .Where(x => x.ConnectionId == connectionId)
                .CountAsync(cancellationToken);

            logger.LogInformation(
                "Disconnect cleanup started for connectionId={ConnectionId} linkedAccountsTargeted={LinkedAccountsTargeted} projectedAccountsTargeted={ProjectedAccountsTargeted} rawTransactionsTargeted={RawTransactionsTargeted} balancesTargeted={BalanceSnapshotsTargeted} projectionTransactionsTargeted={ProjectionTransactionsTargeted} linkedCardsTargeted={LinkedCardsTargeted} cardTransactionsTargeted={CardTransactionsTargeted} cardBalancesTargeted={CardBalancesTargeted} directDebitsTargeted={DirectDebitsTargeted} standingOrdersTargeted={StandingOrdersTargeted} identityRowsTargeted={IdentityRowsTargeted}",
                connectionId,
                linkedAccountsTargeted,
                projectedAccountsTargeted,
                rawTransactionsTargeted,
                balanceSnapshotsTargeted,
                projectionTransactionsTargeted,
                linkedCardsTargeted,
                cardTransactionsTargeted,
                cardBalanceSnapshotsTargeted,
                directDebitsTargeted,
                standingOrdersTargeted,
                identityRowsTargeted);

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var identityRowsDeleted = await dbContext.BankConnectionIdentityInfos
                .Where(x => x.ConnectionId == connectionId)
                .ExecuteDeleteAsync(cancellationToken);
            var linkedCardsDeleted = await dbContext.LinkedBankCards
                .Where(x => x.ConnectionId == connectionId)
                .ExecuteDeleteAsync(cancellationToken);
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
                "Disconnect cleanup completed for connectionId={ConnectionId} linkedAccountsDeleted={LinkedAccountsDeleted} projectedAccountsDeleted={ProjectedAccountsDeleted} linkedCardsDeleted={LinkedCardsDeleted} identityRowsDeleted={IdentityRowsDeleted} targetedRawTransactions={RawTransactionsTargeted} targetedBalanceSnapshots={BalanceSnapshotsTargeted} targetedProjectionTransactions={ProjectionTransactionsTargeted} targetedCardTransactions={CardTransactionsTargeted} targetedCardBalances={CardBalancesTargeted} targetedDirectDebits={DirectDebitsTargeted} targetedStandingOrders={StandingOrdersTargeted} elapsedMs={ElapsedMs}",
                connectionId,
                linkedAccountsDeleted,
                projectedAccountsDeleted,
                linkedCardsDeleted,
                identityRowsDeleted,
                rawTransactionsTargeted,
                balanceSnapshotsTargeted,
                projectionTransactionsTargeted,
                cardTransactionsTargeted,
                cardBalanceSnapshotsTargeted,
                directDebitsTargeted,
                standingOrdersTargeted,
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
                    linkedCardsDeleted,
                    identityRowsDeleted,
                    rawTransactionsTargeted,
                    balanceSnapshotsTargeted,
                    projectionTransactionsTargeted,
                    cardTransactionsTargeted,
                    cardBalanceSnapshotsTargeted,
                    directDebitsTargeted,
                    standingOrdersTargeted
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
        Guid? FinancialAccountId,
        string ProviderAccountId,
        string? ProviderId,
        string? ProviderDisplayName,
        string? ProviderIconUri,
        string? ProviderLogoUri,
        string? ProviderBrandBgColor,
        string? ConnectedFullName,
        string DisplayName,
        string? AccountType,
        string? AccountSubType,
        string Currency,
        string? AccountNumberMetadataJson,
        string CurrentConnectionHealth,
        DateTime CreatedUtc,
        DateTime UpdatedUtc);

    private sealed record LinkedBankCardProjection(
        Guid Id,
        Guid ConnectionId,
        string ProviderCardId,
        string? ProviderAccountId,
        string DisplayName,
        string Currency,
        string? CardType,
        string? CardNetwork,
        string? CardNumberLastFour,
        string? NameOnCard,
        DateTime? ValidFromUtc,
        DateTime? ValidToUtc,
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
                x.LastErrorCode,
                x.GrantedScopesCsv,
                x.SupportsInfo,
                x.SupportsCards,
                x.SupportsDirectDebits,
                x.SupportsStandingOrders,
                x.IdentityInfo != null ? x.IdentityInfo.FullName : null,
                x.IdentityInfo != null ? x.IdentityInfo.FetchedUtc : null))
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
                x.LastErrorCode,
                null,
                null,
                null,
                null,
                null,
                null,
                null))
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
                x.LastErrorCode,
                x.GrantedScopesCsv,
                x.SupportsInfo,
                x.SupportsCards,
                x.SupportsDirectDebits,
                x.SupportsStandingOrders,
                x.IdentityInfo != null ? x.IdentityInfo.FullName : null,
                x.IdentityInfo != null ? x.IdentityInfo.FetchedUtc : null))
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
                x.LastErrorCode,
                null,
                null,
                null,
                null,
                null,
                null,
                null))
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
                    x.LastErrorCode,
                    x.GrantedScopesCsv,
                    x.SupportsInfo,
                    x.SupportsCards,
                    x.SupportsDirectDebits,
                    x.SupportsStandingOrders,
                    x.IdentityInfo != null ? x.IdentityInfo.FullName : null,
                    x.IdentityInfo != null ? x.IdentityInfo.FetchedUtc : null),
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
                    x.LastErrorCode,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
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
                x.FinancialAccountId,
                x.ProviderAccountId,
                x.Connection != null ? x.Connection.ProviderId : null,
                x.Connection != null ? x.Connection.ProviderDisplayName : null,
                x.Connection != null ? x.Connection.ProviderIconUri : null,
                x.Connection != null ? x.Connection.ProviderLogoUri : null,
                x.Connection != null ? x.Connection.ProviderBrandBgColor : null,
                x.Connection != null && x.Connection.IdentityInfo != null ? x.Connection.IdentityInfo.FullName : null,
                x.DisplayName,
                x.AccountType,
                x.AccountSubType,
                x.Currency,
                x.AccountNumberMetadataJson,
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
                x.FinancialAccountId,
                x.ProviderAccountId,
                null,
                x.Connection != null ? x.Connection.ProviderDisplayName : null,
                null,
                null,
                null,
                x.Connection != null && x.Connection.IdentityInfo != null ? x.Connection.IdentityInfo.FullName : null,
                x.DisplayName,
                x.AccountType,
                x.AccountSubType,
                x.Currency,
                x.AccountNumberMetadataJson,
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
                var resolvedDisplayName = ResolveLinkedAccountDisplayName(
                    account.DisplayName,
                    account.ProviderDisplayName,
                    account.AccountType,
                    account.Currency,
                    account.ConnectedFullName,
                    account.AccountNumberMetadataJson);

                return new LinkedBankAccountDto(
                    account.Id,
                    account.ConnectionId,
                    account.FinancialAccountId,
                    account.ProviderAccountId,
                    account.ProviderId,
                    account.ProviderDisplayName,
                    account.ProviderIconUri,
                    account.ProviderLogoUri,
                    account.ProviderBrandBgColor,
                    resolvedDisplayName,
                    account.AccountType,
                    account.AccountSubType,
                    account.Currency,
                    account.CurrentConnectionHealth,
                    latestBalance?.Available,
                    latestBalance?.Current,
                    latestBalance?.Overdraft,
                    account.CreatedUtc,
                    account.UpdatedUtc,
                    account.AccountNumberMetadataJson);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<LinkedBankCardDto>> ListLinkedCardsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var cards = await dbContext.LinkedBankCards
                .AsNoTracking()
                .Where(x => x.Connection != null && x.Connection.UserId == userId)
                .OrderBy(x => x.DisplayName)
                .Select(x => new LinkedBankCardProjection(
                    x.Id,
                    x.ConnectionId,
                    x.ProviderCardId,
                    x.ProviderAccountId,
                    x.DisplayName,
                    x.Currency,
                    x.CardType,
                    x.CardNetwork,
                    x.CardNumberLastFour,
                    x.NameOnCard,
                    x.ValidFromUtc,
                    x.ValidToUtc,
                    x.CurrentConnectionHealth,
                    x.CreatedUtc,
                    x.UpdatedUtc))
                .ToListAsync(cancellationToken);

            if (cards.Count == 0)
            {
                return [];
            }

            var cardIds = cards.Select(x => x.Id).ToList();
            var latestBalances = await dbContext.BankCardBalanceSnapshots
                .AsNoTracking()
                .Where(x => cardIds.Contains(x.LinkedBankCardId))
                .GroupBy(x => x.LinkedBankCardId)
                .Select(g => g.OrderByDescending(x => x.CapturedUtc).First())
                .ToDictionaryAsync(x => x.LinkedBankCardId, cancellationToken);

            return cards
                .Select(card =>
                {
                    latestBalances.TryGetValue(card.Id, out var latestBalance);
                    return new LinkedBankCardDto(
                        card.Id,
                        card.ConnectionId,
                        card.ProviderCardId,
                        card.ProviderAccountId,
                        card.DisplayName,
                        card.Currency,
                        card.CardType,
                        card.CardNetwork,
                        card.CardNumberLastFour,
                        card.NameOnCard,
                        card.ValidFromUtc,
                        card.ValidToUtc,
                        card.CurrentConnectionHealth,
                        latestBalance?.Available,
                        latestBalance?.Current,
                        latestBalance?.Limit,
                        latestBalance?.Outstanding,
                        card.CreatedUtc,
                        card.UpdatedUtc);
                })
                .ToList();
        }
        catch (Exception ex) when (IsExpandedBankingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Expanded banking schema is unavailable while reading cards for userId={UserId}. Returning empty card list.",
                userId);
            return [];
        }
    }

    public async Task<ServiceResult<BankRecurringPaymentsDto>> GetRecurringPaymentsForLinkedAccountAsync(
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
            return ServiceResult<BankRecurringPaymentsDto>.Fail(
                "Account not found.",
                "bank_account_not_found",
                StatusCodes.Status404NotFound);
        }

        try
        {
            var directDebits = await dbContext.BankDirectDebits
                .AsNoTracking()
                .Where(x => x.LinkedBankAccountId == linkedBankAccountId)
                .Join(
                    dbContext.LinkedBankAccounts.AsNoTracking(),
                    debit => debit.LinkedBankAccountId,
                    account => account.Id,
                    (debit, account) => new BankDirectDebitDto(
                        debit.Id,
                        debit.LinkedBankAccountId,
                        account.ConnectionId,
                        account.DisplayName,
                        debit.ProviderDirectDebitId,
                        debit.Status,
                        debit.MandateType,
                        debit.Reference,
                        debit.MerchantName,
                        debit.PreviousPaymentDateUtc,
                        debit.PreviousPaymentAmount,
                        debit.PreviousPaymentCurrency,
                        debit.NextPaymentDateUtc,
                        debit.NextPaymentAmount,
                        debit.NextPaymentCurrency,
                        debit.UpdatedUtc))
                .OrderBy(x => x.NextPaymentDateUtc ?? DateTime.MaxValue)
                .ThenBy(x => x.AccountDisplayName)
                .ToListAsync(cancellationToken);

            var standingOrders = await dbContext.BankStandingOrders
                .AsNoTracking()
                .Where(x => x.LinkedBankAccountId == linkedBankAccountId)
                .Join(
                    dbContext.LinkedBankAccounts.AsNoTracking(),
                    order => order.LinkedBankAccountId,
                    account => account.Id,
                    (order, account) => new BankStandingOrderDto(
                        order.Id,
                        order.LinkedBankAccountId,
                        account.ConnectionId,
                        account.DisplayName,
                        order.ProviderStandingOrderId,
                        order.Status,
                        order.Frequency,
                        order.Reference,
                        order.PayeeName,
                        order.FirstPaymentDateUtc,
                        order.NextPaymentDateUtc,
                        order.FinalPaymentDateUtc,
                        order.NextPaymentAmount,
                        order.NextPaymentCurrency,
                        order.UpdatedUtc))
                .OrderBy(x => x.NextPaymentDateUtc ?? DateTime.MaxValue)
                .ThenBy(x => x.AccountDisplayName)
                .ToListAsync(cancellationToken);

            return ServiceResult<BankRecurringPaymentsDto>.Ok(
                new BankRecurringPaymentsDto(directDebits, standingOrders));
        }
        catch (Exception ex) when (IsExpandedBankingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Expanded banking schema is unavailable while reading recurring payments for linkedAccountId={LinkedAccountId}. Returning empty recurring payload.",
                linkedBankAccountId);
            return ServiceResult<BankRecurringPaymentsDto>.Ok(new BankRecurringPaymentsDto([], []));
        }
    }

    public async Task<BankRecurringPaymentsDto> ListRecurringPaymentsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var directDebits = await dbContext.BankDirectDebits
                .AsNoTracking()
                .Where(x => x.LinkedBankAccount != null
                    && x.LinkedBankAccount.Connection != null
                    && x.LinkedBankAccount.Connection.UserId == userId)
                .Select(x => new BankDirectDebitDto(
                    x.Id,
                    x.LinkedBankAccountId,
                    x.LinkedBankAccount!.ConnectionId,
                    x.LinkedBankAccount.DisplayName,
                    x.ProviderDirectDebitId,
                    x.Status,
                    x.MandateType,
                    x.Reference,
                    x.MerchantName,
                    x.PreviousPaymentDateUtc,
                    x.PreviousPaymentAmount,
                    x.PreviousPaymentCurrency,
                    x.NextPaymentDateUtc,
                    x.NextPaymentAmount,
                    x.NextPaymentCurrency,
                    x.UpdatedUtc))
                .OrderBy(x => x.NextPaymentDateUtc ?? DateTime.MaxValue)
                .ThenBy(x => x.AccountDisplayName)
                .ToListAsync(cancellationToken);

            var standingOrders = await dbContext.BankStandingOrders
                .AsNoTracking()
                .Where(x => x.LinkedBankAccount != null
                    && x.LinkedBankAccount.Connection != null
                    && x.LinkedBankAccount.Connection.UserId == userId)
                .Select(x => new BankStandingOrderDto(
                    x.Id,
                    x.LinkedBankAccountId,
                    x.LinkedBankAccount!.ConnectionId,
                    x.LinkedBankAccount.DisplayName,
                    x.ProviderStandingOrderId,
                    x.Status,
                    x.Frequency,
                    x.Reference,
                    x.PayeeName,
                    x.FirstPaymentDateUtc,
                    x.NextPaymentDateUtc,
                    x.FinalPaymentDateUtc,
                    x.NextPaymentAmount,
                    x.NextPaymentCurrency,
                    x.UpdatedUtc))
                .OrderBy(x => x.NextPaymentDateUtc ?? DateTime.MaxValue)
                .ThenBy(x => x.AccountDisplayName)
                .ToListAsync(cancellationToken);

            return new BankRecurringPaymentsDto(directDebits, standingOrders);
        }
        catch (Exception ex) when (IsExpandedBankingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Expanded banking schema is unavailable while reading recurring payments for userId={UserId}. Returning empty recurring payload.",
                userId);
            return new BankRecurringPaymentsDto([], []);
        }
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

    private static string ResolveLinkedAccountDisplayName(
        string? providerDisplayName,
        string? providerInstitutionDisplayName,
        string? accountType,
        string currency,
        string? connectedFullName,
        string? accountNumberMetadataJson)
    {
        var normalizedDisplayName = NormalizeLabel(providerDisplayName);
        if (!string.IsNullOrWhiteSpace(normalizedDisplayName)
            && LooksLikeConnectedIdentity(normalizedDisplayName, connectedFullName))
        {
            normalizedDisplayName = null;
        }

        var providerLabel = ResolveProviderDisplayLabel(providerInstitutionDisplayName)
            ?? ResolveProviderDisplayLabel(normalizedDisplayName);
        var maskedHint = ExtractMaskedAccountHint(accountNumberMetadataJson);

        if (!string.IsNullOrWhiteSpace(providerLabel))
        {
            if (!string.IsNullOrWhiteSpace(maskedHint))
            {
                return $"{providerLabel} **{maskedHint}";
            }

            return providerLabel;
        }

        if (!string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return normalizedDisplayName;
        }

        var resolvedCurrency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
        var friendlyType = ResolveFriendlyAccountType(accountType);
        if (!string.IsNullOrWhiteSpace(maskedHint))
        {
            return $"{resolvedCurrency} {friendlyType} **{maskedHint}";
        }

        return $"{resolvedCurrency} {friendlyType}";
    }

    private static string? ResolveProviderDisplayLabel(string? providerDisplayName)
    {
        var normalized = NormalizeLabel(providerDisplayName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var compact = normalized;
        if (compact.StartsWith("ob-", StringComparison.OrdinalIgnoreCase)
            || compact.StartsWith("ob_", StringComparison.OrdinalIgnoreCase)
            || compact.StartsWith("ob ", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[3..];
        }

        var tokens = compact
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (tokens.Count > 1)
        {
            var lastToken = tokens[^1];
            if (lastToken.Equals("ie", StringComparison.OrdinalIgnoreCase)
                || lastToken.Equals("uk", StringComparison.OrdinalIgnoreCase)
                || lastToken.Equals("gb", StringComparison.OrdinalIgnoreCase)
                || lastToken.Equals("eu", StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(tokens.Count - 1);
            }
        }

        if (tokens.Count == 0)
        {
            return normalized;
        }

        var joinedSingle = string.Join("", tokens).ToUpperInvariant();
        if (joinedSingle is "AIB" or "BOI" or "PTSB" or "TSB" or "HSBC" or "MBNA" or "RBS")
        {
            return joinedSingle;
        }

        return string.Join(" ", tokens.Select(ToProviderTitleCase));
    }

    private static string ToProviderTitleCase(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        if (token.Length == 1)
        {
            return token.ToUpperInvariant();
        }

        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private static string? NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool LooksLikeConnectedIdentity(string accountLabel, string? connectedFullName)
    {
        var normalizedConnectedName = NormalizeLabel(connectedFullName);
        if (normalizedConnectedName is null)
        {
            return false;
        }

        var accountTokens = accountLabel
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .OrderBy(token => token)
            .ToArray();

        var connectedTokens = normalizedConnectedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .OrderBy(token => token)
            .ToArray();

        if (accountTokens.Length < 2 || accountTokens.Length != connectedTokens.Length)
        {
            return false;
        }

        return accountTokens.SequenceEqual(connectedTokens);
    }

    private static string ResolveFriendlyAccountType(string? accountType)
    {
        var normalized = accountType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "transaction" => "current account",
            "current" => "current account",
            "checking" => "current account",
            "savings" => "savings account",
            "credit" => "credit account",
            "loan" => "loan account",
            _ => "account"
        };
    }

    private static string? ExtractMaskedAccountHint(string? accountNumberMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(accountNumberMetadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(accountNumberMetadataJson);
            var root = document.RootElement;
            string?[] directCandidates =
            [
                TryGetJsonString(root, "iban"),
                TryGetJsonString(root, "number"),
                TryGetJsonString(root, "pan"),
                TryGetJsonString(root, "masked_pan")
            ];

            foreach (var candidate in directCandidates)
            {
                var normalized = ExtractMaskedHintFromValue(candidate);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            if (TryGetJsonProperty(root, "account_number", out var accountNumberNode))
            {
                var fromAccountNumber = ExtractMaskedHintFromValue(TryGetJsonString(accountNumberNode, "number"));
                if (!string.IsNullOrWhiteSpace(fromAccountNumber))
                {
                    return fromAccountNumber;
                }
            }

            if (TryGetJsonProperty(root, "sort_code_account_number", out var sortCodeNode))
            {
                var fromSortCode = ExtractMaskedHintFromValue(TryGetJsonString(sortCodeNode, "account_number"));
                if (!string.IsNullOrWhiteSpace(fromSortCode))
                {
                    return fromSortCode;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out propertyValue))
        {
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.ToString();
        }

        return null;
    }

    private static string? ExtractMaskedHintFromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var alphanumeric = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (alphanumeric.Length < 4)
        {
            return null;
        }

        return alphanumeric[^4..].ToUpperInvariant();
    }

    private async Task<Dictionary<Guid, ConnectionTransactionEnrichmentStats>> BuildConnectionEnrichmentStatsAsync(
        IReadOnlyCollection<EnrichmentConnectionRow> connections,
        CancellationToken cancellationToken)
    {
        if (connections.Count == 0)
        {
            return [];
        }

        var connectionIds = connections.Select(x => x.Id).ToArray();

        var linkedFinancialAccounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.FinancialAccountId.HasValue)
            .Select(x => new
            {
                x.ConnectionId,
                FinancialAccountId = x.FinancialAccountId!.Value
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedFinancialAccounts.Count == 0)
        {
            return [];
        }

        var financialAccountIds = linkedFinancialAccounts
            .Select(x => x.FinancialAccountId)
            .Distinct()
            .ToArray();

        var transactionRows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => financialAccountIds.Contains(x.FinancialAccountId))
            .Select(x => new
            {
                x.FinancialAccountId,
                x.DeterministicClassificationVersion,
                x.DeterministicClassificationTerminal,
                x.DeterministicClassificationStatus,
                x.DeterministicClassificationEvaluatedUtc
            })
            .ToListAsync(cancellationToken);

        var transactionStatsByFinancialAccount = transactionRows
            .GroupBy(x => x.FinancialAccountId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var totalCount = group.Count();
                    var currentRows = group
                        .Where(x =>
                            x.DeterministicClassificationVersion.HasValue
                            && x.DeterministicClassificationVersion.Value >= DeterministicEnrichmentCurrentVersion
                            && x.DeterministicClassificationTerminal)
                        .ToList();
                    var deferredWaitingForCounterpartyCount = group.Count(x =>
                        x.DeterministicClassificationVersion.HasValue
                        && x.DeterministicClassificationVersion.Value >= DeterministicEnrichmentCurrentVersion
                        && x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty);

                    return new
                    {
                        TotalCount = totalCount,
                        CurrentCount = currentRows.Count,
                        DeferredWaitingForCounterpartyCount = deferredWaitingForCounterpartyCount,
                        CurrentEnrichedCountByStartUtc = currentRows
                            .Where(x => x.DeterministicClassificationEvaluatedUtc.HasValue)
                            .Select(x => x.DeterministicClassificationEvaluatedUtc!.Value)
                            .ToList(),
                        LastUpdatedUtc = group
                            .Where(x => x.DeterministicClassificationEvaluatedUtc.HasValue)
                            .Select(x => x.DeterministicClassificationEvaluatedUtc)
                            .Max()
                    };
                });

        var startedByConnectionId = connections.ToDictionary(x => x.Id, x => x.HistoricalEnrichmentStartedUtc);

        var grouped = linkedFinancialAccounts
            .GroupBy(x => x.ConnectionId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var total = 0;
                    var current = 0;
                    var deferredWaitingForCounterparty = 0;
                    var currentEnrichedAfterStart = 0;
                    DateTime? lastUpdatedUtc = null;
                    startedByConnectionId.TryGetValue(group.Key, out var startedUtc);

                    foreach (var linked in group)
                    {
                        if (!transactionStatsByFinancialAccount.TryGetValue(linked.FinancialAccountId, out var stats))
                        {
                            continue;
                        }

                        total += stats.TotalCount;
                        current += stats.CurrentCount;
                        deferredWaitingForCounterparty += stats.DeferredWaitingForCounterpartyCount;
                        if (startedUtc.HasValue)
                        {
                            currentEnrichedAfterStart += stats.CurrentEnrichedCountByStartUtc.Count(x => x >= startedUtc.Value);
                        }

                        lastUpdatedUtc = MaxUtc(lastUpdatedUtc, stats.LastUpdatedUtc);
                    }

                    var stale = Math.Max(0, total - current);

                    return new ConnectionTransactionEnrichmentStats(
                        group.Key,
                        total,
                        current,
                        stale,
                        deferredWaitingForCounterparty,
                        currentEnrichedAfterStart,
                        lastUpdatedUtc);
                });

        return grouped;
    }

    private static bool IsHistoricalEnrichmentCompleted(EnrichmentConnectionRow connection)
    {
        return connection.HistoricalEnrichmentCompletedUtc.HasValue
            && (connection.HistoricalEnrichmentVersion ?? 0) >= DeterministicEnrichmentCurrentVersion
            && !connection.NeedsHistoricalReclassification;
    }

    private static bool IsHistoricalEnrichmentRequired(EnrichmentConnectionRow connection)
    {
        if (connection.NeedsHistoricalReclassification)
        {
            return true;
        }

        if (!connection.HistoricalEnrichmentCompletedUtc.HasValue)
        {
            return true;
        }

        return (connection.HistoricalEnrichmentVersion ?? 0) < DeterministicEnrichmentCurrentVersion;
    }

    private static bool IsHistoricalEnrichmentInProgress(
        EnrichmentConnectionRow connection,
        int remainingCount,
        bool awaitingSync,
        bool required)
    {
        if (remainingCount > 0)
        {
            return true;
        }

        if (required && awaitingSync)
        {
            return true;
        }

        if (required && connection.HistoricalEnrichmentStartedUtc.HasValue)
        {
            return true;
        }

        return connection.HistoricalEnrichmentStartedUtc.HasValue
            && !IsHistoricalEnrichmentCompleted(connection);
    }

    private static bool IsSyncAwaitingRunStatus(string status)
    {
        return status is BankConnectionStatuses.ConnectionStarted
            or BankConnectionStatuses.ConsentInProgress
            or BankConnectionStatuses.ConnectedPendingSync
            or BankConnectionStatuses.SyncPending;
    }

    private static string ResolveHistoricalEnrichmentStage(
        string _connectionStatus,
        bool inProgress,
        bool completed,
        bool required,
        int totalCount,
        int processedCount,
        int remainingCount,
        int deferredWaitingForCounterpartyCount,
        bool awaitingSync,
        DateTime? lastSyncAttemptedUtc,
        DateTime? lastSuccessfulSyncUtc,
        DateTime _updatedUtc)
    {
        if (completed)
        {
            return "completed";
        }

        var hasEverSynced = lastSyncAttemptedUtc.HasValue || lastSuccessfulSyncUtc.HasValue;
        var hasObservedDeterministicProgress = totalCount > 0 || processedCount > 0 || remainingCount > 0;

        if (required && !hasEverSynced && awaitingSync && !hasObservedDeterministicProgress)
        {
            return "waiting_for_first_sync";
        }

        if (required && !inProgress && !awaitingSync)
        {
            return "needs_reclassification";
        }

        if (deferredWaitingForCounterpartyCount > 0 && remainingCount == deferredWaitingForCounterpartyCount)
        {
            return "waiting_for_counterparty";
        }

        if (!inProgress)
        {
            return totalCount > 0 && remainingCount == 0 ? "completed" : "idle";
        }

        if (awaitingSync)
        {
            return "queued_for_sync";
        }

        if (deferredWaitingForCounterpartyCount > 0)
        {
            return "waiting_for_counterparty";
        }

        return "categorizing";
    }

    private static string ResolveOverallHistoricalEnrichmentStage(
        IReadOnlyList<BankEnrichmentConnectionProgressDto> connections,
        bool inProgress,
        bool completed)
    {
        if (completed)
        {
            return "completed";
        }

        if (!inProgress)
        {
            return "idle";
        }

        var hasObservedDeterministicProgress = connections.Any(x =>
            x.TotalCount > 0 || x.ProcessedCount > 0 || x.RemainingCount > 0);

        if (!hasObservedDeterministicProgress && connections.Any(x => x.Stage == "waiting_for_first_sync"))
        {
            return "waiting_for_first_sync";
        }

        if (connections.Any(x => x.Stage == "queued_for_sync"))
        {
            return "queued_for_sync";
        }

        if (connections.Any(x => x.Stage == "needs_reclassification"))
        {
            return "needs_reclassification";
        }

        if (connections.Any(x => x.Stage == "waiting_for_counterparty"))
        {
            return "waiting_for_counterparty";
        }

        return "categorizing";
    }

    private static string MapDeterministicClassificationStatus(DeterministicClassificationStatus status)
    {
        return status switch
        {
            DeterministicClassificationStatus.NotEvaluated => "not_evaluated",
            DeterministicClassificationStatus.Evaluating => "evaluating",
            DeterministicClassificationStatus.ClassifiedMatchedRule => "classified_matched_rule",
            DeterministicClassificationStatus.EvaluatedNoMatchingRule => "evaluated_no_matching_rule",
            DeterministicClassificationStatus.DeferredWaitingForCounterparty => "deferred_waiting_for_counterparty",
            DeterministicClassificationStatus.DeferredWaitingForMoreContext => "deferred_waiting_for_more_context",
            DeterministicClassificationStatus.RejectedAmbiguousMatch => "rejected_ambiguous_match",
            DeterministicClassificationStatus.SupersededRecomputeRequired => "superseded_recompute_required",
            _ => "not_evaluated"
        };
    }

    private static DateTime? MaxUtc(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
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
                    || columnName.Equals("BrandingLastSyncedAtUtc", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("GrantedScopesCsv", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("SupportsInfo", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("SupportsCards", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("SupportsDirectDebits", StringComparison.OrdinalIgnoreCase)
                    || columnName.Equals("SupportsStandingOrders", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var message = postgresException.MessageText ?? string.Empty;
                if (message.Contains("\"ProviderId\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"ProviderIconUri\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"ProviderLogoUri\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"ProviderBrandBgColor\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BrandingLastSyncedAtUtc\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"GrantedScopesCsv\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"SupportsInfo\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"SupportsCards\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"SupportsDirectDebits\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"SupportsStandingOrders\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BankConnectionIdentityInfos\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (current is PostgresException tableException
                && tableException.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                var message = tableException.MessageText ?? string.Empty;
                if (message.Contains("\"BankConnectionIdentityInfos\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool IsExpandedBankingSchemaMissing(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                var tableName = postgresException.TableName ?? string.Empty;
                if (tableName.Equals("LinkedBankCards", StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals("BankCardBalanceSnapshots", StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals("RawBankCardTransactions", StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals("BankDirectDebits", StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals("BankStandingOrders", StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals("BankConnectionIdentityInfos", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var message = postgresException.MessageText ?? string.Empty;
                if (message.Contains("\"LinkedBankCards\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BankCardBalanceSnapshots\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"RawBankCardTransactions\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BankDirectDebits\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BankStandingOrders\"", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\"BankConnectionIdentityInfos\"", StringComparison.OrdinalIgnoreCase))
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




