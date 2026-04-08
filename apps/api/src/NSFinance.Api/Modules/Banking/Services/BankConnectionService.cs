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
    private static readonly TimeSpan SyncingStaleThreshold = TimeSpan.FromMinutes(12);

    private const string SyncLifecyclePhaseConnecting = "connecting";
    private const string SyncLifecyclePhaseImportingBankData = "importing_bank_data";
    private const string SyncLifecyclePhaseImportCompleteEnrichmentQueued = "import_complete_enrichment_queued";
    private const string SyncLifecyclePhaseOrganizingTransactions = "organizing_transactions";
    private const string SyncLifecyclePhaseCompleted = "completed";
    private const string SyncLifecyclePhaseSyncTakingLongerThanExpected = "sync_taking_longer_than_expected";
    private const string SyncLifecyclePhaseAttentionRequired = "attention_required";

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

    private sealed record ConnectionSyncLifecycleResolution(
        string Phase,
        string Reason,
        bool Reconciled,
        bool StaleProtectionApplied);
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
        IReadOnlyList<BankConnectionDto> connections;
        try
        {
            connections = await ListConnectionsWithBrandingAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy projection without branding metadata for userId={UserId}.",
                userId);

            connections = await ListConnectionsWithoutBrandingAsync(userId, cancellationToken);
        }

        return await EnrichConnectionSyncLifecycleAsync(userId, connections, cancellationToken);
    }

    public async Task<BankConnectionDto?> GetConnectionSummaryAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        BankConnectionDto? summary;
        try
        {
            summary = await GetConnectionSummaryWithBrandingAsync(userId, connectionId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy connection summary projection for userId={UserId} connectionId={ConnectionId}.",
                userId,
                connectionId);

            summary = await GetConnectionSummaryWithoutBrandingAsync(userId, connectionId, cancellationToken);
        }

        if (summary is null)
        {
            return null;
        }

        var enriched = await EnrichConnectionSyncLifecycleAsync(userId, [summary], cancellationToken);
        return enriched.Count == 0 ? null : enriched[0];
    }

    public async Task<ConnectedBanksOverviewDto> ListUserVisibleConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        ConnectedBanksOverviewDto overview;
        try
        {
            overview = await ListUserVisibleConnectionsWithBrandingAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderBrandingSchemaMissing(ex))
        {
            logger.LogWarning(
                ex,
                "Provider branding columns are missing from OpenBankingConnections. Falling back to legacy connected-banks projection for userId={UserId}.",
                userId);

            overview = await ListUserVisibleConnectionsWithoutBrandingAsync(userId, cancellationToken);
        }

        var combined = overview.ActiveConnections.Concat(overview.AttentionConnections).ToList();
        var enriched = await EnrichConnectionSyncLifecycleAsync(userId, combined, cancellationToken);
        var enrichedById = enriched.ToDictionary(x => x.Id);

        var active = overview.ActiveConnections
            .Select(x => enrichedById.TryGetValue(x.Id, out var mapped) ? mapped : x)
            .ToList();
        var attention = overview.AttentionConnections
            .Select(x => enrichedById.TryGetValue(x.Id, out var mapped) ? mapped : x)
            .ToList();

        return new ConnectedBanksOverviewDto(active, attention);
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
                Completed: false,
                ProgressPercent: 0d,
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

                var completed = totalCount > 0
                    && remainingCount == 0
                    && IsHistoricalEnrichmentCompleted(connection);
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
        var completedOverall = !inProgressOverall
            && total > 0
            && remaining == 0;
        var percentOverall = total > 0
            ? Math.Round((processed / (double)total) * 100d, 2, MidpointRounding.AwayFromZero)
            : 0d;
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
        var linkedAccounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.ConnectionId == connectionId
                && x.Connection != null
                && x.Connection.UserId == userId
                && x.FinancialAccountId.HasValue)
            .Select(x => new
            {
                FinancialAccountId = x.FinancialAccountId!.Value,
                Provider = x.Connection != null
                    ? (x.Connection.ProviderDisplayName
                       ?? x.Connection.ProviderId
                       ?? x.Connection.ProviderName
                       ?? "unknown_provider")
                    : "unknown_provider"
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var linkedFinancialAccountIds = linkedAccounts
            .Select(x => x.FinancialAccountId)
            .Distinct()
            .ToArray();

        if (linkedFinancialAccountIds.Length == 0)
        {
            return ServiceResult<DeterministicCategorizationDiagnosticsDto>.Fail(
                "Connection not found.",
                "bank_connection_not_found",
                StatusCodes.Status404NotFound);
        }

        var providerByFinancialAccountId = linkedAccounts
            .GroupBy(x => x.FinancialAccountId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Provider).FirstOrDefault() ?? "unknown_provider");

        var sameUserFinancialAccountIds = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == userId)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var fullSameUserCounterpartyUniversePresent = sameUserFinancialAccountIds.Count > 1;

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
                x.Description,
                x.TaxonomyCategoryId,
                x.TaxonomySubcategoryId,
                x.DeterministicClassificationStatus,
                x.DeterministicClassificationTerminal,
                x.DeterministicDeferredRetryEligible,
                x.NeedsDeterministicReclassification,
                x.DeterministicClassificationVersion,
                x.DeterministicClassificationRuleKey,
                x.DeterministicReasonCode,
                x.DeterministicReasonDetailJson,
                x.DeterministicLinkedTransactionId,
                x.DeterministicRelationshipType,
                x.DeterministicRelationshipGroupId,
                x.DeterministicMatchScore,
                x.DeterministicClassificationEvaluatedUtc
            })
            .ToListAsync(cancellationToken);

        var hasCounterpartyAccountsByFinancialAccountId = sameUserFinancialAccountIds
            .Distinct()
            .ToDictionary(
                accountId => accountId,
                accountId => sameUserFinancialAccountIds.Any(other => other != accountId));

        var duplicateClusterStats = rows
            .GroupBy(x =>
                $"{Math.Abs(x.Amount):0.00}|{x.Currency.Trim().ToUpperInvariant()}|{x.BookedAtUtc:yyyy-MM-dd}")
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    ClusterSize = group.Count(),
                    OutflowCount = group.Count(x => x.Amount < 0m),
                    InflowCount = group.Count(x => x.Amount > 0m)
                },
                StringComparer.Ordinal);

        var enrichedRows = rows
            .Select(row =>
            {
                var evidence = ParseDeterministicEvidence(row.DeterministicReasonDetailJson);
                var normalizedDescription = NormalizeDeterministicDescriptionForDiagnostics(row.Description);
                var direction = ResolveDeterministicDirection(row.Amount);
                var amountBucket = ResolveDeterministicAmountBucket(row.Amount);
                var deterministicSemanticFamily = ResolveDeterministicSemanticFamily(
                    row.DeterministicClassificationStatus,
                    row.DeterministicRelationshipType);
                var stylingFromDeterministicSemantic = !string.Equals(deterministicSemanticFamily, "none", StringComparison.Ordinal);
                var taxonomyFallbackUsed = !stylingFromDeterministicSemantic
                    && (row.TaxonomyCategoryId.HasValue || row.TaxonomySubcategoryId.HasValue);
                var amountKey = $"{Math.Abs(row.Amount):0.00}|{row.Currency.Trim().ToUpperInvariant()}|{row.BookedAtUtc:yyyy-MM-dd}";
                duplicateClusterStats.TryGetValue(amountKey, out var duplicateClusterStat);
                var duplicateClusterMember = duplicateClusterStat is not null
                    && duplicateClusterStat.OutflowCount > 1
                    && duplicateClusterStat.InflowCount > 1;
                var duplicateClusterSize = duplicateClusterMember ? duplicateClusterStat!.ClusterSize : 0;
                hasCounterpartyAccountsByFinancialAccountId.TryGetValue(row.FinancialAccountId, out var hasCounterpartyAccounts);
                var fullCounterpartyUniverseForRow = fullSameUserCounterpartyUniversePresent && hasCounterpartyAccounts;
                var provider = providerByFinancialAccountId.TryGetValue(row.FinancialAccountId, out var mappedProvider)
                    ? mappedProvider
                    : "unknown_provider";
                var candidateCounterpartCount = rows.Count(candidate =>
                    candidate.Id != row.Id
                    && candidate.FinancialAccountId != row.FinancialAccountId
                    && candidate.Currency.Equals(row.Currency, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(candidate.Amount) == Math.Abs(row.Amount)
                    && ((candidate.Amount < 0m) != (row.Amount < 0m))
                    && Math.Abs((candidate.BookedAtUtc - row.BookedAtUtc).TotalHours) <= DeterministicCategorizationConstants.TransferCandidateWindowHours);
                var hasPlausibleCounterpartCandidates = candidateCounterpartCount > 0;
                var hasTransferSignals = evidence.HasTransferSignals || ContainsTransferDiagnosticsSignal(normalizedDescription);
                var hasSavingsSignals = evidence.HasSavingsSignals || ContainsSavingsDiagnosticsSignal(normalizedDescription);
                var cooccurredNearbyMerchantSpend = rows.Any(candidate =>
                    candidate.Id != row.Id
                    && candidate.FinancialAccountId == row.FinancialAccountId
                    && candidate.Amount < 0m
                    && Math.Abs((row.BookedAtUtc - candidate.BookedAtUtc).TotalHours) <= 6d
                    && !ContainsTransferDiagnosticsSignal(NormalizeDeterministicDescriptionForDiagnostics(candidate.Description))
                    && !ContainsSavingsDiagnosticsSignal(NormalizeDeterministicDescriptionForDiagnostics(candidate.Description))
                    && Math.Abs(candidate.Amount) > Math.Max(1m, Math.Abs(row.Amount)));
                var candidateFamily = ResolveCandidateFamily(row.DeterministicClassificationRuleKey, evidence.Family);
                var savingsEvaluationOutcome = ResolveSavingsEvaluationOutcome(
                    candidateFamily,
                    row.DeterministicClassificationStatus,
                    row.DeterministicReasonCode,
                    evidence.SavingsRoutingAllowed);
                var deferredOnCounterpartyExpectation =
                    row.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty
                    && row.DeterministicReasonCode is DeterministicClassificationReasonCodes.DeferredMissingCounterparty
                        or "deferred_strong_savings_missing_counterparty";
                var waitingForFutureDataPlausible = IsWaitingForFutureDataPlausible(
                    row.DeterministicClassificationStatus,
                    row.DeterministicReasonCode,
                    row.BookedAtUtc,
                    fullCounterpartyUniverseForRow);
                var nonTerminalExplanation = ResolveNonTerminalExplanation(
                    row.DeterministicClassificationStatus,
                    row.DeterministicReasonCode,
                    row.DeterministicClassificationTerminal,
                    waitingForFutureDataPlausible,
                    fullCounterpartyUniverseForRow);

                return new
                {
                    row.Id,
                    row.FinancialAccountId,
                    row.Amount,
                    row.Currency,
                    row.BookedAtUtc,
                    NormalizedDescription = normalizedDescription,
                    row.DeterministicReasonDetailJson,
                    Provider = provider,
                    row.TaxonomyCategoryId,
                    row.TaxonomySubcategoryId,
                    row.DeterministicClassificationStatus,
                    row.DeterministicClassificationTerminal,
                    row.DeterministicDeferredRetryEligible,
                    row.NeedsDeterministicReclassification,
                    row.DeterministicClassificationVersion,
                    row.DeterministicClassificationRuleKey,
                    row.DeterministicReasonCode,
                    row.DeterministicLinkedTransactionId,
                    row.DeterministicRelationshipType,
                    row.DeterministicRelationshipGroupId,
                    row.DeterministicMatchScore,
                    row.DeterministicClassificationEvaluatedUtc,
                    Direction = direction,
                    AmountBucket = amountBucket,
                    DuplicateClusterMember = duplicateClusterMember,
                    DuplicateClusterSize = duplicateClusterSize,
                    HasCounterpartyAccounts = hasCounterpartyAccounts,
                    FullSameUserCounterpartyUniversePresent = fullCounterpartyUniverseForRow,
                    CandidateCounterpartCount = candidateCounterpartCount,
                    HasPlausibleCounterpartCandidates = hasPlausibleCounterpartCandidates,
                    CandidateFamily = candidateFamily,
                    TopCandidateTransactionId = evidence.TopCandidateTransactionId,
                    TopCandidateScore = evidence.TopCandidateScore,
                    HasTransferSignals = hasTransferSignals,
                    HasSavingsSignals = hasSavingsSignals,
                    CooccurredWithNearbyMerchantSpend = cooccurredNearbyMerchantSpend,
                    DeferredOnCounterpartyExpectation = deferredOnCounterpartyExpectation,
                    SavingsRoutingAllowed = evidence.SavingsRoutingAllowed,
                    SavingsRoutingTier = evidence.SavingsRoutingTier,
                    SavingsProviderStructuralSupport = evidence.SavingsProviderStructuralSupport,
                    SavingsContextualSupport = evidence.SavingsContextualSupport,
                    SavingsRepetitionStrength = evidence.SavingsRepetitionStrength,
                    SavingsExternalCounterpartyRisk = evidence.SavingsExternalCounterpartyRisk,
                    SavingsAmountRiskModifier = evidence.SavingsAmountRiskModifier,
                    SavingsEvaluationOutcome = savingsEvaluationOutcome,
                    TransferTimePrecisionMode = evidence.TransferTimePrecisionMode,
                    TransferStableOrderingUsed = evidence.TransferStableOrderingUsed,
                    TransferTieBreakReason = evidence.TransferTieBreakReason,
                    TransferHasHighConfidenceReferenceOverlap = evidence.TransferHasHighConfidenceReferenceOverlap,
                    TransferNamesOnlyWeakSupport = evidence.TransferNamesOnlyWeakSupport,
                    TransferRoutingInitiallyBlockedExternalCounterpartyRisk = evidence.TransferRoutingInitiallyBlockedExternalCounterpartyRisk,
                    TransferSameUserCandidateUniverseOverrideApplied = evidence.TransferSameUserCandidateUniverseOverrideApplied,
                    TransferHighConfidenceInboundReferencesPresent = evidence.TransferHighConfidenceInboundReferencesPresent,
                    WaitingForFutureDataPlausible = waitingForFutureDataPlausible,
                    NonTerminalExplanation = nonTerminalExplanation,
                    DeterministicSemanticFamily = deterministicSemanticFamily,
                    StylingFromDeterministicSemantic = stylingFromDeterministicSemantic,
                    TaxonomyFallbackUsed = taxonomyFallbackUsed
                };
            })
            .ToList();

        var statusCounts = rows
            .GroupBy(x => x.DeterministicClassificationStatus)
            .Select(group => new DeterministicCategorizationStatusCountDto(
                MapDeterministicClassificationStatus(group.Key),
                group.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Status, StringComparer.Ordinal)
            .ToList();

        var unresolvedRows = enrichedRows
            .Where(x =>
                !x.DeterministicClassificationTerminal
                || x.DeterministicClassificationStatus == DeterministicClassificationStatus.RejectedAmbiguousMatch)
            .ToList();

        var unresolvedBreakdown = unresolvedRows
            .GroupBy(x => new
            {
                Status = MapDeterministicClassificationStatus(x.DeterministicClassificationStatus),
                x.DeterministicReasonCode,
                x.CandidateFamily,
                x.Provider,
                x.FinancialAccountId,
                x.Direction,
                x.AmountBucket,
                x.DuplicateClusterMember,
                x.HasCounterpartyAccounts,
                x.HasPlausibleCounterpartCandidates,
                x.DeferredOnCounterpartyExpectation,
                x.SavingsRoutingAllowed,
                x.SavingsRoutingTier,
                x.SavingsProviderStructuralSupport,
                x.SavingsContextualSupport,
                x.SavingsRepetitionStrength,
                x.SavingsExternalCounterpartyRisk,
                x.SavingsAmountRiskModifier
            })
            .Select(group => new DeterministicDiagnosticsBreakdownDto(
                group.Key.Status,
                group.Key.DeterministicReasonCode,
                group.Key.CandidateFamily,
                group.Key.Provider,
                group.Key.FinancialAccountId,
                group.Key.Direction,
                group.Key.AmountBucket,
                group.Key.DuplicateClusterMember,
                group.Key.HasCounterpartyAccounts,
                group.Key.HasPlausibleCounterpartCandidates,
                group.Key.DeferredOnCounterpartyExpectation,
                group.Key.SavingsRoutingAllowed,
                group.Key.SavingsRoutingTier,
                group.Key.SavingsProviderStructuralSupport,
                group.Key.SavingsContextualSupport,
                group.Key.SavingsRepetitionStrength,
                group.Key.SavingsExternalCounterpartyRisk,
                group.Key.SavingsAmountRiskModifier,
                group.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Status, StringComparer.Ordinal)
            .ThenBy(x => x.CandidateFamily, StringComparer.Ordinal)
            .ToList();

        var sampleRows = enrichedRows
            .Where(x =>
                !x.DeterministicClassificationTerminal
                || x.DeterministicClassificationStatus == DeterministicClassificationStatus.RejectedAmbiguousMatch
                || string.Equals(x.CandidateFamily, "bank_account_transfer", StringComparison.Ordinal)
                || string.Equals(x.DeterministicRelationshipType, "internal_transfer", StringComparison.Ordinal))
            .ToList();

        var sampleDecisions = sampleRows
            .OrderByDescending(x => x.DeterministicClassificationEvaluatedUtc)
            .ThenByDescending(x => x.CandidateCounterpartCount)
            .Take(120)
            .Select(x => new DeterministicTransactionDecisionDto(
                x.Id,
                x.FinancialAccountId,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                x.NormalizedDescription,
                x.Provider,
                MapDeterministicClassificationStatus(x.DeterministicClassificationStatus),
                x.DeterministicClassificationTerminal,
                x.DeterministicDeferredRetryEligible,
                x.DeterministicClassificationVersion,
                x.DeterministicClassificationRuleKey,
                x.DeterministicReasonCode,
                x.CandidateFamily,
                x.Direction,
                x.AmountBucket,
                x.DuplicateClusterMember,
                x.DuplicateClusterSize,
                x.HasCounterpartyAccounts,
                x.FullSameUserCounterpartyUniversePresent,
                x.CandidateCounterpartCount,
                x.TopCandidateTransactionId,
                x.TopCandidateScore,
                x.HasSavingsSignals,
                x.HasTransferSignals,
                x.CooccurredWithNearbyMerchantSpend,
                x.HasPlausibleCounterpartCandidates,
                x.DeferredOnCounterpartyExpectation,
                x.SavingsRoutingAllowed,
                x.SavingsRoutingTier,
                x.SavingsProviderStructuralSupport,
                x.SavingsContextualSupport,
                x.SavingsRepetitionStrength,
                x.SavingsExternalCounterpartyRisk,
                x.SavingsAmountRiskModifier,
                x.SavingsEvaluationOutcome,
                x.TransferTimePrecisionMode,
                x.TransferStableOrderingUsed,
                x.TransferTieBreakReason,
                x.TransferHasHighConfidenceReferenceOverlap,
                x.TransferNamesOnlyWeakSupport,
                x.TransferRoutingInitiallyBlockedExternalCounterpartyRisk,
                x.TransferSameUserCandidateUniverseOverrideApplied,
                x.TransferHighConfidenceInboundReferencesPresent,
                x.WaitingForFutureDataPlausible,
                x.NonTerminalExplanation,
                x.DeterministicSemanticFamily,
                x.StylingFromDeterministicSemantic,
                x.TaxonomyFallbackUsed,
                x.TaxonomyCategoryId,
                x.TaxonomySubcategoryId,
                x.DeterministicReasonDetailJson,
                x.DeterministicLinkedTransactionId,
                x.DeterministicRelationshipType,
                x.DeterministicRelationshipGroupId,
                x.DeterministicMatchScore,
                x.DeterministicClassificationEvaluatedUtc))
            .ToList();

        var terminalCount = enrichedRows.Count(x => x.DeterministicClassificationTerminal);
        var deferredRows = enrichedRows
            .Where(x =>
                x.DeterministicClassificationStatus is DeterministicClassificationStatus.DeferredWaitingForCounterparty
                    or DeterministicClassificationStatus.DeferredWaitingForMoreContext)
            .ToList();
        var deferredMoreContextCount = deferredRows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext);
        var deferredCounterpartyCount = deferredRows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty);
        var deferredRemainingCount = 0;
        var actionableRemainingCount = 0;
        var rowsRemainingTotal = 0;
        var deferredReadyForTerminalizationCount = 0;
        foreach (var row in rows)
        {
            var versionBehind = !row.DeterministicClassificationVersion.HasValue
                || row.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion;
            var remaining = row.NeedsDeterministicReclassification
                || versionBehind
                || !row.DeterministicClassificationTerminal
                || row.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired;
            if (!remaining)
            {
                continue;
            }

            var deferredCounterpartyCurrent = row.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty
                && !versionBehind
                && !row.NeedsDeterministicReclassification;
            var deferredMoreContextCurrent = row.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext
                && !versionBehind
                && !row.NeedsDeterministicReclassification;
            rowsRemainingTotal++;
            if (!deferredCounterpartyCurrent && !deferredMoreContextCurrent)
            {
                actionableRemainingCount++;
                continue;
            }

            deferredRemainingCount++;
            var fullUniverseForRow = fullSameUserCounterpartyUniversePresent && hasCounterpartyAccountsByFinancialAccountId.TryGetValue(row.FinancialAccountId, out var hasCounterparty)
                && hasCounterparty;
            var waitingPlausible = IsWaitingForFutureDataPlausible(
                row.DeterministicClassificationStatus,
                row.DeterministicReasonCode,
                row.BookedAtUtc,
                fullUniverseForRow);
            if (!waitingPlausible)
            {
                deferredReadyForTerminalizationCount++;
                actionableRemainingCount++;
            }
        }

        var rejectedAmbiguousCount = rows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.RejectedAmbiguousMatch);
        var evaluatedNoMatchingRuleCount = rows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.EvaluatedNoMatchingRule);
        var notEvaluatedCount = rows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.NotEvaluated);
        var evaluatingCount = rows.Count(x =>
            x.DeterministicClassificationStatus == DeterministicClassificationStatus.Evaluating);

        var topDeferredReasonCodes = deferredRows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.DeterministicReasonCode) ? "unknown_reason" : x.DeterministicReasonCode!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DeterministicKeyCountDto(group.Key, group.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(6)
            .ToList();
        var topDeferredFamilies = deferredRows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CandidateFamily) ? "unknown_family" : x.CandidateFamily, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DeterministicKeyCountDto(group.Key, group.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(6)
            .ToList();

        var queueEligible = actionableRemainingCount > 0;
        var queueEligibilityReason = queueEligible
            ? "actionable_remaining_rows"
            : rowsRemainingTotal > 0
                ? "deferred_only_remaining_rows"
                : "no_remaining_rows";
        var continuationDecision = queueEligible ? "continue" : "stop";
        var continuationReason = queueEligible
            ? "actionable_remaining_rows"
            : rowsRemainingTotal > 0
                ? "deferred_only_remaining_rows"
                : "no_remaining_rows";
        var remainingWorkClassification = rowsRemainingTotal == 0
            ? "no_remaining_rows"
            : deferredReadyForTerminalizationCount > 0
                ? "ready_for_terminalization_downgrade"
                : queueEligible
                    ? "actively_runnable"
                    : deferredRemainingCount > 0 && deferredRemainingCount == rowsRemainingTotal
                        ? "waiting_on_future_data"
                        : "partially_blocked_remaining";

        return ServiceResult<DeterministicCategorizationDiagnosticsDto>.Ok(
            new DeterministicCategorizationDiagnosticsDto(
                connectionId,
                DeterministicEnrichmentCurrentVersion,
                rows.Count,
                terminalCount,
                Math.Max(0, rows.Count - terminalCount),
                rowsRemainingTotal,
                actionableRemainingCount,
                deferredRemainingCount,
                deferredMoreContextCount,
                deferredCounterpartyCount,
                Math.Max(0, deferredRemainingCount - deferredReadyForTerminalizationCount),
                deferredReadyForTerminalizationCount,
                rejectedAmbiguousCount,
                evaluatedNoMatchingRuleCount,
                notEvaluatedCount,
                evaluatingCount,
                fullSameUserCounterpartyUniversePresent,
                remainingWorkClassification,
                queueEligible,
                queueEligibilityReason,
                continuationDecision,
                continuationReason,
                topDeferredReasonCodes,
                topDeferredFamilies,
                statusCounts,
                unresolvedBreakdown,
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

    private async Task<IReadOnlyList<BankConnectionDto>> EnrichConnectionSyncLifecycleAsync(
        Guid userId,
        IReadOnlyList<BankConnectionDto> connections,
        CancellationToken cancellationToken)
    {
        if (connections.Count == 0)
        {
            return connections;
        }

        var connectionIds = connections
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        var linkedAccountCounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId))
            .GroupBy(x => x.ConnectionId)
            .Select(group => new
            {
                ConnectionId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(x => x.ConnectionId, x => x.Count, cancellationToken);

        var linkedFinancialAccountRows = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.FinancialAccountId.HasValue)
            .Select(x => new
            {
                x.ConnectionId,
                FinancialAccountId = x.FinancialAccountId!.Value
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var importedTransactionCountByConnectionId = new Dictionary<Guid, int>();
        if (linkedFinancialAccountRows.Count > 0)
        {
            var financialAccountIds = linkedFinancialAccountRows
                .Select(x => x.FinancialAccountId)
                .Distinct()
                .ToArray();

            var transactionCountByFinancialAccountId = await dbContext.Transactions
                .AsNoTracking()
                .Where(x => financialAccountIds.Contains(x.FinancialAccountId))
                .GroupBy(x => x.FinancialAccountId)
                .Select(group => new
                {
                    FinancialAccountId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(x => x.FinancialAccountId, x => x.Count, cancellationToken);

            importedTransactionCountByConnectionId = linkedFinancialAccountRows
                .GroupBy(x => x.ConnectionId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(linked =>
                        transactionCountByFinancialAccountId.TryGetValue(linked.FinancialAccountId, out var count)
                            ? count
                            : 0));
        }

        var enrichmentProgress = await GetEnrichmentProgressAsync(userId, cancellationToken);
        var enrichmentByConnectionId = enrichmentProgress.Connections
            .ToDictionary(x => x.ConnectionId);

        var nowUtc = DateTime.UtcNow;

        return connections
            .Select(connection =>
            {
                linkedAccountCounts.TryGetValue(connection.Id, out var linkedAccountCount);
                enrichmentByConnectionId.TryGetValue(connection.Id, out var enrichment);
                importedTransactionCountByConnectionId.TryGetValue(connection.Id, out var importedTransactionCount);

                var enrichmentStage = enrichment?.Stage;
                var resolution = ResolveConnectionSyncLifecyclePhase(
                    connection.Status,
                    connection.LastSyncAttemptedUtc,
                    connection.LastSuccessfulSyncUtc,
                    connection.UpdatedUtc,
                    linkedAccountCount,
                    importedTransactionCount,
                    enrichmentStage,
                    nowUtc);

                return connection with
                {
                    SyncLifecyclePhase = resolution.Phase,
                    SyncLifecycleReason = resolution.Reason,
                    SyncEnrichmentStage = enrichmentStage,
                    LinkedAccountCount = linkedAccountCount,
                    ImportedTransactionCount = importedTransactionCount,
                    SyncStateReconciled = resolution.Reconciled,
                    SyncStateStaleProtectionApplied = resolution.StaleProtectionApplied
                };
            })
            .ToList();
    }

    private static ConnectionSyncLifecycleResolution ResolveConnectionSyncLifecyclePhase(
        string status,
        DateTime? lastSyncAttemptedUtc,
        DateTime? lastSuccessfulSyncUtc,
        DateTime connectionUpdatedUtc,
        int linkedAccountCount,
        int importedTransactionCount,
        string? enrichmentStage,
        DateTime nowUtc)
    {
        var normalizedStage = string.IsNullOrWhiteSpace(enrichmentStage)
            ? null
            : enrichmentStage.Trim().ToLowerInvariant();
        var hasLinkedAccounts = linkedAccountCount > 0;
        var hasImportedTransactions = importedTransactionCount > 0;
        var hasAnySyncEvidence = lastSyncAttemptedUtc.HasValue || lastSuccessfulSyncUtc.HasValue;
        var hasPostImportEvidence = hasLinkedAccounts && (hasImportedTransactions || hasAnySyncEvidence);

        var stageIsQueued = normalizedStage is "queued_for_sync" or "needs_reclassification" or "waiting_for_first_sync";
        var stageIsOrganizing = normalizedStage is "categorizing" or "waiting_for_counterparty";
        var stageIsCompleted = normalizedStage is "completed";

        var statusIsSyncingLike = status is BankConnectionStatuses.ConnectedPendingSync
            or BankConnectionStatuses.SyncPending
            or BankConnectionStatuses.Connected;

        var syncReferenceUtc = MaxUtc(MaxUtc(lastSuccessfulSyncUtc, lastSyncAttemptedUtc), connectionUpdatedUtc);
        var staleSyncPending =
            statusIsSyncingLike
            && syncReferenceUtc.HasValue
            && nowUtc - syncReferenceUtc.Value >= SyncingStaleThreshold;

        if (status is BankConnectionStatuses.ConnectionStarted or BankConnectionStatuses.ConsentInProgress)
        {
            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseConnecting,
                "connection_authorization_in_progress",
                Reconciled: false,
                StaleProtectionApplied: false);
        }

        if (status is BankConnectionStatuses.ReauthRequired
            or BankConnectionStatuses.Expired
            or BankConnectionStatuses.Revoked
            or BankConnectionStatuses.DisconnectFailed
            or BankConnectionStatuses.DisconnectPending
            or BankConnectionStatuses.Failed)
        {
            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseAttentionRequired,
                "connection_requires_attention",
                Reconciled: false,
                StaleProtectionApplied: false);
        }

        if (stageIsCompleted || status == BankConnectionStatuses.Synced)
        {
            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseCompleted,
                stageIsCompleted ? "enrichment_stage_completed" : "connection_status_synced",
                Reconciled: statusIsSyncingLike && status != BankConnectionStatuses.Synced,
                StaleProtectionApplied: staleSyncPending);
        }

        if (stageIsOrganizing && hasPostImportEvidence)
        {
            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseOrganizingTransactions,
                $"enrichment_stage_{normalizedStage}",
                Reconciled: statusIsSyncingLike,
                StaleProtectionApplied: staleSyncPending);
        }

        if (stageIsQueued && hasPostImportEvidence)
        {
            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseImportCompleteEnrichmentQueued,
                $"enrichment_stage_{normalizedStage}",
                Reconciled: statusIsSyncingLike,
                StaleProtectionApplied: staleSyncPending);
        }

        if (statusIsSyncingLike)
        {
            if (hasPostImportEvidence)
            {
                return new ConnectionSyncLifecycleResolution(
                    SyncLifecyclePhaseImportCompleteEnrichmentQueued,
                    staleSyncPending
                        ? "stale_sync_status_reconciled_post_import"
                        : "import_evidence_present_waiting_for_enrichment",
                    Reconciled: true,
                    StaleProtectionApplied: staleSyncPending);
            }

            if (staleSyncPending)
            {
                return new ConnectionSyncLifecycleResolution(
                    SyncLifecyclePhaseSyncTakingLongerThanExpected,
                    "stale_sync_without_import_evidence",
                    Reconciled: true,
                    StaleProtectionApplied: true);
            }

            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseImportingBankData,
                "awaiting_initial_import",
                Reconciled: false,
                StaleProtectionApplied: false);
        }

        if (hasPostImportEvidence)
        {
            return new ConnectionSyncLifecycleResolution(
                SyncLifecyclePhaseCompleted,
                "post_import_evidence_without_syncing_status",
                Reconciled: false,
                StaleProtectionApplied: false);
        }

        return new ConnectionSyncLifecycleResolution(
            SyncLifecyclePhaseConnecting,
            "default_connecting_fallback",
            Reconciled: false,
            StaleProtectionApplied: false);
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

    private sealed record DeterministicEvidenceParseResult(
        string? Family,
        Guid? TopCandidateTransactionId,
        int? TopCandidateScore,
        bool HasTransferSignals,
        bool HasSavingsSignals,
        string? TransferTimePrecisionMode,
        bool TransferStableOrderingUsed,
        string? TransferTieBreakReason,
        bool TransferHasHighConfidenceReferenceOverlap,
        bool TransferNamesOnlyWeakSupport,
        bool TransferRoutingInitiallyBlockedExternalCounterpartyRisk,
        bool TransferSameUserCandidateUniverseOverrideApplied,
        bool TransferHighConfidenceInboundReferencesPresent,
        bool SavingsRoutingAllowed,
        string? SavingsRoutingTier,
        bool SavingsProviderStructuralSupport,
        bool SavingsContextualSupport,
        int SavingsRepetitionStrength,
        bool SavingsExternalCounterpartyRisk,
        int SavingsAmountRiskModifier);

    private static DeterministicEvidenceParseResult ParseDeterministicEvidence(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return new DeterministicEvidenceParseResult(null, null, null, false, false, null, false, null, false, false, false, false, false, false, null, false, false, 0, false, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            var root = document.RootElement;
            var family = TryReadDiagnosticsJsonString(root, "family");
            var candidateIdText = TryReadDiagnosticsJsonString(root, "candidateId")
                ?? TryReadDiagnosticsJsonString(root, "topCandidateId");
            Guid? topCandidateId = null;
            if (!string.IsNullOrWhiteSpace(candidateIdText) && Guid.TryParse(candidateIdText, out var parsedCandidateId))
            {
                topCandidateId = parsedCandidateId;
            }

            var topCandidateScore = TryReadDiagnosticsJsonInt(root, "bestScore")
                ?? TryReadDiagnosticsJsonInt(root, "topCandidateScore")
                ?? TryReadDiagnosticsJsonInt(root, "score");
            var transferSignals = TryReadDiagnosticsJsonBool(root, "transferKeyword")
                || TryReadDiagnosticsJsonBool(root, "hasTransferKeyword")
                || string.Equals(family, "bank_account_transfer", StringComparison.Ordinal);
            var savingsSignals = TryReadDiagnosticsJsonBool(root, "savingsKeyword")
                || TryReadDiagnosticsJsonBool(root, "strongSignal")
                || string.Equals(family, "savings_transfer", StringComparison.Ordinal);
            var transferTimePrecisionMode = TryReadDiagnosticsJsonString(root, "timePrecisionMode");
            var transferStableOrderingUsed = TryReadDiagnosticsJsonBool(root, "stableOrderingUsed");
            var transferTieBreakReason = TryReadDiagnosticsJsonString(root, "finalTieBreakReason")
                                         ?? TryReadDiagnosticsJsonString(root, "tieBreakReason");
            var transferHighConfidenceOverlap = (TryReadDiagnosticsJsonInt(root, "topCandidateReferenceOverlap") ?? 0) > 0
                                                || (TryReadDiagnosticsJsonInt(root, "bestReferenceOverlap") ?? 0) > 0
                                                || (TryReadNestedDiagnosticsJsonInt(root, "referenceOverlapSummary", "highConfidence") ?? 0) > 0;
            var transferNamesOnlyWeakSupport = TryReadDiagnosticsJsonBool(root, "topCandidateWeakNamesOnlySupport")
                                               || TryReadDiagnosticsJsonBool(root, "bestWeakNamesOnlySupport")
                                               || TryReadDiagnosticsJsonBool(root, "weakNameSupportOnly")
                                               || TryReadNestedDiagnosticsJsonBool(root, "referenceOverlapSummary", "namesOnlyWeakSupport");
            var transferRoutingInitiallyBlockedExternalCounterpartyRisk =
                TryReadDiagnosticsJsonBool(root, "routingInitiallyBlockedExternalCounterpartyRisk");
            var transferSameUserCandidateUniverseOverrideApplied =
                TryReadDiagnosticsJsonBool(root, "sameUserCandidateUniverseOverrideApplied");
            var transferHighConfidenceInboundReferencesPresent =
                TryReadDiagnosticsJsonBool(root, "highConfidenceInboundReferencesPresent");
            var savingsRoutingAllowed = TryReadDiagnosticsJsonBool(root, "savingsRoutingAllowed");
            var savingsRoutingTier = TryReadDiagnosticsJsonString(root, "savingsRoutingTier");
            var savingsProviderStructuralSupport = TryReadDiagnosticsJsonBool(root, "providerStructuralSupport");
            var savingsContextualSupport = TryReadDiagnosticsJsonBool(root, "contextualSupport");
            var savingsRepetitionStrength = TryReadDiagnosticsJsonInt(root, "repetitionStrength") ?? 0;
            var savingsExternalCounterpartyRisk = TryReadDiagnosticsJsonBool(root, "externalCounterpartyRisk");
            var savingsAmountRiskModifier = TryReadDiagnosticsJsonInt(root, "amountRiskModifier") ?? 0;

            return new DeterministicEvidenceParseResult(
                family,
                topCandidateId,
                topCandidateScore,
                transferSignals,
                savingsSignals,
                transferTimePrecisionMode,
                transferStableOrderingUsed,
                transferTieBreakReason,
                transferHighConfidenceOverlap,
                transferNamesOnlyWeakSupport,
                transferRoutingInitiallyBlockedExternalCounterpartyRisk,
                transferSameUserCandidateUniverseOverrideApplied,
                transferHighConfidenceInboundReferencesPresent,
                savingsRoutingAllowed,
                savingsRoutingTier,
                savingsProviderStructuralSupport,
                savingsContextualSupport,
                savingsRepetitionStrength,
                savingsExternalCounterpartyRisk,
                savingsAmountRiskModifier);
        }
        catch (JsonException)
        {
            return new DeterministicEvidenceParseResult(null, null, null, false, false, null, false, null, false, false, false, false, false, false, null, false, false, 0, false, 0);
        }
    }

    private static string ResolveCandidateFamily(string? ruleKey, string? evidenceFamily)
    {
        if (!string.IsNullOrWhiteSpace(evidenceFamily))
        {
            return evidenceFamily;
        }

        var normalizedRuleKey = ruleKey?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedRuleKey))
        {
            return "none";
        }

        if (normalizedRuleKey.Contains("savings_transfer", StringComparison.Ordinal))
        {
            return "savings_transfer";
        }

        if (normalizedRuleKey.Contains("bank_transfer", StringComparison.Ordinal)
            || normalizedRuleKey.Contains("internal_transfer", StringComparison.Ordinal))
        {
            return "bank_account_transfer";
        }

        return "none";
    }

    private static string ResolveDeterministicSemanticFamily(
        DeterministicClassificationStatus status,
        string? deterministicRelationshipType)
    {
        if (status != DeterministicClassificationStatus.ClassifiedMatchedRule)
        {
            return "none";
        }

        if (string.Equals(deterministicRelationshipType, "internal_transfer", StringComparison.Ordinal))
        {
            return "internal_transfer";
        }

        if (string.Equals(deterministicRelationshipType, "savings_transfer", StringComparison.Ordinal))
        {
            return "savings_transfer";
        }

        return "none";
    }

    private static string ResolveSavingsEvaluationOutcome(
        string candidateFamily,
        DeterministicClassificationStatus status,
        string? reasonCode,
        bool savingsRoutingAllowed)
    {
        if (!savingsRoutingAllowed && candidateFamily != "savings_transfer")
        {
            return "not_evaluated";
        }

        if (status == DeterministicClassificationStatus.ClassifiedMatchedRule
            && string.Equals(candidateFamily, "savings_transfer", StringComparison.Ordinal))
        {
            return "classified";
        }

        if (string.Equals(reasonCode, DeterministicClassificationReasonCodes.SavingsRejectedInsufficientContext, StringComparison.Ordinal))
        {
            return "blocked_insufficient_context";
        }

        if (!savingsRoutingAllowed)
        {
            return "blocked_routing";
        }

        return "evaluated_not_classified";
    }

    private static bool IsWaitingForFutureDataPlausible(
        DeterministicClassificationStatus status,
        string? reasonCode,
        DateTime bookedAtUtc,
        bool fullCounterpartyUniversePresent)
    {
        var now = DateTime.UtcNow;
        var ageHours = Math.Max(0d, (now - bookedAtUtc).TotalHours);

        return status switch
        {
            DeterministicClassificationStatus.DeferredWaitingForCounterparty =>
                !fullCounterpartyUniversePresent
                && string.Equals(reasonCode, DeterministicClassificationReasonCodes.DeferredMissingCounterparty, StringComparison.Ordinal)
                && ageHours < 48d,
            DeterministicClassificationStatus.DeferredWaitingForMoreContext =>
                string.Equals(reasonCode, DeterministicClassificationReasonCodes.DeferredPendingBookedContext, StringComparison.Ordinal)
                && ageHours < 24d,
            _ => false
        };
    }

    private static string ResolveNonTerminalExplanation(
        DeterministicClassificationStatus status,
        string? reasonCode,
        bool terminal,
        bool waitingForFutureDataPlausible,
        bool fullCounterpartyUniversePresent)
    {
        if (terminal)
        {
            return "terminal";
        }

        if (status == DeterministicClassificationStatus.NotEvaluated)
        {
            return "not_evaluated_yet";
        }

        if (status == DeterministicClassificationStatus.Evaluating)
        {
            return "currently_evaluating";
        }

        if (waitingForFutureDataPlausible)
        {
            return status == DeterministicClassificationStatus.DeferredWaitingForMoreContext
                ? "waiting_for_posted_context"
                : "waiting_for_missing_counterparty_data";
        }

        if (status == DeterministicClassificationStatus.DeferredWaitingForCounterparty && fullCounterpartyUniversePresent)
        {
            return "deferred_invalid_full_counterparty_universe_present";
        }

        if (status == DeterministicClassificationStatus.DeferredWaitingForCounterparty
            || status == DeterministicClassificationStatus.DeferredWaitingForMoreContext)
        {
            return $"deferred_requires_terminalization:{reasonCode ?? "unknown_reason"}";
        }

        return "non_terminal_unclassified";
    }

    private static string NormalizeDeterministicDescriptionForDiagnostics(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var lowered = description.Trim().ToLowerInvariant();
        var normalizedChars = lowered
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();
        return string.Join(
            ' ',
            new string(normalizedChars)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool ContainsTransferDiagnosticsSignal(string normalizedDescription)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return false;
        }

        return normalizedDescription.Contains("internal transfer", StringComparison.Ordinal)
            || normalizedDescription.Contains("bank transfer", StringComparison.Ordinal)
            || normalizedDescription.Contains("transfer", StringComparison.Ordinal)
            || normalizedDescription.Contains("xfer", StringComparison.Ordinal)
            || normalizedDescription.Contains("faster", StringComparison.Ordinal);
    }

    private static bool ContainsSavingsDiagnosticsSignal(string normalizedDescription)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return false;
        }

        return normalizedDescription.Contains("savings", StringComparison.Ordinal)
            || normalizedDescription.Contains("vault", StringComparison.Ordinal)
            || normalizedDescription.Contains("pocket", StringComparison.Ordinal)
            || normalizedDescription.Contains("round up", StringComparison.Ordinal)
            || normalizedDescription.Contains("roundup", StringComparison.Ordinal)
            || normalizedDescription.Contains("spare change", StringComparison.Ordinal)
            || normalizedDescription.Contains("flexible cash", StringComparison.Ordinal);
    }

    private static string ResolveDeterministicDirection(decimal amount)
    {
        if (amount < 0m)
        {
            return "outflow";
        }

        if (amount > 0m)
        {
            return "inflow";
        }

        return "neutral";
    }

    private static string ResolveDeterministicAmountBucket(decimal amount)
    {
        var absolute = Math.Abs(amount);
        if (absolute < 5m)
        {
            return "lt_5";
        }

        if (absolute < 20m)
        {
            return "5_to_20";
        }

        if (absolute < 100m)
        {
            return "20_to_100";
        }

        if (absolute < 500m)
        {
            return "100_to_500";
        }

        return "gte_500";
    }

    private static string? TryReadDiagnosticsJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static int? TryReadDiagnosticsJsonInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    private static int? TryReadNestedDiagnosticsJsonInt(JsonElement element, string parentPropertyName, string childPropertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(parentPropertyName, out var parent)
            || parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(childPropertyName, out var child))
        {
            return null;
        }

        if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    private static bool TryReadDiagnosticsJsonBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static bool TryReadNestedDiagnosticsJsonBool(JsonElement element, string parentPropertyName, string childPropertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(parentPropertyName, out var parent)
            || parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(childPropertyName, out var child))
        {
            return false;
        }

        return child.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(child.GetString(), out var parsed) && parsed,
            _ => false
        };
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




