using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Modules.Banking.Services.Models;
using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankSyncService(
    AppDbContext dbContext,
    BankConnectionService bankConnectionService,
    TrueLayerConfigurationService configurationService,
    TrueLayerTokenService tokenService,
    TrueLayerDataService dataService,
    ISecretProtector secretProtector,
    IAuditService auditService,
    DeterministicReclassificationTriggerService reclassificationTriggerService,
    DeterministicTransactionCategorizationService deterministicCategorizationService,
    DeterministicCategorizationMetrics deterministicMetrics,
    IMerchantResolutionService merchantResolutionService,
    ILogger<BankSyncService> logger)
{
    private const int InternalTransferMatchLookbackDays = 21;
    private const int InternalTransferMatchMaxWindowHours = 72;
    private const int InternalTransferMatchMinimumScore = 6;
    private const int InternalTransferMutualBestMinimumMargin = 2;
    private const int InternalTransferCrossDayPenaltyWhenSameDayExists = 4;
    private const int InternalTransferWeakTimestampCrossDayPenalty = 3;
    private const int InternalTransferNearestNeighborPenalty = 2;
    private const int InternalTransferSequenceTieBreakBoost = 2;
    private const int InternalTransferAmbiguityMaxScoreGap = 1;
    private const int SavingsRelationshipLookbackDays = 35;
    private const int SavingsTransferSubcategoryId = DeterministicCategorizationConstants.GeneralSavingsTransferSubcategoryId;
    private const int SavingsTransferCategoryId = DeterministicCategorizationConstants.SavingsAndInvestmentsCategoryId;
    private const int DeterministicEnrichmentCurrentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;
    private const int DeterministicEnrichmentIncrementalLookbackDays = DeterministicCategorizationConstants.IncrementalLookbackDays;
    private const int DeterministicEnrichmentHistoricalBatchSize = 600;
    private const int DeterministicEnrichmentHistoricalContextPaddingDays = 4;
    private const int DeferredCounterpartyExpiryHours = 48;
    private const int DeferredMoreContextExpiryHours = 24;
    private const int ProjectionBackfillReconcileMaxRowsPerSync = 500;
    private const string DeterministicEnqueueReasonIngestionInsert = DeterministicReclassificationTriggerReasons.ProjectedRowRemapOrDedupeCorrection;
    private static readonly HashSet<string> InternalTransferAccountHintStopTokens =
    [
        "account",
        "accounts",
        "current",
        "savings",
        "bank",
        "card",
        "payment",
        "transfer",
        "from",
        "into",
        "the",
        "and"
    ];
    private static readonly HashSet<string> InternalTransferGenericNoiseTokens =
    [
        "transfer",
        "payment",
        "bank",
        "account",
        "accounts",
        "card",
        "internal",
        "funds",
        "fund",
        "cash",
        "flexible",
        "pocket",
        "vault",
        "saving",
        "savings",
        "round",
        "roundup",
        "up",
        "to",
        "from",
        "for",
        "the",
        "and"
    ];
    private static readonly string[] SavingsMovementSignalKeywords =
    [
        "pocket",
        "vault",
        "cash fund",
        "flexible cash",
        "savings pot",
        "spare change",
        "round up",
        "round-up"
    ];
    private static readonly string[] StrongSavingsMovementSignalKeywords =
    [
        "flexible cash",
        "pocket",
        "vault",
        "cash fund",
        "savings pot",
        "spare change",
        "round up",
        "round-up"
    ];
    private static readonly HashSet<string> RequestedScopeSet = TrueLayerScopes.Default
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public readonly record struct DeterministicEnrichmentRunResult(
        Guid ConnectionId,
        bool HistoricalEnrichmentInProgress,
        bool HistoricalEnrichmentCompleted,
        double? HistoricalEnrichmentProgressPercent,
        DateTime? HistoricalEnrichmentCheckpointUtc,
        int RowsEvaluated,
        int RowsRemaining,
        int RowsActionableRemaining,
        int RowsDeferredRemaining,
        int RowsDeferredWaitingForCounterparty,
        int RowsDeferredWaitingForMoreContext,
        int RowsDeferredLegitimateWaiting,
        int RowsDeferredReadyForTerminalization,
        int RowsRejectedAmbiguous,
        int RowsEvaluatedNoMatchingRule,
        int RowsNotEvaluated,
        int RowsEvaluating,
        int RowsVersionBehind,
        int RowsMarkedForReclassification,
        int RowsSupersededRecomputeRequired,
        bool FullSameUserCounterpartyUniversePresent,
        string DeferredReasonBreakdown,
        string DeferredFamilyBreakdown,
        int BatchesProcessed,
        string Mode,
        bool HasChanges);

    public async Task<ServiceResult<BankSyncResult>> RunInitialSyncAsync(
        OpenBankingConnection connection,
        TrueLayerResolvedConfiguration configuration,
        TrueLayerTokenExchangeResult tokenResult,
        CancellationToken cancellationToken)
    {
        var stored = await StoreRefreshedTokenAsync(connection, tokenResult, cancellationToken);
        if (!stored.Succeeded)
        {
            return ServiceResult<BankSyncResult>.Fail(
                stored.Error!.Message,
                stored.Error.Code,
                stored.Error.StatusCode);
        }

        return await SyncWithAccessTokenAsync(
            connection,
            configuration,
            tokenResult.AccessToken,
            trigger: "initial_sync",
            cancellationToken);
    }

    public async Task<ServiceResult> PersistTokenAsync(
        OpenBankingConnection connection,
        TrueLayerTokenExchangeResult tokenResult,
        CancellationToken cancellationToken)
    {
        return await StoreRefreshedTokenAsync(connection, tokenResult, cancellationToken);
    }

    public async Task<ServiceResult<BankSyncResult>> SyncConnectionAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken,
        string trigger = "manual_sync")
    {
        var connectionResult = await bankConnectionService.GetConnectionForSyncAsync(userId, connectionId, cancellationToken);
        if (!connectionResult.Succeeded)
        {
            return ServiceResult<BankSyncResult>.Fail(
                connectionResult.Error!.Message,
                connectionResult.Error.Code,
                connectionResult.Error.StatusCode);
        }

        var connection = connectionResult.Value!;
        if (connection.Status == BankConnectionStatuses.DisconnectPending)
        {
            logger.LogInformation(
                "Skipped sync because disconnect is pending for connectionId={ConnectionId}",
                connection.Id);

            return ServiceResult<BankSyncResult>.Fail(
                "Disconnect is in progress for this bank connection.",
                "bank_connection_disconnect_pending",
                StatusCodes.Status409Conflict);
        }

        if (connection.Status is BankConnectionStatuses.Revoked or BankConnectionStatuses.DisconnectFailed)
        {
            logger.LogInformation(
                "Skipped sync because connection is disconnected state={Status} connectionId={ConnectionId}",
                connection.Status,
                connection.Id);

            return ServiceResult<BankSyncResult>.Fail(
                "This bank connection has been disconnected.",
                "bank_connection_disconnected",
                StatusCodes.Status409Conflict);
        }

        var configResult = configurationService.Resolve();
        if (!configResult.Succeeded)
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                configResult.Error!.Code,
                configResult.Error.Message,
                cancellationToken);

            logger.LogWarning(
                "Bank sync configuration invalid for connectionId={ConnectionId} code={Code}",
                connection.Id,
                configResult.Error.Code);

            return ServiceResult<BankSyncResult>.Fail(
                configResult.Error.Message,
                configResult.Error.Code,
                configResult.Error.StatusCode);
        }

        if (!string.Equals(connection.ProviderName, BankingProviders.TrueLayer, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<BankSyncResult>.Fail(
                "Unsupported provider for this sync request.",
                "bank_provider_not_supported",
                StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(connection.ProviderEnvironment, configResult.Value!.Environment, StringComparison.OrdinalIgnoreCase))
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                "truelayer_environment_mismatch",
                "Connection environment does not match server configuration.",
                cancellationToken);

            return ServiceResult<BankSyncResult>.Fail(
                "Environment mismatch detected. Reconnect in the configured environment.",
                "truelayer_environment_mismatch",
                StatusCodes.Status409Conflict);
        }

        if (connection.Token is null
            || connection.Token.IsRevoked
            || string.IsNullOrWhiteSpace(connection.Token.EncryptedRefreshToken))
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.ReauthRequired,
                "refresh_token_missing",
                "Reconnect required.",
                cancellationToken);

            return ServiceResult<BankSyncResult>.Fail(
                "Reconnect required.",
                "bank_connection_reauth_required",
                StatusCodes.Status409Conflict);
        }

        string refreshToken;
        try
        {
            refreshToken = secretProtector.Unprotect(connection.Token.EncryptedRefreshToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unable to decrypt refresh token for connectionId={ConnectionId}",
                connection.Id);

            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.ReauthRequired,
                "refresh_token_invalid",
                "Reconnect required.",
                cancellationToken);

            return ServiceResult<BankSyncResult>.Fail(
                "Stored refresh token is invalid.",
                "refresh_token_invalid",
                StatusCodes.Status409Conflict);
        }

        var tokenResult = await tokenService.RefreshAccessTokenAsync(configResult.Value, refreshToken, cancellationToken);
        if (!tokenResult.Succeeded)
        {
            var nextStatus = tokenResult.Error?.Code is "truelayer_authorization_code_invalid" or "truelayer_client_credentials_invalid"
                ? BankConnectionStatuses.ReauthRequired
                : BankConnectionStatuses.Failed;

            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                nextStatus,
                tokenResult.Error?.Code,
                tokenResult.Error?.Message,
                cancellationToken);

            await auditService.WriteEventAsync(
                category: "banking",
                eventName: "manual_sync_failed",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: userId,
                actorType: "user",
                metadata: new
                {
                    code = tokenResult.Error?.Code,
                    status = nextStatus
                },
                cancellationToken);

            return ServiceResult<BankSyncResult>.Fail(
                tokenResult.Error!.Message,
                tokenResult.Error.Code,
                tokenResult.Error.StatusCode);
        }

        var storedToken = await StoreRefreshedTokenAsync(connection, tokenResult.Value!, cancellationToken);
        if (!storedToken.Succeeded)
        {
            return ServiceResult<BankSyncResult>.Fail(
                storedToken.Error!.Message,
                storedToken.Error.Code,
                storedToken.Error.StatusCode);
        }

        return await SyncWithAccessTokenAsync(
            connection,
            configResult.Value,
            tokenResult.Value!.AccessToken,
            trigger,
            cancellationToken);
    }

    public async Task<ServiceResult<DeterministicEnrichmentRunResult>> RunDeterministicEnrichmentAsync(
        Guid userId,
        Guid connectionId,
        string trigger,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.OpenBankingConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return ServiceResult<DeterministicEnrichmentRunResult>.Fail(
                "Connection not found.",
                "bank_connection_not_found",
                StatusCodes.Status404NotFound);
        }

        if (connection.Status is BankConnectionStatuses.DisconnectPending
            or BankConnectionStatuses.DisconnectFailed
            or BankConnectionStatuses.Revoked)
        {
            return ServiceResult<DeterministicEnrichmentRunResult>.Fail(
                "Connection is disconnected.",
                "bank_connection_disconnected",
                StatusCodes.Status409Conflict);
        }

        if (connection.Status is not (
                BankConnectionStatuses.ConnectedPendingSync
                or BankConnectionStatuses.Connected
                or BankConnectionStatuses.SyncPending
                or BankConnectionStatuses.Synced
                or BankConnectionStatuses.ReauthRequired
                or BankConnectionStatuses.Expired
                or BankConnectionStatuses.Failed))
        {
            return ServiceResult<DeterministicEnrichmentRunResult>.Fail(
                "Connection is not ready for deterministic enrichment.",
                "bank_connection_not_ready_for_enrichment",
                StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var summary = await RunDeterministicEnrichmentPassAsync(
            connection,
            now,
            isInitialBackfill: false,
            includeHistorical: true,
            cancellationToken);

        connection.UpdatedUtc = now;

        if (summary.HasChanges || dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Deterministic enrichment run completed connectionId={ConnectionId} trigger={Trigger} mode={Mode} historicalInProgress={HistoricalInProgress} historicalCompleted={HistoricalCompleted} progressPercent={ProgressPercent} rowsEvaluated={RowsEvaluated} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining} rowsDeferredRemaining={RowsDeferredRemaining} rowsDeferredCounterparty={RowsDeferredCounterparty} rowsDeferredMoreContext={RowsDeferredMoreContext} rowsDeferredLegitimateWaiting={RowsDeferredLegitimateWaiting} rowsDeferredReadyForTerminalization={RowsDeferredReadyForTerminalization} rowsRejectedAmbiguous={RowsRejectedAmbiguous} rowsEvaluatedNoMatch={RowsEvaluatedNoMatch} fullCounterpartyUniversePresent={FullCounterpartyUniversePresent} deferredReasonBreakdown={DeferredReasonBreakdown} deferredFamilyBreakdown={DeferredFamilyBreakdown}",
            connection.Id,
            trigger,
            summary.Mode,
            summary.HistoricalEnrichmentInProgress,
            summary.HistoricalEnrichmentCompleted,
            summary.HistoricalEnrichmentProgressPercent,
            summary.RowsEvaluated,
            summary.RowsRemaining,
            summary.RemainingWorkSnapshot.RowsActionableRemaining,
            summary.RemainingWorkSnapshot.RowsDeferredRemaining,
            summary.RemainingWorkSnapshot.RowsDeferredWaitingForCounterparty,
            summary.RemainingWorkSnapshot.RowsDeferredWaitingForMoreContext,
            summary.RemainingWorkSnapshot.RowsDeferredLegitimateWaiting,
            summary.RemainingWorkSnapshot.RowsDeferredReadyForTerminalization,
            summary.RemainingWorkSnapshot.RowsRejectedAmbiguous,
            summary.RemainingWorkSnapshot.RowsEvaluatedNoMatchingRule,
            summary.RemainingWorkSnapshot.FullSameUserCounterpartyUniversePresent,
            summary.RemainingWorkSnapshot.DeferredReasonBreakdown,
            summary.RemainingWorkSnapshot.DeferredFamilyBreakdown);

        return ServiceResult<DeterministicEnrichmentRunResult>.Ok(
            new DeterministicEnrichmentRunResult(
                connection.Id,
                summary.HistoricalEnrichmentInProgress,
                summary.HistoricalEnrichmentCompleted,
                summary.HistoricalEnrichmentProgressPercent,
                summary.HistoricalEnrichmentCheckpointUtc,
                summary.RowsEvaluated,
                summary.RowsRemaining,
                summary.RemainingWorkSnapshot.RowsActionableRemaining,
                summary.RemainingWorkSnapshot.RowsDeferredRemaining,
                summary.RemainingWorkSnapshot.RowsDeferredWaitingForCounterparty,
                summary.RemainingWorkSnapshot.RowsDeferredWaitingForMoreContext,
                summary.RemainingWorkSnapshot.RowsDeferredLegitimateWaiting,
                summary.RemainingWorkSnapshot.RowsDeferredReadyForTerminalization,
                summary.RemainingWorkSnapshot.RowsRejectedAmbiguous,
                summary.RemainingWorkSnapshot.RowsEvaluatedNoMatchingRule,
                summary.RemainingWorkSnapshot.RowsNotEvaluated,
                summary.RemainingWorkSnapshot.RowsEvaluating,
                summary.RemainingWorkSnapshot.RowsVersionBehind,
                summary.RemainingWorkSnapshot.RowsMarkedForReclassification,
                summary.RemainingWorkSnapshot.RowsSupersededRecomputeRequired,
                summary.RemainingWorkSnapshot.FullSameUserCounterpartyUniversePresent,
                summary.RemainingWorkSnapshot.DeferredReasonBreakdown,
                summary.RemainingWorkSnapshot.DeferredFamilyBreakdown,
                summary.BatchesProcessed,
                summary.Mode,
                summary.HasChanges));
    }

    private async Task<ServiceResult<BankSyncResult>> SyncWithAccessTokenAsync(
        OpenBankingConnection connection,
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string trigger,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var isInitialBackfill = await IsInitialBackfillPendingAsync(connection, now, cancellationToken);
        var transactionSyncMode = isInitialBackfill ? "initial_backfill" : "incremental_sync";
        var linkedAccountCountBeforeSync = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == connection.UserId)
            .CountAsync(cancellationToken);
        var sameUserUniverseAccountIdsBeforeSync = await LoadSameUserFinancialAccountIdsWithProjectedRowsAsync(
            connection.UserId,
            cancellationToken);
        var sameUserUniverseSizeBeforeSync = sameUserUniverseAccountIdsBeforeSync.Count;

        if (isInitialBackfill && !connection.InitialBackfillStartedUtc.HasValue)
        {
            connection.InitialBackfillStartedUtc = now;
        }

        var currentStatus = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.Id == connection.Id)
            .Select(x => x.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (currentStatus == BankConnectionStatuses.DisconnectPending)
        {
            logger.LogInformation(
                "Aborted sync because disconnect became pending for connectionId={ConnectionId} trigger={Trigger}",
                connection.Id,
                trigger);

            return ServiceResult<BankSyncResult>.Fail(
                "Disconnect is in progress for this bank connection.",
                "bank_connection_disconnect_pending",
                StatusCodes.Status409Conflict);
        }

        if (currentStatus is BankConnectionStatuses.Revoked or BankConnectionStatuses.DisconnectFailed)
        {
            logger.LogInformation(
                "Aborted sync because connection is disconnected state={Status} connectionId={ConnectionId} trigger={Trigger}",
                currentStatus,
                connection.Id,
                trigger);

            return ServiceResult<BankSyncResult>.Fail(
                "This bank connection has been disconnected.",
                "bank_connection_disconnected",
                StatusCodes.Status409Conflict);
        }

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.SyncPending,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        logger.LogInformation(
            "Bank sync started for connectionId={ConnectionId} trigger={Trigger} transactionMode={TransactionMode} initialBackfillCompleted={InitialBackfillCompleted}",
            connection.Id,
            trigger,
            transactionSyncMode,
            connection.InitialBackfillCompletedUtc.HasValue);

        try
        {
        var accountsResult = await dataService.GetAccountsAsync(configuration, accessToken, cancellationToken);
        if (!accountsResult.Succeeded)
        {
            return await HandleSyncFailureAsync(connection, accountsResult.Error!, trigger, "accounts_refresh", cancellationToken);
        }

        var accountsSynced = 0;
        var balancesSynced = 0;
        var transactionsImported = 0;
        var settledFetched = 0;
        var pendingFetched = 0;
        DateTime? latestFetchedRowUtc = null;
        var hasFetchedRowNewerThanCheckpoint = false;
        var cardsSynced = 0;
        var cardBalancesSynced = 0;
        var cardTransactionsImported = 0;
        var projectedTransactionsFromNewRaw = 0;
        var projectedTransactionsBackfilled = 0;
        var projectedTransactionsPromoted = 0;
        var projectedTransactionsSkippedUnbookedFetched = 0;
        var projectedTransactionsSkippedUnbookedBackfill = 0;
        var projectedTransactionsSkippedDuplicate = 0;
        var projectedDuplicateCheckAttempts = 0;
        var projectedBackfillRowsEvaluated = 0;
        var projectedBackfillRowsDeferred = 0;
        var projectedCandidatePoolSize = 0;
        var projectedTransactionIdsPendingDeterministicReclassification = new HashSet<Guid>();
        var deterministicKickoffFinancialAccountIds = new HashSet<Guid>();
        var ingestionKickoffPendingProjectedRows = 0;
        var ingestionKickoffPendingProjectedTransactionIds = new HashSet<Guid>();
        var ingestionKickoffPendingFinancialAccountIds = new HashSet<Guid>();
        var ingestionKickoffRowsInserted = 0;
        var ingestionKickoffRowsMarked = 0;
        var ingestionKickoffQueueAccepted = false;
        var ingestionKickoffEvents = 0;
        var directDebitsSynced = 0;
        var standingOrdersSynced = 0;
        var identityInfoSynced = false;
        var linkedTransfersMatched = 0;
        var relationshipRowsUpserted = 0;
        var historicalEnrichmentInProgress = false;
        var historicalEnrichmentCompleted = false;
        double? historicalEnrichmentProgressPercent = null;
        DateTime? historicalEnrichmentCheckpointUtc = null;
        var deterministicEnrichmentRowsEvaluated = 0;
        var deterministicEnrichmentRowsRemaining = 0;
        var deterministicEnrichmentBatchesProcessed = 0;
        var deterministicEnrichmentMode = "incremental";
        var syncRunId = Guid.NewGuid();
        var merchantResolutionRunState = new MerchantResolutionRunState(syncRunId);
        var merchantResolutionCache = new Dictionary<string, MerchantResolutionResult>(StringComparer.Ordinal);
        var merchantResolutionFailedDescriptorKeys = new HashSet<string>(StringComparer.Ordinal);
        var providerBrandingRefreshAttempted = false;
        DateTime? requestedBackfillWindowStartUtc = null;
        DateTime? syncObservedEarliestBookedAtUtc = null;
        DateTime? syncObservedLatestBookedAtUtc = null;
        var cardsSupported = connection.SupportsCards;
        var directDebitsSupported = connection.SupportsDirectDebits;
        var standingOrdersSupported = connection.SupportsStandingOrders;
        var infoSupported = connection.SupportsInfo;

        if (ShouldRequestScope(connection.GrantedScopesCsv, "info"))
        {
            var infoResult = await dataService.GetInfoAsync(configuration, accessToken, cancellationToken);
            if (infoResult.Succeeded)
            {
                infoSupported = true;
                identityInfoSynced = infoResult.Value is not null;
                await UpsertIdentityInfoAsync(connection, infoResult.Value, now, cancellationToken);
            }
            else if (IsOptionalDatasetUnsupported(infoResult.Error))
            {
                infoSupported = false;
                logger.LogInformation(
                    "TrueLayer info endpoint unavailable for connectionId={ConnectionId} status={StatusCode} code={Code}",
                    connection.Id,
                    infoResult.Error?.StatusCode,
                    infoResult.Error?.Code);
            }
            else
            {
                logger.LogWarning(
                    "TrueLayer info sync failed for connectionId={ConnectionId} status={StatusCode} code={Code}",
                    connection.Id,
                    infoResult.Error?.StatusCode,
                    infoResult.Error?.Code);
            }
        }

        foreach (var providerAccount in accountsResult.Value!)
        {
            var linkedAccount = await UpsertLinkedAccountAsync(connection, providerAccount, now, cancellationToken);
            accountsSynced++;

            if (!string.IsNullOrWhiteSpace(providerAccount.ProviderId))
            {
                connection.ProviderId = providerAccount.ProviderId;
                connection.ProviderConnectionReference = providerAccount.ProviderId;
            }

            if (!string.IsNullOrWhiteSpace(providerAccount.ProviderDisplayName))
            {
                connection.ProviderDisplayName = providerAccount.ProviderDisplayName;
            }

            ApplyProviderBrandingFromAccount(connection, providerAccount, now);

            if (!providerBrandingRefreshAttempted
                && ShouldRefreshProviderBranding(connection, now)
                && !string.IsNullOrWhiteSpace(connection.ProviderId))
            {
                providerBrandingRefreshAttempted = true;

                var brandingResult = await dataService.GetProviderBrandingAsync(
                    configuration,
                    accessToken,
                    connection.ProviderId!,
                    cancellationToken);

                if (brandingResult.Succeeded && brandingResult.Value is not null)
                {
                    ApplyProviderBrandingFromProviderLookup(connection, brandingResult.Value, now);
                }
                else if (!brandingResult.Succeeded)
                {
                    logger.LogWarning(
                        "Provider branding refresh failed connectionId={ConnectionId} providerId={ProviderId} code={Code}",
                        connection.Id,
                        connection.ProviderId,
                        brandingResult.Error?.Code);
                }
            }

            var balanceStageStopwatch = Stopwatch.StartNew();
            var balanceResult = await dataService.GetBalanceAsync(
                configuration,
                accessToken,
                providerAccount.AccountId,
                cancellationToken);

            if (!balanceResult.Succeeded)
            {
                return await HandleSyncFailureAsync(connection, balanceResult.Error!, trigger, "account_balance_refresh", cancellationToken);
            }

            if (balanceResult.Value is not null)
            {
                var capturedAtUtc = balanceResult.Value.CapturedAtUtc;
                logger.LogInformation(
                    "Fetched bank balance accountId={AccountId} linkedAccountId={LinkedAccountId} capturedUtc={CapturedUtc} current={Current} available={Available} currency={Currency}",
                    providerAccount.AccountId,
                    linkedAccount.Id,
                    capturedAtUtc,
                    balanceResult.Value.Current,
                    balanceResult.Value.Available,
                    balanceResult.Value.Currency);

                var shouldPersistBalanceSnapshot = await ShouldPersistBalanceSnapshotAsync(
                    linkedAccount.Id,
                    balanceResult.Value,
                    cancellationToken);

                if (shouldPersistBalanceSnapshot)
                {
                    dbContext.BankBalanceSnapshots.Add(new BankBalanceSnapshot
                    {
                        Id = Guid.NewGuid(),
                        LinkedBankAccountId = linkedAccount.Id,
                        Available = balanceResult.Value.Available,
                        Current = balanceResult.Value.Current,
                        Overdraft = balanceResult.Value.Overdraft,
                        Currency = balanceResult.Value.Currency,
                        CapturedUtc = capturedAtUtc,
                        RawPayloadJson = balanceResult.Value.RawPayloadJson
                    });
                    balancesSynced++;
                    logger.LogInformation(
                        "Recorded bank balance snapshot accountId={AccountId} linkedAccountId={LinkedAccountId} capturedUtc={CapturedUtc} current={Current} available={Available} currency={Currency}",
                        providerAccount.AccountId,
                        linkedAccount.Id,
                        capturedAtUtc,
                        balanceResult.Value.Current,
                        balanceResult.Value.Available,
                        balanceResult.Value.Currency);
                }
                else
                {
                    logger.LogInformation(
                        "Skipped unchanged bank balance snapshot accountId={AccountId} linkedAccountId={LinkedAccountId} capturedUtc={CapturedUtc}",
                        providerAccount.AccountId,
                        linkedAccount.Id,
                        capturedAtUtc);
                }
            }
            else
            {
                logger.LogInformation(
                    "No bank balance payload returned accountId={AccountId} linkedAccountId={LinkedAccountId}",
                    providerAccount.AccountId,
                    linkedAccount.Id);
            }

            await PersistSyncStageChangesAsync(
                connection.Id,
                providerAccount.AccountId,
                "account_balance_refresh",
                cancellationToken);
            logger.LogInformation(
                "Bank sync phase duration connectionId={ConnectionId} accountId={AccountId} phase={Phase} elapsedMs={ElapsedMs}",
                connection.Id,
                providerAccount.AccountId,
                "account_balance_refresh",
                balanceStageStopwatch.ElapsedMilliseconds);

            var providerPolicy = ResolveProviderTransactionSyncPolicy(providerAccount);
            var transactionWindow = BuildTransactionWindow(
                connection,
                providerPolicy,
                now,
                isInitialBackfill);

            logger.LogInformation(
                "Fetching transactions accountId={AccountId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} mode={Mode} fromUtc={FromUtc} toUtc={ToUtc} policy={PolicyName} policyKey={PolicyKey} policyFamily={PolicyFamily} pendingSupport={PendingSupport} timestampPrecision={TimestampPrecision}",
                providerAccount.AccountId,
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                transactionWindow.Mode,
                transactionWindow.FromUtc,
                transactionWindow.ToUtc,
                transactionWindow.PolicyName ?? "<none>",
                providerPolicy.ProviderKey,
                providerPolicy.ProviderFamily,
                providerPolicy.PendingSupport,
                providerPolicy.TimestampPrecision);

            var transactionStageStopwatch = Stopwatch.StartNew();
            var transactionFetchStopwatch = Stopwatch.StartNew();
            var transactionsFetchResult = await FetchAccountTransactionsAsync(
                configuration,
                accessToken,
                providerAccount,
                providerPolicy,
                transactionWindow,
                isInitialBackfill,
                cancellationToken);

            if (!transactionsFetchResult.Succeeded)
            {
                return await HandleSyncFailureAsync(connection, transactionsFetchResult.Error!, trigger, "account_transactions_import", cancellationToken);
            }

            var fetchedTransactions = transactionsFetchResult.Value!;
            var transactionFetchElapsedMs = transactionFetchStopwatch.ElapsedMilliseconds;
            var latestImportedCheckpointUtcBefore = connection.LatestImportedTransactionUtc;
            var hasFetchedRowNewerThanCheckpointForAccount =
                fetchedTransactions.LatestReturnedUtc.HasValue
                && (!latestImportedCheckpointUtcBefore.HasValue
                    || fetchedTransactions.LatestReturnedUtc.Value > latestImportedCheckpointUtcBefore.Value);
            var latestReturnedLagHours = fetchedTransactions.LatestReturnedUtc.HasValue
                ? (now - fetchedTransactions.LatestReturnedUtc.Value).TotalHours
                : (double?)null;
            var staleReturnedSlice = latestReturnedLagHours.HasValue && latestReturnedLagHours.Value > 24;

            logger.LogInformation(
                "Fetched bank transactions accountId={AccountId} connectionId={ConnectionId} settledFetched={SettledFetched} pendingFetched={PendingFetched} pendingOutcome={PendingOutcome} totalFetched={TotalFetched} requestWindows={RequestWindows} potentiallyCappedWindows={PotentiallyCappedWindows} repeatedWindowPayloads={RepeatedWindowPayloads} earliestReturnedUtc={EarliestReturnedUtc} latestReturnedUtc={LatestReturnedUtc} latestImportedCheckpointUtcBefore={LatestImportedCheckpointUtcBefore} hasFetchedRowNewerThanCheckpoint={HasFetchedRowNewerThanCheckpoint} latestReturnedLagHours={LatestReturnedLagHours} staleReturnedSlice={StaleReturnedSlice}",
                providerAccount.AccountId,
                connection.Id,
                fetchedTransactions.SettledFetched,
                fetchedTransactions.PendingFetched,
                fetchedTransactions.PendingOutcome,
                fetchedTransactions.Transactions.Count,
                fetchedTransactions.RequestWindowCount,
                fetchedTransactions.PotentiallyCappedWindowCount,
                fetchedTransactions.RepeatedWindowPayloadCount,
                fetchedTransactions.EarliestReturnedUtc,
                fetchedTransactions.LatestReturnedUtc,
                latestImportedCheckpointUtcBefore,
                hasFetchedRowNewerThanCheckpointForAccount,
                latestReturnedLagHours,
                staleReturnedSlice);

            if (!hasFetchedRowNewerThanCheckpointForAccount && fetchedTransactions.Transactions.Count > 0)
            {
                logger.LogWarning(
                    "Fetched bank transaction payload did not include rows newer than checkpoint accountId={AccountId} connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} latestImportedCheckpointUtcBefore={LatestImportedCheckpointUtcBefore} latestReturnedUtc={LatestReturnedUtc} latestReturnedLagHours={LatestReturnedLagHours}",
                    providerAccount.AccountId,
                    connection.Id,
                    providerAccount.ProviderId ?? "<unknown>",
                    providerAccount.ProviderDisplayName ?? "<unknown>",
                    latestImportedCheckpointUtcBefore,
                    fetchedTransactions.LatestReturnedUtc,
                    latestReturnedLagHours);
            }

            settledFetched += fetchedTransactions.SettledFetched;
            pendingFetched += fetchedTransactions.PendingFetched;
            latestFetchedRowUtc = MaxUtc(latestFetchedRowUtc, fetchedTransactions.LatestReturnedUtc);
            hasFetchedRowNewerThanCheckpoint = hasFetchedRowNewerThanCheckpoint || hasFetchedRowNewerThanCheckpointForAccount;

            if (isInitialBackfill
                && providerPolicy.InitialLongHistoryGraceMinutes.HasValue
                && now - connection.CreatedUtc > TimeSpan.FromMinutes(providerPolicy.InitialLongHistoryGraceMinutes.Value))
            {
                logger.LogWarning(
                    "Initial backfill long-history grace window elapsed for provider policy connectionId={ConnectionId} policyKey={PolicyKey} policyFamily={PolicyFamily} graceMinutes={GraceMinutes} elapsedSeconds={ElapsedSeconds}",
                    connection.Id,
                    providerPolicy.ProviderKey,
                    providerPolicy.ProviderFamily,
                    providerPolicy.InitialLongHistoryGraceMinutes.Value,
                    (now - connection.CreatedUtc).TotalSeconds);
            }

            requestedBackfillWindowStartUtc = MinUtc(requestedBackfillWindowStartUtc, transactionWindow.FromUtc);

            var observedForAccount = ExtractObservedTransactionBounds(fetchedTransactions.Transactions);
            syncObservedEarliestBookedAtUtc = MinUtc(syncObservedEarliestBookedAtUtc, observedForAccount.EarliestBookedAtUtc);
            syncObservedLatestBookedAtUtc = MaxUtc(syncObservedLatestBookedAtUtc, observedForAccount.LatestBookedAtUtc);

            var transactionUpsertStopwatch = Stopwatch.StartNew();
            var transactionUpsert = await UpsertTransactionsAsync(
                linkedAccount,
                providerAccount,
                providerPolicy,
                fetchedTransactions.Transactions,
                now,
                cancellationToken);
            var transactionUpsertElapsedMs = transactionUpsertStopwatch.ElapsedMilliseconds;
            transactionsImported += transactionUpsert.RawInserted + transactionUpsert.RawUpdated;
            projectedTransactionsFromNewRaw += transactionUpsert.ProjectedFromNewRaw;
            projectedTransactionsPromoted += transactionUpsert.ProjectedFromStatusTransition;
            projectedTransactionsBackfilled += transactionUpsert.ProjectedBackfilled;
            projectedTransactionsSkippedUnbookedFetched += transactionUpsert.ProjectedSkippedUnbookedFetched;
            projectedTransactionsSkippedUnbookedBackfill += transactionUpsert.ProjectedSkippedUnbookedBackfill;
            projectedTransactionsSkippedDuplicate += transactionUpsert.ProjectedSkippedDuplicate;
            projectedDuplicateCheckAttempts += transactionUpsert.ProjectedDuplicateCheckAttempts;
            projectedBackfillRowsEvaluated += transactionUpsert.ProjectedBackfillRowsEvaluated;
            projectedBackfillRowsDeferred += transactionUpsert.ProjectedBackfillRowsDeferred;
            projectedCandidatePoolSize += transactionUpsert.ProjectedCandidatePoolSize;
            projectedTransactionIdsPendingDeterministicReclassification.UnionWith(
                transactionUpsert.ProjectedTransactionIdsForDeterministicReclassification);
            var projectedRowsInsertedThisUpsert =
                transactionUpsert.ProjectedFromNewRaw
                + transactionUpsert.ProjectedFromStatusTransition
                + transactionUpsert.ProjectedBackfilled;
            if (projectedRowsInsertedThisUpsert > 0)
            {
                ingestionKickoffPendingProjectedRows += projectedRowsInsertedThisUpsert;
                ingestionKickoffPendingProjectedTransactionIds.UnionWith(
                    transactionUpsert.ProjectedTransactionIdsForDeterministicReclassification);
                if (linkedAccount.FinancialAccountId.HasValue)
                {
                    ingestionKickoffPendingFinancialAccountIds.Add(linkedAccount.FinancialAccountId.Value);
                }
            }
            if (transactionUpsert.ProjectedTransactionIdsForDeterministicReclassification.Count > 0
                && linkedAccount.FinancialAccountId.HasValue)
            {
                deterministicKickoffFinancialAccountIds.Add(linkedAccount.FinancialAccountId.Value);
            }

            logger.LogInformation(
                "Bank transaction lifecycle accountId={AccountId} connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} fetched={Fetched} rawInserted={RawInserted} rawUpdated={RawUpdated} rawSkippedProviderId={RawSkippedProviderId} rawSkippedDedupe={RawSkippedDedupe} projectedFromNewRaw={ProjectedFromNewRaw} projectedFromStatusTransition={ProjectedFromStatusTransition} projectedBackfilled={ProjectedBackfilled} projectedSkippedUnbookedFetched={ProjectedSkippedUnbookedFetched} projectedSkippedUnbookedBackfill={ProjectedSkippedUnbookedBackfill} projectedSkippedDuplicate={ProjectedSkippedDuplicate} projectedDuplicateCheckAttempts={ProjectedDuplicateCheckAttempts} projectedBackfillRowsEvaluated={ProjectedBackfillRowsEvaluated} projectedBackfillRowsDeferred={ProjectedBackfillRowsDeferred} projectedCandidatePoolSize={ProjectedCandidatePoolSize} fetchElapsedMs={FetchElapsedMs} upsertElapsedMs={UpsertElapsedMs}",
                providerAccount.AccountId,
                connection.Id,
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                transactionUpsert.Fetched,
                transactionUpsert.RawInserted,
                transactionUpsert.RawUpdated,
                transactionUpsert.RawSkippedProviderId,
                transactionUpsert.RawSkippedDedupe,
                transactionUpsert.ProjectedFromNewRaw,
                transactionUpsert.ProjectedFromStatusTransition,
                transactionUpsert.ProjectedBackfilled,
                transactionUpsert.ProjectedSkippedUnbookedFetched,
                transactionUpsert.ProjectedSkippedUnbookedBackfill,
                transactionUpsert.ProjectedSkippedDuplicate,
                transactionUpsert.ProjectedDuplicateCheckAttempts,
                transactionUpsert.ProjectedBackfillRowsEvaluated,
                transactionUpsert.ProjectedBackfillRowsDeferred,
                transactionUpsert.ProjectedCandidatePoolSize,
                transactionFetchElapsedMs,
                transactionUpsertElapsedMs);

            if (transactionUpsert.ProjectedBackfilled > 0)
            {
                logger.LogWarning(
                    "Backfilled previously missing projected bank transactions accountId={AccountId} connectionId={ConnectionId} projectedBackfilled={ProjectedBackfilled}",
                    providerAccount.AccountId,
                    connection.Id,
                    transactionUpsert.ProjectedBackfilled);
            }

            if (transactionUpsert.ProjectedSkippedUnbookedFetched > 0 || transactionUpsert.ProjectedSkippedUnbookedBackfill > 0)
            {
                logger.LogInformation(
                    "Skipped projecting non-booked bank transactions into ledger accountId={AccountId} connectionId={ConnectionId} projectedSkippedUnbookedFetched={ProjectedSkippedUnbookedFetched} projectedSkippedUnbookedBackfill={ProjectedSkippedUnbookedBackfill}",
                    providerAccount.AccountId,
                    connection.Id,
                    transactionUpsert.ProjectedSkippedUnbookedFetched,
                    transactionUpsert.ProjectedSkippedUnbookedBackfill);
            }

            if (ShouldRequestScope(connection.GrantedScopesCsv, "direct_debits"))
            {
                var directDebitsResult = await dataService.GetDirectDebitsAsync(
                    configuration,
                    accessToken,
                    providerAccount.AccountId,
                    cancellationToken);

                if (directDebitsResult.Succeeded)
                {
                    directDebitsSupported = true;
                    directDebitsSynced += await UpsertDirectDebitsAsync(
                        linkedAccount,
                        directDebitsResult.Value!,
                        now,
                        cancellationToken);
                }
                else if (IsOptionalDatasetUnsupported(directDebitsResult.Error))
                {
                    if (directDebitsSupported is null)
                    {
                        directDebitsSupported = false;
                    }

                    logger.LogInformation(
                        "Direct debits unavailable for accountId={AccountId} connectionId={ConnectionId} status={StatusCode}",
                        providerAccount.AccountId,
                        connection.Id,
                        directDebitsResult.Error?.StatusCode);
                }
                else
                {
                    logger.LogWarning(
                        "Direct debits sync failed for accountId={AccountId} connectionId={ConnectionId} status={StatusCode} code={Code}",
                        providerAccount.AccountId,
                        connection.Id,
                        directDebitsResult.Error?.StatusCode,
                        directDebitsResult.Error?.Code);
                }
            }

            if (ShouldRequestScope(connection.GrantedScopesCsv, "standing_orders"))
            {
                var standingOrdersResult = await dataService.GetStandingOrdersAsync(
                    configuration,
                    accessToken,
                    providerAccount.AccountId,
                    cancellationToken);

                if (standingOrdersResult.Succeeded)
                {
                    standingOrdersSupported = true;
                    standingOrdersSynced += await UpsertStandingOrdersAsync(
                        linkedAccount,
                        standingOrdersResult.Value!,
                        now,
                        cancellationToken);
                }
                else if (IsOptionalDatasetUnsupported(standingOrdersResult.Error))
                {
                    if (standingOrdersSupported is null)
                    {
                        standingOrdersSupported = false;
                    }

                    logger.LogInformation(
                        "Standing orders unavailable for accountId={AccountId} connectionId={ConnectionId} status={StatusCode}",
                        providerAccount.AccountId,
                        connection.Id,
                        standingOrdersResult.Error?.StatusCode);
                }
                else
                {
                    logger.LogWarning(
                        "Standing orders sync failed for accountId={AccountId} connectionId={ConnectionId} status={StatusCode} code={Code}",
                        providerAccount.AccountId,
                        connection.Id,
                        standingOrdersResult.Error?.StatusCode,
                        standingOrdersResult.Error?.Code);
                }
            }

            await PersistSyncStageChangesAsync(
                connection.Id,
                providerAccount.AccountId,
                "account_transactions_and_commitments",
                cancellationToken);
            logger.LogInformation(
                "Bank sync phase duration connectionId={ConnectionId} accountId={AccountId} phase={Phase} elapsedMs={ElapsedMs}",
                connection.Id,
                providerAccount.AccountId,
                "account_transactions_and_commitments",
                transactionStageStopwatch.ElapsedMilliseconds);

            var merchantResolutionStopwatch = Stopwatch.StartNew();
            var merchantResolutionSummary = await ResolveMerchantResolutionForAccountTransactionsAsync(
                connection.UserId,
                connection.Id,
                linkedAccount,
                providerAccount,
                transactionUpsert.NormalizedTransactionIdsForMerchantResolution,
                syncRunId,
                merchantResolutionRunState,
                merchantResolutionCache,
                merchantResolutionFailedDescriptorKeys,
                now,
                cancellationToken);

            if (merchantResolutionSummary.TriggerCandidates > 0)
            {
                await PersistSyncStageChangesAsync(
                    connection.Id,
                    providerAccount.AccountId,
                    "account_merchant_resolution",
                    cancellationToken);

                logger.LogInformation(
                    "Bank sync phase duration connectionId={ConnectionId} accountId={AccountId} phase={Phase} elapsedMs={ElapsedMs}",
                    connection.Id,
                    providerAccount.AccountId,
                    "account_merchant_resolution",
                    merchantResolutionStopwatch.ElapsedMilliseconds);
            }

            if (ingestionKickoffPendingProjectedRows > 0
                && ingestionKickoffPendingProjectedTransactionIds.Count > 0)
            {
                var ingestionKickoffOutcome = await TriggerDeterministicKickoffFromIngestionAsync(
                    connection,
                    ingestionKickoffPendingProjectedRows,
                    ingestionKickoffPendingProjectedTransactionIds,
                    ingestionKickoffPendingFinancialAccountIds,
                    ResolveSyncReclassificationReason(
                        trigger,
                        universeExpanded: false,
                        remapOrDedupeCorrection: false),
                    now,
                    cancellationToken);
                ingestionKickoffRowsInserted += ingestionKickoffOutcome.InsertedRows;
                ingestionKickoffRowsMarked += ingestionKickoffOutcome.MarkedRows;
                ingestionKickoffQueueAccepted = ingestionKickoffQueueAccepted || ingestionKickoffOutcome.QueueAccepted;
                ingestionKickoffEvents++;
                ingestionKickoffPendingProjectedRows = 0;
                ingestionKickoffPendingProjectedTransactionIds.Clear();
                ingestionKickoffPendingFinancialAccountIds.Clear();
            }
        }

        if (ShouldRequestScope(connection.GrantedScopesCsv, "cards"))
        {
            var cardsResult = await dataService.GetCardsAsync(configuration, accessToken, cancellationToken);
            if (cardsResult.Succeeded)
            {
                cardsSupported = true;
                var cardProviderPolicy = ResolveProviderTransactionSyncPolicy(connection);
                foreach (var providerCard in cardsResult.Value!)
                {
                    var linkedCard = await UpsertLinkedCardAsync(connection, providerCard, now, cancellationToken);
                    cardsSynced++;

                    if (ShouldRequestScope(connection.GrantedScopesCsv, "balance"))
                    {
                        var cardBalanceResult = await dataService.GetCardBalanceAsync(
                            configuration,
                            accessToken,
                            providerCard.CardId,
                            cancellationToken);

                        if (cardBalanceResult.Succeeded && cardBalanceResult.Value is not null)
                        {
                            var shouldPersistCardBalanceSnapshot = await ShouldPersistCardBalanceSnapshotAsync(
                                linkedCard.Id,
                                cardBalanceResult.Value,
                                cancellationToken);

                            if (shouldPersistCardBalanceSnapshot)
                            {
                                dbContext.BankCardBalanceSnapshots.Add(new BankCardBalanceSnapshot
                                {
                                    Id = Guid.NewGuid(),
                                    LinkedBankCardId = linkedCard.Id,
                                    Available = cardBalanceResult.Value.Available,
                                    Current = cardBalanceResult.Value.Current,
                                    Limit = cardBalanceResult.Value.Limit,
                                    Outstanding = cardBalanceResult.Value.Outstanding,
                                    Currency = cardBalanceResult.Value.Currency,
                                    CapturedUtc = cardBalanceResult.Value.CapturedAtUtc,
                                    RawPayloadJson = cardBalanceResult.Value.RawPayloadJson
                                });
                                cardBalancesSynced++;
                            }
                        }
                        else if (!cardBalanceResult.Succeeded && !IsOptionalDatasetUnsupported(cardBalanceResult.Error))
                        {
                            logger.LogWarning(
                                "Card balance sync failed for cardId={CardId} connectionId={ConnectionId} status={StatusCode} code={Code}",
                                providerCard.CardId,
                                connection.Id,
                                cardBalanceResult.Error?.StatusCode,
                                cardBalanceResult.Error?.Code);
                        }
                    }

                    if (ShouldRequestScope(connection.GrantedScopesCsv, "transactions"))
                    {
                        var cardTransactionWindow = BuildCardTransactionWindow(connection, cardProviderPolicy, now, isInitialBackfill);
                        var cardTransactionsResult = await dataService.GetCardTransactionsAsync(
                            configuration,
                            accessToken,
                            providerCard.CardId,
                            cardTransactionWindow.FromUtc,
                            cardTransactionWindow.ToUtc,
                            cancellationToken);

                        if (cardTransactionsResult.Succeeded)
                        {
                            var allCardTransactions = cardTransactionsResult.Value!.ToList();
                            if (cardProviderPolicy.PendingSupport == ProviderPendingSupportMode.Unsupported)
                            {
                                logger.LogInformation(
                                    "Pending card transactions fetch skipped by provider policy cardId={CardId} connectionId={ConnectionId} policyKey={PolicyKey} policyFamily={PolicyFamily}",
                                    providerCard.CardId,
                                    connection.Id,
                                    cardProviderPolicy.ProviderKey,
                                    cardProviderPolicy.ProviderFamily);
                            }
                            else
                            {
                                var pendingCardTransactionsResult = await dataService.GetPendingCardTransactionsAsync(
                                    configuration,
                                    accessToken,
                                    providerCard.CardId,
                                    cardTransactionWindow.FromUtc,
                                    cardTransactionWindow.ToUtc,
                                    cancellationToken);

                                if (pendingCardTransactionsResult.Succeeded)
                                {
                                    allCardTransactions.AddRange(pendingCardTransactionsResult.Value!);
                                }
                                else if (IsOptionalDatasetUnsupported(pendingCardTransactionsResult.Error))
                                {
                                    logger.LogInformation(
                                        "Pending card transactions endpoint unavailable for cardId={CardId} connectionId={ConnectionId} status={StatusCode}",
                                        providerCard.CardId,
                                        connection.Id,
                                        pendingCardTransactionsResult.Error?.StatusCode);
                                }
                                else
                                {
                                    logger.LogWarning(
                                        "Pending card transactions sync failed for cardId={CardId} connectionId={ConnectionId} status={StatusCode} code={Code}",
                                        providerCard.CardId,
                                        connection.Id,
                                        pendingCardTransactionsResult.Error?.StatusCode,
                                        pendingCardTransactionsResult.Error?.Code);
                                }
                            }

                            var cardTransactionUpsert = await UpsertCardTransactionsAsync(
                                linkedCard,
                                allCardTransactions,
                                now,
                                cancellationToken);
                            cardTransactionsImported += cardTransactionUpsert.ImportedCount;
                            projectedTransactionIdsPendingDeterministicReclassification.UnionWith(
                                cardTransactionUpsert.ProjectedTransactionIdsForDeterministicReclassification);
                            if (cardTransactionUpsert.ProjectedInsertedCount > 0)
                            {
                                ingestionKickoffPendingProjectedRows += cardTransactionUpsert.ProjectedInsertedCount;
                                ingestionKickoffPendingProjectedTransactionIds.UnionWith(
                                    cardTransactionUpsert.ProjectedTransactionIdsForDeterministicReclassification);
                                if (cardTransactionUpsert.ProjectedFinancialAccountId.HasValue)
                                {
                                    ingestionKickoffPendingFinancialAccountIds.Add(
                                        cardTransactionUpsert.ProjectedFinancialAccountId.Value);
                                }
                            }
                            if (cardTransactionUpsert.ProjectedTransactionIdsForDeterministicReclassification.Count > 0
                                && cardTransactionUpsert.ProjectedFinancialAccountId.HasValue)
                            {
                                deterministicKickoffFinancialAccountIds.Add(
                                    cardTransactionUpsert.ProjectedFinancialAccountId.Value);
                            }
                        }
                        else if (!IsOptionalDatasetUnsupported(cardTransactionsResult.Error))
                        {
                            logger.LogWarning(
                                "Card transactions sync failed for cardId={CardId} connectionId={ConnectionId} status={StatusCode} code={Code}",
                                providerCard.CardId,
                                connection.Id,
                                cardTransactionsResult.Error?.StatusCode,
                                cardTransactionsResult.Error?.Code);
                        }
                    }
                }
            }
            else if (IsOptionalDatasetUnsupported(cardsResult.Error))
            {
                cardsSupported = false;
                logger.LogInformation(
                    "Cards endpoint unavailable for connectionId={ConnectionId} status={StatusCode}",
                    connection.Id,
                    cardsResult.Error?.StatusCode);
            }
            else
            {
                logger.LogWarning(
                    "Cards sync failed for connectionId={ConnectionId} status={StatusCode} code={Code}",
                    connection.Id,
                    cardsResult.Error?.StatusCode,
                    cardsResult.Error?.Code);
            }
        }

        await PersistSyncStageChangesAsync(
            connection.Id,
            accountId: null,
            stageName: "cards_refresh",
            cancellationToken);

        if (ingestionKickoffPendingProjectedRows > 0
            && ingestionKickoffPendingProjectedTransactionIds.Count > 0)
        {
            var ingestionKickoffOutcome = await TriggerDeterministicKickoffFromIngestionAsync(
                connection,
                ingestionKickoffPendingProjectedRows,
                ingestionKickoffPendingProjectedTransactionIds,
                ingestionKickoffPendingFinancialAccountIds,
                ResolveSyncReclassificationReason(
                    trigger,
                    universeExpanded: false,
                    remapOrDedupeCorrection: false),
                now,
                cancellationToken);
            ingestionKickoffRowsInserted += ingestionKickoffOutcome.InsertedRows;
            ingestionKickoffRowsMarked += ingestionKickoffOutcome.MarkedRows;
            ingestionKickoffQueueAccepted = ingestionKickoffQueueAccepted || ingestionKickoffOutcome.QueueAccepted;
            ingestionKickoffEvents++;
            ingestionKickoffPendingProjectedRows = 0;
            ingestionKickoffPendingProjectedTransactionIds.Clear();
            ingestionKickoffPendingFinancialAccountIds.Clear();
        }

        var statusBeforePersistingImportedData = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.Id == connection.Id)
            .Select(x => x.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (statusBeforePersistingImportedData == BankConnectionStatuses.DisconnectPending)
        {
            logger.LogInformation(
                "Aborted sync before persisting imported data because disconnect is pending connectionId={ConnectionId}",
                connection.Id);
            dbContext.ChangeTracker.Clear();

            return ServiceResult<BankSyncResult>.Fail(
                "Disconnect is in progress for this bank connection.",
                "bank_connection_disconnect_pending",
                StatusCodes.Status409Conflict);
        }

        if (statusBeforePersistingImportedData is BankConnectionStatuses.Revoked or BankConnectionStatuses.DisconnectFailed)
        {
            logger.LogInformation(
                "Aborted sync before persisting imported data because connection is disconnected state={Status} connectionId={ConnectionId}",
                statusBeforePersistingImportedData,
                connection.Id);
            dbContext.ChangeTracker.Clear();

            return ServiceResult<BankSyncResult>.Fail(
                "This bank connection has been disconnected.",
                "bank_connection_disconnected",
                StatusCodes.Status409Conflict);
        }

        connection.EarliestImportedTransactionUtc = MinUtc(connection.EarliestImportedTransactionUtc, syncObservedEarliestBookedAtUtc);
        connection.LatestImportedTransactionUtc = MaxUtc(connection.LatestImportedTransactionUtc, syncObservedLatestBookedAtUtc);
        connection.SupportsInfo = infoSupported;
        connection.SupportsCards = cardsSupported;
        connection.SupportsDirectDebits = directDebitsSupported;
        connection.SupportsStandingOrders = standingOrdersSupported;

        if (isInitialBackfill)
        {
            connection.InitialBackfillWindowStartUtc = MinUtc(connection.InitialBackfillWindowStartUtc, requestedBackfillWindowStartUtc);
            connection.InitialBackfillCompletedUtc ??= now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var linkedAccountCountAfterSync = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == connection.UserId)
            .CountAsync(cancellationToken);
        var sameUserUniverseAccountIdsAfterSync = await LoadSameUserFinancialAccountIdsWithProjectedRowsAsync(
            connection.UserId,
            cancellationToken);
        var sameUserUniverseSizeAfterSync = sameUserUniverseAccountIdsAfterSync.Count;
        var sameUserUniverseExpanded = sameUserUniverseAccountIdsAfterSync
            .Except(sameUserUniverseAccountIdsBeforeSync)
            .Any();
        var newlyImportedRowsMarkedPostSync =
            await MarkSpecificTransactionsForDeterministicReclassificationAsync(
                projectedTransactionIdsPendingDeterministicReclassification,
                now,
                cancellationToken);
        var newlyImportedRowsMarkedForDeterministicReclassification =
            ingestionKickoffRowsMarked + newlyImportedRowsMarkedPostSync;
        var universeExpansionRowsInvalidated = sameUserUniverseExpanded
            ? await InvalidateTransferRowsForSameUserUniverseExpansionAsync(connection.UserId, now, cancellationToken)
            : 0;
        var deterministicKickoffAccountIdsText = deterministicKickoffFinancialAccountIds.Count == 0
            ? "none"
            : string.Join(
                ",",
                deterministicKickoffFinancialAccountIds
                    .OrderBy(x => x)
                    .Take(12)
                    .Select(x => x.ToString("N")));

        logger.LogInformation(
            "Deterministic kickoff post-import connectionId={ConnectionId} userId={UserId} importedTransactions={ImportedTransactions} projectedRowsForKickoff={ProjectedRowsForKickoff} markedRows={MarkedRows} ingestionKickoffEvents={IngestionKickoffEvents} ingestionInsertedRows={IngestionInsertedRows} ingestionMarkedRows={IngestionMarkedRows} ingestionQueueAccepted={IngestionQueueAccepted} kickoffFinancialAccountCount={KickoffFinancialAccountCount} kickoffFinancialAccountIds={KickoffFinancialAccountIds} linkedAccountsBefore={LinkedAccountsBefore} linkedAccountsAfter={LinkedAccountsAfter} sameUserUniverseSizeBefore={SameUserUniverseSizeBefore} sameUserUniverseSizeAfter={SameUserUniverseSizeAfter} sameUserUniverseExpanded={SameUserUniverseExpanded} universeExpansionRowsInvalidated={UniverseExpansionRowsInvalidated}",
            connection.Id,
            connection.UserId,
            transactionsImported,
            projectedTransactionIdsPendingDeterministicReclassification.Count,
            newlyImportedRowsMarkedForDeterministicReclassification,
            ingestionKickoffEvents,
            ingestionKickoffRowsInserted,
            ingestionKickoffRowsMarked,
            ingestionKickoffQueueAccepted,
            deterministicKickoffFinancialAccountIds.Count,
            deterministicKickoffAccountIdsText,
            linkedAccountCountBeforeSync,
            linkedAccountCountAfterSync,
            sameUserUniverseSizeBeforeSync,
            sameUserUniverseSizeAfterSync,
            sameUserUniverseExpanded,
            universeExpansionRowsInvalidated);

        var syncReclassificationReasonCode = ResolveSyncReclassificationReason(
            trigger,
            sameUserUniverseExpanded,
            remapOrDedupeCorrection: projectedTransactionsPromoted > 0 || projectedTransactionsBackfilled > 0);

        if (newlyImportedRowsMarkedForDeterministicReclassification > 0 || universeExpansionRowsInvalidated > 0)
        {
            await reclassificationTriggerService.TriggerAsync(
                new DeterministicReclassificationTriggerRequest(
                    UserId: connection.UserId,
                    Source: "sync_changed_scope_marking",
                    ReasonCode: syncReclassificationReasonCode,
                    SourceConnectionId: connection.Id,
                    ConnectionIds: [connection.Id],
                    MarkConnectionsForHistoricalReplay: true,
                    QueueConnections: false),
                cancellationToken);
        }

        var dataChanged =
            transactionsImported > 0
            || cardTransactionsImported > 0
            || projectedTransactionsPromoted > 0
            || projectedTransactionsBackfilled > 0
            || balancesSynced > 0
            || cardBalancesSynced > 0
            || linkedTransfersMatched > 0
            || relationshipRowsUpserted > 0
            || newlyImportedRowsMarkedForDeterministicReclassification > 0
            || universeExpansionRowsInvalidated > 0;

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.Synced,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        var remainingWorkSnapshotAfterSync = await GetDeterministicRemainingWorkSnapshotAsync(
            connection.UserId,
            cancellationToken);
        var rowsInDeterministicScopeAfterSync = remainingWorkSnapshotAfterSync.RowsTotal;
        var actionableRowsRemainingAfterSync = remainingWorkSnapshotAfterSync.RowsActionableRemaining;
        var requiresHistoricalReplayWithImportedRows = connection.NeedsHistoricalReclassification
            && rowsInDeterministicScopeAfterSync > 0;
        var shouldContinueFromRowTruthDebt = DeterministicEnrichmentContinuationPolicy.ShouldContinue(
            remainingWorkSnapshotAfterSync.RowsRemaining,
            remainingWorkSnapshotAfterSync.RowsActionableRemaining,
            remainingWorkSnapshotAfterSync.RowsNotEvaluated,
            remainingWorkSnapshotAfterSync.RowsEvaluating,
            remainingWorkSnapshotAfterSync.RowsVersionBehind,
            remainingWorkSnapshotAfterSync.RowsMarkedForReclassification,
            remainingWorkSnapshotAfterSync.RowsSupersededRecomputeRequired);
        var shouldQueuePostSync = requiresHistoricalReplayWithImportedRows || shouldContinueFromRowTruthDebt;
        var postSyncQueueReason = ingestionKickoffRowsInserted > 0
            ? DeterministicEnqueueReasonIngestionInsert
            : syncReclassificationReasonCode;
        var postSyncQueueAccepted = false;
        if (shouldQueuePostSync)
        {
            var queueTrigger = await reclassificationTriggerService.TriggerAsync(
                new DeterministicReclassificationTriggerRequest(
                    UserId: connection.UserId,
                    Source: "sync_post_completion_queue",
                    ReasonCode: postSyncQueueReason,
                    SourceConnectionId: connection.Id,
                    ConnectionIds: [connection.Id],
                    MarkConnectionsForHistoricalReplay: false,
                    QueueConnections: true),
                cancellationToken);
            postSyncQueueAccepted = queueTrigger.QueueRequestsAttempted > 0 && queueTrigger.QueueFailures == 0;

            logger.LogInformation(
                "Deterministic kickoff queue outcome connectionId={ConnectionId} userId={UserId} queueAccepted={QueueAccepted} markedRows={MarkedRows} universeExpansionRowsInvalidated={UniverseExpansionRowsInvalidated} rowsInDeterministicScopeAfterSync={RowsInDeterministicScopeAfterSync} actionableRowsRemainingAfterSync={ActionableRowsRemainingAfterSync} requiresHistoricalReplayWithImportedRows={RequiresHistoricalReplayWithImportedRows} reason={Reason}",
                connection.Id,
                connection.UserId,
                postSyncQueueAccepted,
                newlyImportedRowsMarkedForDeterministicReclassification,
                universeExpansionRowsInvalidated,
                rowsInDeterministicScopeAfterSync,
                actionableRowsRemainingAfterSync,
                requiresHistoricalReplayWithImportedRows,
                postSyncQueueReason);
        }
        else
        {
            logger.LogInformation(
                "Skipped post-sync deterministic enqueue because no deterministic row debt remained after sync connectionId={ConnectionId} userId={UserId} ingestionKickoffQueueAccepted={IngestionKickoffQueueAccepted} markedRows={MarkedRows} universeExpansionRowsInvalidated={UniverseExpansionRowsInvalidated} rowsInDeterministicScopeAfterSync={RowsInDeterministicScopeAfterSync} rowsRemainingAfterSync={RowsRemainingAfterSync} actionableRowsRemainingAfterSync={ActionableRowsRemainingAfterSync} rowsNotEvaluatedAfterSync={RowsNotEvaluatedAfterSync} rowsEvaluatingAfterSync={RowsEvaluatingAfterSync} rowsVersionBehindAfterSync={RowsVersionBehindAfterSync} rowsMarkedForReclassificationAfterSync={RowsMarkedForReclassificationAfterSync} rowsSupersededAfterSync={RowsSupersededAfterSync} requiresHistoricalReplayWithImportedRows={RequiresHistoricalReplayWithImportedRows}",
                connection.Id,
                connection.UserId,
                ingestionKickoffQueueAccepted,
                newlyImportedRowsMarkedForDeterministicReclassification,
                universeExpansionRowsInvalidated,
                rowsInDeterministicScopeAfterSync,
                remainingWorkSnapshotAfterSync.RowsRemaining,
                actionableRowsRemainingAfterSync,
                remainingWorkSnapshotAfterSync.RowsNotEvaluated,
                remainingWorkSnapshotAfterSync.RowsEvaluating,
                remainingWorkSnapshotAfterSync.RowsVersionBehind,
                remainingWorkSnapshotAfterSync.RowsMarkedForReclassification,
                remainingWorkSnapshotAfterSync.RowsSupersededRecomputeRequired,
                requiresHistoricalReplayWithImportedRows);
        }

        var queueAccepted = ingestionKickoffQueueAccepted || postSyncQueueAccepted;
        if (postSyncQueueAccepted)
        {
            deterministicEnrichmentMode = string.Equals(
                postSyncQueueReason,
                DeterministicEnqueueReasonIngestionInsert,
                StringComparison.Ordinal)
                ? "queued_from_ingestion"
                : "queued_post_sync";
        }
        else if (shouldQueuePostSync)
        {
            deterministicEnrichmentMode = "queue_enqueue_failed";
        }
        else if (ingestionKickoffQueueAccepted)
        {
            deterministicEnrichmentMode = "queued_from_ingestion";
        }
        else
        {
            deterministicEnrichmentMode = "queue_not_required";
        }

        historicalEnrichmentCheckpointUtc = connection.HistoricalEnrichmentCheckpointUtc;
        historicalEnrichmentCompleted = connection.HistoricalEnrichmentCompletedUtc.HasValue
            && (connection.HistoricalEnrichmentVersion ?? 0) >= DeterministicEnrichmentCurrentVersion
            && !connection.NeedsHistoricalReclassification;

        if (!historicalEnrichmentCompleted)
        {
            historicalEnrichmentInProgress =
                queueAccepted
                || connection.NeedsHistoricalReclassification
                || !connection.HistoricalEnrichmentCompletedUtc.HasValue;
            historicalEnrichmentProgressPercent = historicalEnrichmentInProgress ? 0d : null;
        }
        else
        {
            historicalEnrichmentInProgress = false;
            historicalEnrichmentProgressPercent = 100d;
        }

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: trigger == "initial_sync" ? "initial_sync_success" : "manual_sync_success",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new
            {
                accountsSynced,
                balancesSynced,
                transactionsImported,
                rawTransactionsChanged = transactionsImported,
                projectedTransactionsFromNewRaw,
                projectedTransactionsPromoted,
                projectedTransactionsBackfilled,
                projectedTransactionsSkippedUnbookedFetched,
                projectedTransactionsSkippedUnbookedBackfill,
                projectedTransactionsSkippedDuplicate,
                projectedDuplicateCheckAttempts,
                projectedBackfillRowsEvaluated,
                projectedBackfillRowsDeferred,
                projectedCandidatePoolSize,
                sameUserUniverseSizeBeforeSync,
                sameUserUniverseSizeAfterSync,
                sameUserUniverseExpanded,
                ingestionKickoffEvents,
                ingestionKickoffRowsInserted,
                ingestionKickoffRowsMarked,
                ingestionKickoffQueueAccepted,
                newlyImportedRowsMarkedForDeterministicReclassification,
                universeExpansionRowsInvalidated,
                cardsSynced,
                cardBalancesSynced,
                cardTransactionsImported,
                directDebitsSynced,
                standingOrdersSynced,
                identityInfoSynced,
                linkedTransfersMatched,
                relationshipRowsUpserted,
                deterministicEnrichmentMode,
                postSyncQueueAccepted,
                postSyncQueueReason,
                queueAccepted,
                deterministicEnrichmentBatchesProcessed,
                deterministicEnrichmentRowsEvaluated,
                deterministicEnrichmentRowsRemaining,
                historicalEnrichmentInProgress,
                historicalEnrichmentCompleted,
                historicalEnrichmentProgressPercent,
                historicalEnrichmentCheckpointUtc,
                dataChanged,
                transactionMode = transactionSyncMode,
                requestedBackfillWindowStartUtc,
                observedEarliestTransactionUtc = syncObservedEarliestBookedAtUtc,
                observedLatestTransactionUtc = syncObservedLatestBookedAtUtc,
                initialBackfillCompletedUtc = connection.InitialBackfillCompletedUtc
            },
            cancellationToken);

        logger.LogInformation(
            "Bank sync completed for connectionId={ConnectionId} accountsSynced={AccountsSynced} balancesSynced={BalancesSynced} rawTransactionsChanged={RawTransactionsChanged} projectedTransactionsFromNewRaw={ProjectedTransactionsFromNewRaw} projectedTransactionsPromoted={ProjectedTransactionsPromoted} projectedTransactionsBackfilled={ProjectedTransactionsBackfilled} projectedTransactionsSkippedUnbookedFetched={ProjectedTransactionsSkippedUnbookedFetched} projectedTransactionsSkippedUnbookedBackfill={ProjectedTransactionsSkippedUnbookedBackfill} projectedTransactionsSkippedDuplicate={ProjectedTransactionsSkippedDuplicate} projectedDuplicateCheckAttempts={ProjectedDuplicateCheckAttempts} projectedBackfillRowsEvaluated={ProjectedBackfillRowsEvaluated} projectedBackfillRowsDeferred={ProjectedBackfillRowsDeferred} projectedCandidatePoolSize={ProjectedCandidatePoolSize} cardsSynced={CardsSynced} cardBalancesSynced={CardBalancesSynced} cardTransactionsImported={CardTransactionsImported} directDebitsSynced={DirectDebitsSynced} standingOrdersSynced={StandingOrdersSynced} infoSynced={InfoSynced} linkedTransfersMatched={LinkedTransfersMatched} relationshipRowsUpserted={RelationshipRowsUpserted} deterministicEnrichmentMode={DeterministicEnrichmentMode} deterministicEnrichmentBatchesProcessed={DeterministicEnrichmentBatchesProcessed} deterministicEnrichmentRowsEvaluated={DeterministicEnrichmentRowsEvaluated} deterministicEnrichmentRowsRemaining={DeterministicEnrichmentRowsRemaining} historicalEnrichmentInProgress={HistoricalEnrichmentInProgress} historicalEnrichmentCompleted={HistoricalEnrichmentCompleted} historicalEnrichmentProgressPercent={HistoricalEnrichmentProgressPercent} historicalEnrichmentCheckpointUtc={HistoricalEnrichmentCheckpointUtc} dataChanged={DataChanged} transactionMode={TransactionMode} initialBackfillCompletedUtc={InitialBackfillCompletedUtc} earliestImportedUtc={EarliestImportedUtc} latestImportedUtc={LatestImportedUtc}",
            connection.Id,
            accountsSynced,
            balancesSynced,
            transactionsImported,
            projectedTransactionsFromNewRaw,
            projectedTransactionsPromoted,
            projectedTransactionsBackfilled,
            projectedTransactionsSkippedUnbookedFetched,
            projectedTransactionsSkippedUnbookedBackfill,
            projectedTransactionsSkippedDuplicate,
            projectedDuplicateCheckAttempts,
            projectedBackfillRowsEvaluated,
            projectedBackfillRowsDeferred,
            projectedCandidatePoolSize,
            cardsSynced,
            cardBalancesSynced,
            cardTransactionsImported,
            directDebitsSynced,
            standingOrdersSynced,
            identityInfoSynced,
            linkedTransfersMatched,
            relationshipRowsUpserted,
            deterministicEnrichmentMode,
            deterministicEnrichmentBatchesProcessed,
            deterministicEnrichmentRowsEvaluated,
            deterministicEnrichmentRowsRemaining,
            historicalEnrichmentInProgress,
            historicalEnrichmentCompleted,
            historicalEnrichmentProgressPercent,
            historicalEnrichmentCheckpointUtc,
            dataChanged,
            transactionSyncMode,
            connection.InitialBackfillCompletedUtc,
            connection.EarliestImportedTransactionUtc,
            connection.LatestImportedTransactionUtc);

        var freshnessSummary =
            settledFetched + pendingFetched == 0
                ? "no_rows_returned"
                : hasFetchedRowNewerThanCheckpoint
                    ? "newer_rows_returned"
                    : settledFetched == 0 && pendingFetched > 0
                        ? "pending_only_rows_returned"
                        : "no_newer_rows_returned";

        return ServiceResult<BankSyncResult>.Ok(
            new BankSyncResult(
                connection.Id,
                accountsSynced,
                balancesSynced,
                transactionsImported,
                settledFetched,
                pendingFetched,
                latestFetchedRowUtc,
                hasFetchedRowNewerThanCheckpoint,
                freshnessSummary,
                BankConnectionStatuses.Synced,
                DateTime.UtcNow,
                dataChanged,
                historicalEnrichmentInProgress,
                historicalEnrichmentCompleted,
                historicalEnrichmentProgressPercent,
                historicalEnrichmentCheckpointUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Bank sync crashed unexpectedly connectionId={ConnectionId} trigger={Trigger}",
                connection.Id,
                trigger);

            return await HandleSyncFailureAsync(
                connection,
                new ServiceError(
                    "Unexpected error during bank sync execution.",
                    "bank_sync_unexpected_exception",
                    StatusCodes.Status500InternalServerError),
                trigger,
                "unexpected_exception",
                cancellationToken);
        }
    }

    private async Task<ServiceResult> StoreRefreshedTokenAsync(
        OpenBankingConnection connection,
        TrueLayerTokenExchangeResult tokenResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var encryptedRefreshToken = secretProtector.Protect(tokenResult.RefreshToken);
            await bankConnectionService.StoreTokenAsync(
                connection,
                encryptedRefreshToken,
                tokenResult.AccessTokenExpiresUtc,
                cancellationToken);
            ApplyGrantedScopes(connection, tokenResult.Scope);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult.Ok();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to protect/store refresh token for connectionId={ConnectionId}",
                connection.Id);

            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                "token_storage_failed",
                "Token storage failed.",
                cancellationToken);

            return ServiceResult.Fail(
                "Secure token storage failed.",
                "token_storage_failed",
                StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<LinkedBankAccount> UpsertLinkedAccountAsync(
        OpenBankingConnection connection,
        TrueLayerAccountRecord providerAccount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var linkedAccount = await dbContext.LinkedBankAccounts
            .Include(x => x.FinancialAccount)
            .SingleOrDefaultAsync(
                x => x.ConnectionId == connection.Id && x.ProviderAccountId == providerAccount.AccountId,
                cancellationToken);

        if (linkedAccount is null)
        {
            linkedAccount = new LinkedBankAccount
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                ProviderAccountId = providerAccount.AccountId,
                CreatedUtc = now
            };
            dbContext.LinkedBankAccounts.Add(linkedAccount);
        }

        var resolvedDisplayName = ResolveProviderAccountDisplayName(connection, providerAccount);
        linkedAccount.DisplayName = resolvedDisplayName;
        linkedAccount.AccountType = providerAccount.AccountType;
        linkedAccount.AccountSubType = providerAccount.AccountSubType;
        linkedAccount.Currency = providerAccount.Currency;
        linkedAccount.AccountNumberMetadataJson = providerAccount.AccountNumberMetadataJson;
        linkedAccount.RawPayloadJson = providerAccount.RawPayloadJson;
        linkedAccount.CurrentConnectionHealth = "healthy";
        linkedAccount.UpdatedUtc = now;

        if (linkedAccount.FinancialAccount is null)
        {
            var projectionAccount = new FinancialAccount
            {
                Id = Guid.NewGuid(),
                UserId = connection.UserId,
                Name = resolvedDisplayName,
                Type = MapAccountType(providerAccount.AccountType),
                Currency = providerAccount.Currency,
                CreatedUtc = now
            };
            dbContext.FinancialAccounts.Add(projectionAccount);
            linkedAccount.FinancialAccount = projectionAccount;
            linkedAccount.FinancialAccountId = projectionAccount.Id;
        }
        else
        {
            linkedAccount.FinancialAccount.Name = resolvedDisplayName;
            linkedAccount.FinancialAccount.Type = MapAccountType(providerAccount.AccountType);
            linkedAccount.FinancialAccount.Currency = providerAccount.Currency;
        }

        return linkedAccount;
    }

    private async Task<bool> ShouldPersistBalanceSnapshotAsync(
        Guid linkedBankAccountId,
        TrueLayerBalanceRecord candidate,
        CancellationToken cancellationToken)
    {
        var latest = await dbContext.BankBalanceSnapshots
            .AsNoTracking()
            .Where(x => x.LinkedBankAccountId == linkedBankAccountId)
            .OrderByDescending(x => x.CapturedUtc)
            .Select(x => new
            {
                x.Available,
                x.Current,
                x.Overdraft,
                x.Currency,
                x.CapturedUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return true;
        }

        return latest.CapturedUtc != candidate.CapturedAtUtc
            || latest.Available != candidate.Available
            || latest.Current != candidate.Current
            || latest.Overdraft != candidate.Overdraft
            || !string.Equals(latest.Currency, candidate.Currency, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ShouldPersistCardBalanceSnapshotAsync(
        Guid linkedBankCardId,
        TrueLayerCardBalanceRecord candidate,
        CancellationToken cancellationToken)
    {
        var latest = await dbContext.BankCardBalanceSnapshots
            .AsNoTracking()
            .Where(x => x.LinkedBankCardId == linkedBankCardId)
            .OrderByDescending(x => x.CapturedUtc)
            .Select(x => new
            {
                x.Available,
                x.Current,
                x.Limit,
                x.Outstanding,
                x.Currency,
                x.CapturedUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return true;
        }

        return latest.CapturedUtc != candidate.CapturedAtUtc
            || latest.Available != candidate.Available
            || latest.Current != candidate.Current
            || latest.Limit != candidate.Limit
            || latest.Outstanding != candidate.Outstanding
            || !string.Equals(latest.Currency, candidate.Currency, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TransactionUpsertSummary> UpsertTransactionsAsync(
        LinkedBankAccount linkedAccount,
        TrueLayerAccountRecord providerAccount,
        ProviderTransactionSyncPolicy providerPolicy,
        IReadOnlyList<TrueLayerTransactionRecord> providerTransactions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var fetchedCount = providerTransactions.Count;
        var existingRawRows = await dbContext.RawBankTransactions
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id)
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.ImportedUtc)
            .ToListAsync(cancellationToken);

        var existingRawByProviderId = new Dictionary<string, RawBankTransaction>(StringComparer.Ordinal);
        var existingRawByDedupeKey = new Dictionary<string, RawBankTransaction>(StringComparer.Ordinal);
        foreach (var row in existingRawRows.OrderByDescending(x => x.ImportedUtc))
        {
            if (!string.IsNullOrWhiteSpace(row.ProviderTransactionId)
                && !existingRawByProviderId.ContainsKey(row.ProviderTransactionId))
            {
                existingRawByProviderId[row.ProviderTransactionId] = row;
            }

            if (!existingRawByDedupeKey.ContainsKey(row.DedupeKey))
            {
                existingRawByDedupeKey[row.DedupeKey] = row;
            }
        }

        var normalizedRowsByRawId = await dbContext.NormalizedBankTransactions
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id)
            .ToDictionaryAsync(x => x.RawBankTransactionId, cancellationToken);

        var rawInserted = 0;
        var rawUpdated = 0;
        var rawSkippedProviderId = 0;
        var rawSkippedDedupe = 0;
        var projectedFromNewRaw = 0;
        var projectedFromStatusTransition = 0;
        var projectedBackfilled = 0;
        var projectedSkippedUnbookedFetched = 0;
        var projectedSkippedUnbookedBackfill = 0;
        var projectedSkippedDuplicate = 0;
        var projectedDuplicateCheckAttempts = 0;
        var projectedBackfillRowsEvaluated = 0;
        var projectedBackfillRowsDeferred = 0;
        var projectedCandidatePoolSize = 0;
        var projectedTransactionIdsForDeterministicReclassification = new HashSet<Guid>();
        var normalizedTransactionIdsForMerchantResolution = new HashSet<Guid>();
        ProjectionReconciliationState? projectionState = null;

        static void MarkProjectedForDeterministicReclassification(
            HashSet<Guid> target,
            Guid? projectedTransactionId)
        {
            if (projectedTransactionId.HasValue)
            {
                target.Add(projectedTransactionId.Value);
            }
        }

        if (linkedAccount.FinancialAccountId.HasValue)
        {
            projectionState = await BuildProjectionReconciliationStateAsync(
                linkedAccount.FinancialAccountId.Value,
                existingRawRows,
                cancellationToken);
            projectedCandidatePoolSize = projectionState.ProjectedCandidateCount;

            foreach (var row in existingRawRows)
            {
                if (row.ProjectedTransactionId.HasValue
                    && projectionState.KnownProjectedTransactionIds.Contains(row.ProjectedTransactionId.Value))
                {
                    continue;
                }

                if (row.ProjectedTransactionId.HasValue)
                {
                    // Stale pointer (projected row deleted/moved). Reconcile again.
                    row.ProjectedTransactionId = null;
                }

                if (!IsBookedProjectionStatus(row.TransactionStatus))
                {
                    projectedSkippedUnbookedBackfill++;
                    continue;
                }

                if (projectedBackfillRowsEvaluated >= ProjectionBackfillReconcileMaxRowsPerSync)
                {
                    projectedBackfillRowsDeferred++;
                    continue;
                }

                projectedBackfillRowsEvaluated++;
                projectedDuplicateCheckAttempts++;

                if (TryLinkRawRowToExistingProjectedTransaction(
                        row,
                        providerAccount,
                        linkedAccount,
                        sourceStage: "backfill_reconcile",
                        triggerRecord: null,
                        projectionState,
                        out var collidedTransactionId))
                {
                    projectedSkippedDuplicate++;
                    logger.LogDebug(
                        "Bank transaction projection dedupe matched existing ledger row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} sourceStage={SourceStage} existingTransactionId={ExistingTransactionId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} description={Description}",
                        providerAccount.ProviderId ?? "<unknown>",
                        providerAccount.ProviderDisplayName ?? "<unknown>",
                        linkedAccount.ConnectionId,
                        providerAccount.AccountId,
                        linkedAccount.Id,
                        "backfill_reconcile",
                        collidedTransactionId,
                        row.ProviderTransactionId ?? "<none>",
                        row.DedupeKey,
                        row.Amount,
                        row.Currency,
                        row.BookedAtUtc,
                        row.Description);
                    MarkProjectedForDeterministicReclassification(
                        projectedTransactionIdsForDeterministicReclassification,
                        row.ProjectedTransactionId);
                    continue;
                }

                var projected = CreateProjectedTransaction(
                    linkedAccount.FinancialAccountId.Value,
                    row.Amount,
                    row.Currency,
                    row.Description,
                    row.BookedAtUtc,
                    now);

                dbContext.Transactions.Add(projected);
                row.ProjectedTransactionId = projected.Id;
                projectionState.KnownProjectedTransactionIds.Add(projected.Id);
                MarkProjectedForDeterministicReclassification(
                    projectedTransactionIdsForDeterministicReclassification,
                    projected.Id);
                projectedBackfilled++;
            }

            if (projectedBackfillRowsDeferred > 0)
            {
                logger.LogInformation(
                    "Deferred projection backfill reconciliation rows to keep sync bounded accountId={AccountId} connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} backfillEvaluated={BackfillEvaluated} backfillDeferred={BackfillDeferred} maxRowsPerSync={MaxRowsPerSync}",
                    providerAccount.AccountId,
                    linkedAccount.ConnectionId,
                    providerAccount.ProviderId ?? "<unknown>",
                    providerAccount.ProviderDisplayName ?? "<unknown>",
                    projectedBackfillRowsEvaluated,
                    projectedBackfillRowsDeferred,
                    ProjectionBackfillReconcileMaxRowsPerSync);
            }
        }

        if (providerTransactions.Count > 0)
        {
            var timestampSourceBreakdown = string.Join(
                ",",
                providerTransactions
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.TimestampSource) ? "unknown" : x.TimestampSource, StringComparer.OrdinalIgnoreCase)
                    .Select(group => $"{group.Key}:{group.Count()}")
                    .OrderBy(x => x, StringComparer.Ordinal));

            var timestampPrecisionBreakdown = string.Join(
                ",",
                providerTransactions
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.TimestampPrecision) ? "unknown_needs_verification" : x.TimestampPrecision, StringComparer.OrdinalIgnoreCase)
                    .Select(group => $"{group.Key}:{group.Count()}")
                    .OrderBy(x => x, StringComparer.Ordinal));

            var rawTimestampSamples = string.Join(
                ",",
                providerTransactions
                    .Select(x => x.ProviderTimestampRaw)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .Take(3));

            logger.LogInformation(
                "Fetched transaction timestamp provenance accountId={AccountId} connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} sourceBreakdown={SourceBreakdown} precisionBreakdown={PrecisionBreakdown} rawTimestampSamples={RawTimestampSamples}",
                providerAccount.AccountId,
                linkedAccount.ConnectionId,
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                string.IsNullOrWhiteSpace(timestampSourceBreakdown) ? "<none>" : timestampSourceBreakdown,
                string.IsNullOrWhiteSpace(timestampPrecisionBreakdown) ? "<none>" : timestampPrecisionBreakdown,
                string.IsNullOrWhiteSpace(rawTimestampSamples) ? "<none>" : rawTimestampSamples);
        }

        foreach (var providerTransaction in providerTransactions)
        {
            logger.LogDebug(
                "Bank transaction normalization providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} sourceEndpoint={SourceEndpoint} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} valueAtUtc={ValueAtUtc} providerTimestampRaw={ProviderTimestampRaw} valueTimestampRaw={ValueTimestampRaw} timestampSource={TimestampSource} timestampPrecision={TimestampPrecision} providerStatus={ProviderStatus} normalizedStatus={NormalizedStatus} normalizationReason={NormalizationReason}",
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                linkedAccount.ConnectionId,
                providerAccount.AccountId,
                linkedAccount.Id,
                providerTransaction.SourceEndpoint,
                providerTransaction.ProviderTransactionId ?? "<none>",
                providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                providerTransaction.DedupeKey,
                providerTransaction.Amount,
                providerTransaction.Currency,
                providerTransaction.BookedAtUtc,
                providerTransaction.ValueAtUtc,
                providerTransaction.ProviderTimestampRaw ?? "<none>",
                providerTransaction.ValueTimestampRaw ?? "<none>",
                providerTransaction.TimestampSource,
                providerTransaction.TimestampPrecision,
                providerTransaction.ProviderStatus ?? "<none>",
                providerTransaction.TransactionStatus ?? "<null>",
                providerTransaction.StatusNormalizationReason);

            var matchedByProviderId = false;
            RawBankTransaction? existingRaw = null;
            if (!string.IsNullOrWhiteSpace(providerTransaction.ProviderTransactionId)
                && existingRawByProviderId.TryGetValue(providerTransaction.ProviderTransactionId, out var existingByProviderId))
            {
                existingRaw = existingByProviderId;
                matchedByProviderId = true;
            }
            else if (existingRawByDedupeKey.TryGetValue(providerTransaction.DedupeKey, out var existingByDedupeKey))
            {
                existingRaw = existingByDedupeKey;
            }

            if (existingRaw is not null)
            {
                var wasBooked = IsBookedProjectionStatus(existingRaw.TransactionStatus);
                var previousProviderTransactionId = existingRaw.ProviderTransactionId;
                var previousDedupeKey = existingRaw.DedupeKey;
                var changed = ApplyRawTransactionUpdate(
                    existingRaw,
                    providerTransaction,
                    providerPolicy.ProviderKey,
                    now);
                if (changed)
                {
                    rawUpdated++;
                    MarkProjectedForDeterministicReclassification(
                        projectedTransactionIdsForDeterministicReclassification,
                        existingRaw.ProjectedTransactionId);
                }
                else if (matchedByProviderId)
                {
                    rawSkippedProviderId++;
                }
                else
                {
                    rawSkippedDedupe++;
                }

                logger.LogDebug(
                    "Bank raw transaction upsert decision providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} matchStrategy={MatchStrategy} rawOutcome={RawOutcome} existingRawId={ExistingRawId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} description={Description}",
                    providerAccount.ProviderId ?? "<unknown>",
                    providerAccount.ProviderDisplayName ?? "<unknown>",
                    linkedAccount.ConnectionId,
                    providerAccount.AccountId,
                    linkedAccount.Id,
                    matchedByProviderId ? "provider_transaction_id" : "dedupe_key",
                    changed
                        ? "raw_updated_existing"
                        : matchedByProviderId
                            ? "raw_skipped_provider_id_unchanged"
                            : "raw_skipped_dedupe_unchanged",
                    existingRaw.Id,
                    providerTransaction.ProviderTransactionId ?? existingRaw.ProviderTransactionId ?? "<none>",
                    providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                    providerTransaction.DedupeKey,
                    providerTransaction.Amount,
                    providerTransaction.Currency,
                    providerTransaction.BookedAtUtc,
                    providerTransaction.Description);

                if (!string.Equals(previousProviderTransactionId, existingRaw.ProviderTransactionId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(previousProviderTransactionId))
                {
                    existingRawByProviderId.Remove(previousProviderTransactionId);
                }

                if (!string.Equals(previousDedupeKey, existingRaw.DedupeKey, StringComparison.Ordinal))
                {
                    existingRawByDedupeKey.Remove(previousDedupeKey);
                }

                if (!string.IsNullOrWhiteSpace(existingRaw.ProviderTransactionId))
                {
                    existingRawByProviderId[existingRaw.ProviderTransactionId] = existingRaw;
                }

                existingRawByDedupeKey[existingRaw.DedupeKey] = existingRaw;

                var isNowBooked = IsBookedProjectionStatus(existingRaw.TransactionStatus);
                if (projectionState is not null
                    && linkedAccount.FinancialAccountId.HasValue
                    && isNowBooked)
                {
                    if (existingRaw.ProjectedTransactionId.HasValue
                        && projectionState.KnownProjectedTransactionIds.Contains(existingRaw.ProjectedTransactionId.Value))
                    {
                        logger.LogDebug(
                            "Bank transaction already linked to projected ledger row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} projectedTransactionId={ProjectedTransactionId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            linkedAccount.Id,
                            existingRaw.ProjectedTransactionId.Value,
                            existingRaw.ProviderTransactionId ?? "<none>",
                            existingRaw.DedupeKey);
                    }
                    else
                    {
                        projectedDuplicateCheckAttempts++;

                        if (TryLinkRawRowToExistingProjectedTransaction(
                                 existingRaw,
                                 providerAccount,
                                 linkedAccount,
                                 sourceStage: !wasBooked && isNowBooked ? "status_transition_reconcile" : "existing_booked_reconcile",
                                 triggerRecord: providerTransaction,
                                 projectionState,
                                 out var collidedTransactionId))
                        {
                            projectedSkippedDuplicate++;
                            logger.LogDebug(
                                "Bank transaction projection dedupe matched existing ledger row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} sourceStage={SourceStage} existingTransactionId={ExistingTransactionId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} description={Description}",
                                providerAccount.ProviderId ?? "<unknown>",
                                providerAccount.ProviderDisplayName ?? "<unknown>",
                                linkedAccount.ConnectionId,
                                providerAccount.AccountId,
                                linkedAccount.Id,
                                !wasBooked && isNowBooked ? "status_transition_reconcile" : "existing_booked_reconcile",
                                collidedTransactionId,
                                providerTransaction.ProviderTransactionId ?? existingRaw.ProviderTransactionId ?? "<none>",
                                providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                                existingRaw.DedupeKey,
                                existingRaw.Amount,
                                existingRaw.Currency,
                                existingRaw.BookedAtUtc,
                                existingRaw.Description);
                            MarkProjectedForDeterministicReclassification(
                                projectedTransactionIdsForDeterministicReclassification,
                                existingRaw.ProjectedTransactionId);
                        }
                        else
                        {
                            var projected = CreateProjectedTransaction(
                                linkedAccount.FinancialAccountId.Value,
                                existingRaw.Amount,
                                existingRaw.Currency,
                                existingRaw.Description,
                                existingRaw.BookedAtUtc,
                                now);

                            dbContext.Transactions.Add(projected);
                            existingRaw.ProjectedTransactionId = projected.Id;
                            projectionState.KnownProjectedTransactionIds.Add(projected.Id);
                            MarkProjectedForDeterministicReclassification(
                                projectedTransactionIdsForDeterministicReclassification,
                                projected.Id);

                            if (!wasBooked && isNowBooked)
                            {
                                projectedFromStatusTransition++;
                            }
                            else
                            {
                                projectedBackfilled++;
                            }

                            logger.LogDebug(
                                "Bank transaction projected from existing raw row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} fromStatus={FromStatus} toStatus={ToStatus}",
                                providerAccount.ProviderId ?? "<unknown>",
                                providerAccount.ProviderDisplayName ?? "<unknown>",
                                linkedAccount.ConnectionId,
                                providerAccount.AccountId,
                                linkedAccount.Id,
                                providerTransaction.ProviderTransactionId ?? existingRaw.ProviderTransactionId ?? "<none>",
                                providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                                existingRaw.DedupeKey,
                                wasBooked ? "booked" : "unbooked",
                                isNowBooked ? "booked" : "unbooked");
                        }
                    }
                }
                else if (!isNowBooked)
                {
                    projectedSkippedUnbookedFetched++;
                    logger.LogDebug(
                        "Bank transaction projection skipped because normalized status is unbooked providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} normalizedStatus={NormalizedStatus} sourceEndpoint={SourceEndpoint} providerStatus={ProviderStatus} normalizationReason={NormalizationReason}",
                        providerAccount.ProviderId ?? "<unknown>",
                        providerAccount.ProviderDisplayName ?? "<unknown>",
                        linkedAccount.ConnectionId,
                        providerAccount.AccountId,
                        linkedAccount.Id,
                        providerTransaction.ProviderTransactionId ?? "<none>",
                        providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                        providerTransaction.DedupeKey,
                        providerTransaction.TransactionStatus ?? "<null>",
                        providerTransaction.SourceEndpoint,
                        providerTransaction.ProviderStatus ?? "<none>",
                        providerTransaction.StatusNormalizationReason);
                }

                var normalizedOutcome = UpsertNormalizedBankTransaction(
                    normalizedRowsByRawId,
                    existingRaw,
                    linkedAccount,
                    providerPolicy,
                    providerTransaction,
                    now);
                if (ShouldQueueMerchantResolution(normalizedOutcome, existingRaw))
                {
                    normalizedTransactionIdsForMerchantResolution.Add(normalizedOutcome.NormalizedTransactionId);
                }

                continue;
            }

            var rawTransaction = new RawBankTransaction
            {
                Id = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                ProviderTransactionId = providerTransaction.ProviderTransactionId,
                NormalizedProviderTransactionId = providerTransaction.NormalizedProviderTransactionId,
                DedupeKey = providerTransaction.DedupeKey,
                Amount = providerTransaction.Amount,
                Currency = providerTransaction.Currency,
                BookedAtUtc = providerTransaction.BookedAtUtc,
                ValueAtUtc = providerTransaction.ValueAtUtc,
                Description = providerTransaction.Description,
                TransactionType = providerTransaction.TransactionType,
                TransactionStatus = providerTransaction.TransactionStatus,
                SourceEndpoint = providerTransaction.SourceEndpoint,
                ProviderStatus = providerTransaction.ProviderStatus,
                StatusNormalizationReason = providerTransaction.StatusNormalizationReason,
                ProviderTimestampRaw = providerTransaction.ProviderTimestampRaw,
                ValueTimestampRaw = providerTransaction.ValueTimestampRaw,
                TimestampSource = providerTransaction.TimestampSource,
                TimestampPrecision = providerTransaction.TimestampPrecision,
                TimestampNormalizationPolicyKey = providerPolicy.ProviderKey,
                ProjectedTransactionId = null,
                RawPayloadJson = providerTransaction.RawPayloadJson,
                ImportedUtc = now
            };
            dbContext.RawBankTransactions.Add(rawTransaction);
            logger.LogDebug(
                "Bank raw transaction upsert decision providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} matchStrategy={MatchStrategy} rawOutcome={RawOutcome} existingRawId={ExistingRawId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} description={Description}",
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                linkedAccount.ConnectionId,
                providerAccount.AccountId,
                linkedAccount.Id,
                "none",
                "raw_inserted",
                null,
                providerTransaction.ProviderTransactionId ?? "<none>",
                providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                providerTransaction.DedupeKey,
                providerTransaction.Amount,
                providerTransaction.Currency,
                providerTransaction.BookedAtUtc,
                providerTransaction.Description);

            if (projectionState is not null && linkedAccount.FinancialAccountId.HasValue)
            {
                if (IsBookedProjectionStatus(providerTransaction.TransactionStatus))
                {
                    projectedDuplicateCheckAttempts++;
                    if (TryLinkRawRowToExistingProjectedTransaction(
                            rawTransaction,
                            providerAccount,
                            linkedAccount,
                            sourceStage: "new_raw_reconcile",
                            triggerRecord: providerTransaction,
                            projectionState,
                            out var collidedTransactionId))
                    {
                        projectedSkippedDuplicate++;
                        logger.LogDebug(
                            "Bank transaction projection dedupe matched existing ledger row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} sourceStage={SourceStage} existingTransactionId={ExistingTransactionId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} description={Description}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            linkedAccount.Id,
                            "new_raw_reconcile",
                            collidedTransactionId,
                            providerTransaction.ProviderTransactionId ?? "<none>",
                            providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                            providerTransaction.DedupeKey,
                            providerTransaction.Amount,
                            providerTransaction.Currency,
                            providerTransaction.BookedAtUtc,
                            providerTransaction.Description);
                        MarkProjectedForDeterministicReclassification(
                            projectedTransactionIdsForDeterministicReclassification,
                            rawTransaction.ProjectedTransactionId);
                    }
                    else
                    {
                        var projected = CreateProjectedTransaction(
                            linkedAccount.FinancialAccountId.Value,
                            providerTransaction.Amount,
                            providerTransaction.Currency,
                            providerTransaction.Description,
                            providerTransaction.BookedAtUtc,
                            now);

                        dbContext.Transactions.Add(projected);
                        rawTransaction.ProjectedTransactionId = projected.Id;
                        projectionState.KnownProjectedTransactionIds.Add(projected.Id);
                        MarkProjectedForDeterministicReclassification(
                            projectedTransactionIdsForDeterministicReclassification,
                            projected.Id);
                        projectedFromNewRaw++;
                        logger.LogDebug(
                            "Bank transaction projected from new raw row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            linkedAccount.Id,
                            providerTransaction.ProviderTransactionId ?? "<none>",
                            providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                            providerTransaction.DedupeKey);
                    }
                }
                else
                {
                    projectedSkippedUnbookedFetched++;
                    logger.LogDebug(
                        "Bank transaction projection skipped because normalized status is unbooked for new raw row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} normalizedStatus={NormalizedStatus} sourceEndpoint={SourceEndpoint} providerStatus={ProviderStatus} normalizationReason={NormalizationReason}",
                        providerAccount.ProviderId ?? "<unknown>",
                        providerAccount.ProviderDisplayName ?? "<unknown>",
                        linkedAccount.ConnectionId,
                        providerAccount.AccountId,
                        linkedAccount.Id,
                        providerTransaction.ProviderTransactionId ?? "<none>",
                        providerTransaction.NormalizedProviderTransactionId ?? "<none>",
                        providerTransaction.DedupeKey,
                        providerTransaction.TransactionStatus ?? "<null>",
                        providerTransaction.SourceEndpoint,
                        providerTransaction.ProviderStatus ?? "<none>",
                        providerTransaction.StatusNormalizationReason);
                }
            }

            if (!string.IsNullOrWhiteSpace(providerTransaction.ProviderTransactionId))
            {
                existingRawByProviderId[providerTransaction.ProviderTransactionId] = rawTransaction;
            }

            existingRawByDedupeKey[providerTransaction.DedupeKey] = rawTransaction;
            var insertedNormalizedOutcome = UpsertNormalizedBankTransaction(
                normalizedRowsByRawId,
                rawTransaction,
                linkedAccount,
                providerPolicy,
                providerTransaction,
                now);
            if (ShouldQueueMerchantResolution(insertedNormalizedOutcome, rawTransaction))
            {
                normalizedTransactionIdsForMerchantResolution.Add(insertedNormalizedOutcome.NormalizedTransactionId);
            }
            rawInserted++;
        }

        return new TransactionUpsertSummary(
            fetchedCount,
            rawInserted,
            rawUpdated,
            rawSkippedProviderId,
            rawSkippedDedupe,
            projectedFromNewRaw,
            projectedFromStatusTransition,
            projectedBackfilled,
            projectedSkippedUnbookedFetched,
            projectedSkippedUnbookedBackfill,
            projectedSkippedDuplicate,
            projectedDuplicateCheckAttempts,
            projectedBackfillRowsEvaluated,
            projectedBackfillRowsDeferred,
            projectedCandidatePoolSize,
            projectedTransactionIdsForDeterministicReclassification.ToArray(),
            normalizedTransactionIdsForMerchantResolution.ToArray());
    }

    private async Task<ProjectionReconciliationState> BuildProjectionReconciliationStateAsync(
        Guid projectedAccountId,
        IReadOnlyList<RawBankTransaction> existingRawRows,
        CancellationToken cancellationToken)
    {
        var projectedRows = await dbContext.Transactions
            .Where(x => x.FinancialAccountId == projectedAccountId)
            .Select(x => new ProjectedTransactionSnapshot(
                x.Id,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                x.Description))
            .ToListAsync(cancellationToken);

        var knownProjectedTransactionIds = projectedRows
            .Select(x => x.Id)
            .ToHashSet();

        var linkedProjectedTransactionIds = existingRawRows
            .Where(x => x.ProjectedTransactionId.HasValue && knownProjectedTransactionIds.Contains(x.ProjectedTransactionId.Value))
            .Select(x => x.ProjectedTransactionId!.Value)
            .ToHashSet();

        var fingerprintToProjectedIds = new Dictionary<string, Queue<Guid>>(StringComparer.Ordinal);
        foreach (var row in projectedRows.OrderBy(x => x.BookedAtUtc).ThenBy(x => x.Id))
        {
            var fingerprint = CreateProjectionFingerprint(
                row.Amount,
                row.Currency,
                row.BookedAtUtc,
                row.Description);

            if (!fingerprintToProjectedIds.TryGetValue(fingerprint, out var queue))
            {
                queue = new Queue<Guid>();
                fingerprintToProjectedIds[fingerprint] = queue;
            }

            queue.Enqueue(row.Id);
        }

        return new ProjectionReconciliationState(
            knownProjectedTransactionIds,
            linkedProjectedTransactionIds,
            fingerprintToProjectedIds,
            projectedRows.Count);
    }

    private bool TryLinkRawRowToExistingProjectedTransaction(
        RawBankTransaction rawRow,
        TrueLayerAccountRecord providerAccount,
        LinkedBankAccount linkedAccount,
        string sourceStage,
        TrueLayerTransactionRecord? triggerRecord,
        ProjectionReconciliationState projectionState,
        out Guid collidedTransactionId)
    {
        collidedTransactionId = Guid.Empty;
        var projectionFingerprint = CreateProjectionFingerprint(
            rawRow.Amount,
            rawRow.Currency,
            rawRow.BookedAtUtc,
            rawRow.Description);

        if (!projectionState.FingerprintToProjectedIds.TryGetValue(projectionFingerprint, out var queue))
        {
            return false;
        }

        while (queue.Count > 0)
        {
            var candidateId = queue.Peek();
            if (projectionState.LinkedProjectedTransactionIds.Contains(candidateId))
            {
                queue.Dequeue();
                continue;
            }

            collidedTransactionId = candidateId;
            projectionState.LinkedProjectedTransactionIds.Add(candidateId);
            rawRow.ProjectedTransactionId = candidateId;

            logger.LogDebug(
                "Bank transaction projection collision details providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} sourceStage={SourceStage} existingTransactionId={ExistingTransactionId} fingerprint={ProjectionFingerprint} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} description={Description} providerTransactionId={ProviderTransactionId} normalizedProviderTransactionId={NormalizedProviderTransactionId} dedupeKey={DedupeKey} sourceEndpoint={SourceEndpoint} providerStatus={ProviderStatus} normalizedStatus={NormalizedStatus}",
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                linkedAccount.ConnectionId,
                providerAccount.AccountId,
                linkedAccount.Id,
                sourceStage,
                candidateId,
                projectionFingerprint,
                rawRow.Amount,
                rawRow.Currency,
                rawRow.BookedAtUtc,
                rawRow.Description,
                triggerRecord?.ProviderTransactionId ?? rawRow.ProviderTransactionId ?? "<none>",
                triggerRecord?.NormalizedProviderTransactionId ?? "<none>",
                rawRow.DedupeKey,
                triggerRecord?.SourceEndpoint ?? "<backfill_raw>",
                triggerRecord?.ProviderStatus ?? "<none>",
                triggerRecord?.TransactionStatus ?? rawRow.TransactionStatus ?? "<null>");

            return true;
        }

        return false;
    }

    private static Transaction CreateProjectedTransaction(
        Guid projectedAccountId,
        decimal amount,
        string currency,
        string description,
        DateTime bookedAtUtc,
        DateTime createdUtc)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = projectedAccountId,
            Amount = amount,
            Currency = currency,
            Description = description,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = createdUtc
        };
    }

    private static bool ApplyRawTransactionUpdate(
        RawBankTransaction existing,
        TrueLayerTransactionRecord incoming,
        string timestampNormalizationPolicyKey,
        DateTime importedUtc)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(incoming.ProviderTransactionId)
            && !string.Equals(existing.ProviderTransactionId, incoming.ProviderTransactionId, StringComparison.Ordinal))
        {
            existing.ProviderTransactionId = incoming.ProviderTransactionId;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(incoming.NormalizedProviderTransactionId)
            && !string.Equals(existing.NormalizedProviderTransactionId, incoming.NormalizedProviderTransactionId, StringComparison.Ordinal))
        {
            existing.NormalizedProviderTransactionId = incoming.NormalizedProviderTransactionId;
            changed = true;
        }

        if (!string.Equals(existing.DedupeKey, incoming.DedupeKey, StringComparison.Ordinal))
        {
            existing.DedupeKey = incoming.DedupeKey;
            changed = true;
        }

        if (existing.Amount != incoming.Amount)
        {
            existing.Amount = incoming.Amount;
            changed = true;
        }

        if (!string.Equals(existing.Currency, incoming.Currency, StringComparison.OrdinalIgnoreCase))
        {
            existing.Currency = incoming.Currency;
            changed = true;
        }

        if (existing.BookedAtUtc != incoming.BookedAtUtc)
        {
            existing.BookedAtUtc = incoming.BookedAtUtc;
            changed = true;
        }

        if (existing.ValueAtUtc != incoming.ValueAtUtc)
        {
            existing.ValueAtUtc = incoming.ValueAtUtc;
            changed = true;
        }

        if (!string.Equals(existing.Description, incoming.Description, StringComparison.Ordinal))
        {
            existing.Description = incoming.Description;
            changed = true;
        }

        if (!string.Equals(existing.TransactionType, incoming.TransactionType, StringComparison.Ordinal))
        {
            existing.TransactionType = incoming.TransactionType;
            changed = true;
        }

        if (!string.Equals(existing.TransactionStatus, incoming.TransactionStatus, StringComparison.OrdinalIgnoreCase))
        {
            existing.TransactionStatus = incoming.TransactionStatus;
            changed = true;
        }

        if (!string.Equals(existing.SourceEndpoint, incoming.SourceEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            existing.SourceEndpoint = incoming.SourceEndpoint;
            changed = true;
        }

        if (!string.Equals(existing.ProviderStatus, incoming.ProviderStatus, StringComparison.OrdinalIgnoreCase))
        {
            existing.ProviderStatus = incoming.ProviderStatus;
            changed = true;
        }

        if (!string.Equals(existing.StatusNormalizationReason, incoming.StatusNormalizationReason, StringComparison.Ordinal))
        {
            existing.StatusNormalizationReason = incoming.StatusNormalizationReason;
            changed = true;
        }

        if (!string.Equals(existing.ProviderTimestampRaw, incoming.ProviderTimestampRaw, StringComparison.Ordinal))
        {
            existing.ProviderTimestampRaw = incoming.ProviderTimestampRaw;
            changed = true;
        }

        if (!string.Equals(existing.ValueTimestampRaw, incoming.ValueTimestampRaw, StringComparison.Ordinal))
        {
            existing.ValueTimestampRaw = incoming.ValueTimestampRaw;
            changed = true;
        }

        if (!string.Equals(existing.TimestampSource, incoming.TimestampSource, StringComparison.Ordinal))
        {
            existing.TimestampSource = incoming.TimestampSource;
            changed = true;
        }

        if (!string.Equals(existing.TimestampPrecision, incoming.TimestampPrecision, StringComparison.Ordinal))
        {
            existing.TimestampPrecision = incoming.TimestampPrecision;
            changed = true;
        }

        if (!string.Equals(existing.TimestampNormalizationPolicyKey, timestampNormalizationPolicyKey, StringComparison.Ordinal))
        {
            existing.TimestampNormalizationPolicyKey = timestampNormalizationPolicyKey;
            changed = true;
        }

        if (!string.Equals(existing.RawPayloadJson, incoming.RawPayloadJson, StringComparison.Ordinal))
        {
            existing.RawPayloadJson = incoming.RawPayloadJson;
            changed = true;
        }

        if (changed)
        {
            existing.ImportedUtc = importedUtc;
        }

        return changed;
    }

    private NormalizedUpsertOutcome UpsertNormalizedBankTransaction(
        IDictionary<Guid, NormalizedBankTransaction> normalizedRowsByRawId,
        RawBankTransaction rawTransaction,
        LinkedBankAccount linkedAccount,
        ProviderTransactionSyncPolicy providerPolicy,
        TrueLayerTransactionRecord providerTransaction,
        DateTime now)
    {
        var wasCreated = false;
        if (!normalizedRowsByRawId.TryGetValue(rawTransaction.Id, out var normalized))
        {
            normalized = new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = rawTransaction.Id,
                LinkedBankAccountId = linkedAccount.Id,
                FinancialAccountId = linkedAccount.FinancialAccountId,
                ImportedUtc = now
            };
            dbContext.NormalizedBankTransactions.Add(normalized);
            normalizedRowsByRawId[rawTransaction.Id] = normalized;
            wasCreated = true;
        }

        var previousDescription = normalized.Description;
        var hadPriorInterpretation = !string.IsNullOrWhiteSpace(normalized.InterpretationReasonJson);

        normalized.ProjectedTransactionId = rawTransaction.ProjectedTransactionId;
        normalized.ProviderTransactionId = rawTransaction.ProviderTransactionId;
        normalized.NormalizedProviderTransactionId = providerTransaction.NormalizedProviderTransactionId ?? rawTransaction.NormalizedProviderTransactionId;
        normalized.DedupeKey = rawTransaction.DedupeKey;
        normalized.Amount = rawTransaction.Amount;
        normalized.Currency = rawTransaction.Currency;
        normalized.BookedAtUtc = rawTransaction.BookedAtUtc;
        normalized.ValueAtUtc = providerTransaction.ValueAtUtc ?? rawTransaction.ValueAtUtc;
        normalized.Description = rawTransaction.Description;
        normalized.TransactionType = rawTransaction.TransactionType;
        normalized.TransactionStatus = rawTransaction.TransactionStatus;
        normalized.SourceEndpoint = rawTransaction.SourceEndpoint;
        normalized.ProviderStatus = rawTransaction.ProviderStatus;
        normalized.StatusNormalizationReason = rawTransaction.StatusNormalizationReason;
        normalized.ProviderTimestampRaw = providerTransaction.ProviderTimestampRaw ?? rawTransaction.ProviderTimestampRaw;
        normalized.ValueTimestampRaw = providerTransaction.ValueTimestampRaw ?? rawTransaction.ValueTimestampRaw;
        normalized.TimestampSource = providerTransaction.TimestampSource ?? rawTransaction.TimestampSource;
        normalized.TimestampPrecision = providerTransaction.TimestampPrecision ?? rawTransaction.TimestampPrecision;
        normalized.TimestampNormalizedByPolicy = providerPolicy.TimestampPrecision.ToString();
        normalized.NormalizationPolicyKey = providerPolicy.ProviderKey;
        normalized.NormalizationPolicyFamily = providerPolicy.ProviderFamily;
        normalized.LastNormalizedUtc = now;

        return new NormalizedUpsertOutcome(
            normalized.Id,
            wasCreated,
            !string.Equals(previousDescription, normalized.Description, StringComparison.Ordinal),
            hadPriorInterpretation);
    }

    private async Task<MerchantResolutionTriggerSummary> ResolveMerchantResolutionForAccountTransactionsAsync(
        Guid userId,
        Guid connectionId,
        LinkedBankAccount linkedAccount,
        TrueLayerAccountRecord providerAccount,
        IReadOnlyCollection<Guid> normalizedTransactionIds,
        Guid syncRunId,
        MerchantResolutionRunState runState,
        Dictionary<string, MerchantResolutionResult> sharedDescriptorCache,
        HashSet<string> sharedFailedDescriptorKeys,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (normalizedTransactionIds.Count == 0)
        {
            return MerchantResolutionTriggerSummary.Empty;
        }

        var normalizedRows = await dbContext.NormalizedBankTransactions
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id && normalizedTransactionIds.Contains(x.Id))
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.ImportedUtc)
            .ToListAsync(cancellationToken);

        var missingRows = Math.Max(0, normalizedTransactionIds.Count - normalizedRows.Count);
        var skippedIneligible = 0;
        var executed = 0;
        var dedupedReuse = 0;
        var resolved = 0;
        var unresolved = 0;
        var cooldownBlocked = 0;
        var failed = 0;
        var projectedTransactionIds = normalizedRows
            .Where(x => x.ProjectedTransactionId.HasValue)
            .Select(x => x.ProjectedTransactionId!.Value)
            .Distinct()
            .ToArray();
        var projectedContexts = projectedTransactionIds.Length == 0
            ? new Dictionary<Guid, MerchantResolutionProjectedContext>()
            : await dbContext.Transactions
                .Where(x => projectedTransactionIds.Contains(x.Id))
                .Select(x => new MerchantResolutionProjectedContext(
                    ProjectedTransactionId: x.Id,
                    TaxonomyDomainId: x.TaxonomyDomainId,
                    TaxonomyCategoryId: x.TaxonomyCategoryId,
                    TaxonomySubcategoryId: x.TaxonomySubcategoryId,
                    DeterministicTerminal: x.DeterministicClassificationTerminal,
                    DeterministicResultCode: x.DeterministicReasonCode,
                    DeterministicStatus: x.DeterministicClassificationStatus))
                .ToDictionaryAsync(x => x.ProjectedTransactionId!.Value, cancellationToken);

        foreach (var normalized in normalizedRows)
        {
            if (!IsMerchantResolutionEligible(normalized))
            {
                skippedIneligible++;
                continue;
            }

            var descriptor = normalized.Description.Trim();
            if (descriptor.Length == 0)
            {
                skippedIneligible++;
                continue;
            }

            var cacheKey = BuildMerchantResolutionCacheKey(descriptor);
            if (sharedFailedDescriptorKeys.Contains(cacheKey))
            {
                failed++;
                continue;
            }

            if (!sharedDescriptorCache.TryGetValue(cacheKey, out var resolution))
            {
                try
                {
                    projectedContexts.TryGetValue(normalized.ProjectedTransactionId ?? Guid.Empty, out var projectedContext);
                    resolution = await merchantResolutionService.ResolveAsync(
                        new MerchantResolutionRequest(
                            RawDescriptor: descriptor,
                            UserId: userId,
                            ConnectionId: connectionId,
                            SyncRunId: syncRunId,
                            TransactionId: projectedContext?.ProjectedTransactionId ?? normalized.ProjectedTransactionId,
                            NormalizedTransactionId: normalized.Id,
                            TaxonomyDomainId: projectedContext?.TaxonomyDomainId,
                            TaxonomyCategoryId: projectedContext?.TaxonomyCategoryId,
                            TaxonomySubcategoryId: projectedContext?.TaxonomySubcategoryId,
                            DeterministicTerminal: projectedContext?.DeterministicTerminal ?? false,
                            DeterministicResultCode: projectedContext?.DeterministicResultCode
                                                     ?? projectedContext?.DeterministicStatus.ToString(),
                            ManualOverridePresent: false,
                            Amount: normalized.Amount,
                            DescriptorMerchantLike: IsMerchantDescriptorCandidate(descriptor),
                            TriggerSource: "account_transaction_upsert",
                            RunState: runState),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    sharedFailedDescriptorKeys.Add(cacheKey);
                    failed++;
                    logger.LogWarning(
                        ex,
                        "Bank merchant resolution execution failed connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} normalizedTransactionId={NormalizedTransactionId}",
                        connectionId,
                        providerAccount.AccountId,
                        linkedAccount.Id,
                        normalized.Id);
                    continue;
                }

                sharedDescriptorCache[cacheKey] = resolution;
                executed++;
            }
            else
            {
                dedupedReuse++;
            }

            ApplyMerchantResolutionInterpretation(normalized, resolution, now);

            if (resolution.IsResolved)
            {
                resolved++;
            }
            else
            {
                unresolved++;
            }

            if (resolution.ReasonCodes.Any(code => code.Equals("investigation_cooldown_active", StringComparison.OrdinalIgnoreCase)))
            {
                cooldownBlocked++;
            }
        }

        logger.LogInformation(
            "Bank merchant resolution trigger connectionId={ConnectionId} accountId={AccountId} linkedBankAccountId={LinkedBankAccountId} triggerCandidates={TriggerCandidates} executed={Executed} dedupedReuse={DedupedReuse} skippedIneligible={SkippedIneligible} missingRows={MissingRows} failed={Failed} resolved={Resolved} unresolved={Unresolved} cooldownBlocked={CooldownBlocked}",
            connectionId,
            providerAccount.AccountId,
            linkedAccount.Id,
            normalizedTransactionIds.Count,
            executed,
            dedupedReuse,
            skippedIneligible,
            missingRows,
            failed,
            resolved,
            unresolved,
            cooldownBlocked);

        return new MerchantResolutionTriggerSummary(
            normalizedTransactionIds.Count,
            executed,
            dedupedReuse,
            skippedIneligible,
            missingRows,
            failed,
            resolved,
            unresolved,
            cooldownBlocked);
    }

    private static bool ShouldQueueMerchantResolution(NormalizedUpsertOutcome outcome, RawBankTransaction rawTransaction)
    {
        if (!IsBookedProjectionStatus(rawTransaction.TransactionStatus))
        {
            return false;
        }

        if (rawTransaction.Amount >= 0m)
        {
            return false;
        }

        if (!IsMerchantDescriptorCandidate(rawTransaction.Description))
        {
            return false;
        }

        if (LooksLikeInternalTransferDescription(rawTransaction.Description)
            || LooksLikeSavingsPocketMovementDescription(rawTransaction.Description))
        {
            return false;
        }

        return outcome.WasCreated || outcome.DescriptionChanged || !outcome.HadPriorInterpretation;
    }

    private static bool IsMerchantResolutionEligible(NormalizedBankTransaction normalized)
    {
        if (!IsBookedProjectionStatus(normalized.TransactionStatus))
        {
            return false;
        }

        if (normalized.Amount >= 0m)
        {
            return false;
        }

        if (!IsMerchantDescriptorCandidate(normalized.Description))
        {
            return false;
        }

        if (LooksLikeInternalTransferDescription(normalized.Description)
            || LooksLikeSavingsPocketMovementDescription(normalized.Description))
        {
            return false;
        }

        return true;
    }

    private static bool IsMerchantDescriptorCandidate(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var alphanumericCount = 0;
        var hasLetter = false;
        foreach (var character in description)
        {
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }

            alphanumericCount++;
            if (char.IsLetter(character))
            {
                hasLetter = true;
            }

            if (alphanumericCount >= 3 && hasLetter)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildMerchantResolutionCacheKey(string descriptor)
    {
        return string.Join(
            ' ',
            descriptor
                .Trim()
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void ApplyMerchantResolutionInterpretation(
        NormalizedBankTransaction normalized,
        MerchantResolutionResult resolution,
        DateTime now)
    {
        normalized.InterpretationConfidenceScore = (int)Math.Round(
            Math.Clamp(resolution.ResolutionConfidence, 0d, 1d) * 100d,
            MidpointRounding.AwayFromZero);
        normalized.InterpretationConfidenceTier = ResolveMerchantInterpretationTier(resolution);
        normalized.InterpretationReasonJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            trigger = "account_transaction_upsert",
            evaluatedUtc = now,
            isResolved = resolution.IsResolved,
            merchantId = resolution.MerchantId,
            unresolvedMerchantId = resolution.UnresolvedMerchantId,
            resolutionType = resolution.ResolutionType.ToString(),
            acceptanceDecisionType = resolution.AcceptanceDecisionType?.ToString(),
            finalState = resolution.FinalState.ToString(),
            triggerMode = resolution.TriggerMode?.ToString(),
            aiGateDecision = resolution.AIGateDecision,
            aiGateSkipReason = resolution.AIGateSkipReason?.ToString(),
            modelUsed = resolution.ModelUsed,
            confidence = Math.Round(Math.Clamp(resolution.ResolutionConfidence, 0d, 1d), 4, MidpointRounding.AwayFromZero),
            reasonCodes = resolution.ReasonCodes
        });
    }

    private static string ResolveMerchantInterpretationTier(MerchantResolutionResult resolution)
    {
        if (resolution.FinalState == MerchantResolutionFinalState.DeterministicTerminal)
        {
            return "deterministic_terminal";
        }

        if (resolution.FinalState == MerchantResolutionFinalState.AIResolvedTerminal)
        {
            return "resolved_ai_terminal";
        }

        if (resolution.FinalState == MerchantResolutionFinalState.AIEnrichedSuggestionOnly)
        {
            return "ai_suggestion_only";
        }

        if (resolution.FinalState == MerchantResolutionFinalState.NeedsUserConfirmation)
        {
            return "needs_user_confirmation";
        }

        if (resolution.IsResolved)
        {
            return resolution.ResolutionType switch
            {
                MerchantResolutionType.ExactAlias => "resolved_exact_alias",
                MerchantResolutionType.FuzzyAlias => "resolved_fuzzy_alias",
                MerchantResolutionType.FamilyMatch => "resolved_family_match",
                _ => "resolved"
            };
        }

        if (resolution.ReasonCodes.Any(code => code.Equals("investigation_cooldown_active", StringComparison.OrdinalIgnoreCase)))
        {
            return "unresolved_cooldown";
        }

        return resolution.AcceptanceDecisionType switch
        {
            MerchantAcceptanceDecisionType.Rejected => "unresolved_rejected",
            MerchantAcceptanceDecisionType.LowConfidence => "unresolved_low_conf",
            MerchantAcceptanceDecisionType.AcceptedCautious => "unresolved_cautious",
            MerchantAcceptanceDecisionType.Unresolved => "unresolved",
            _ => "unresolved"
        };
    }

    private async Task UpsertIdentityInfoAsync(
        OpenBankingConnection connection,
        TrueLayerIdentityInfoRecord? info,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (info is null)
        {
            return;
        }

        var existing = await dbContext.BankConnectionIdentityInfos
            .SingleOrDefaultAsync(x => x.ConnectionId == connection.Id, cancellationToken);

        if (existing is null)
        {
            existing = new BankConnectionIdentityInfo
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                FetchedUtc = now
            };
            dbContext.BankConnectionIdentityInfos.Add(existing);
        }

        existing.FullName = info.FullName;
        existing.Email = info.Email;
        existing.Phone = info.Phone;
        existing.DateOfBirth = info.DateOfBirth;
        existing.RawPayloadJson = info.RawPayloadJson;
        existing.FetchedUtc = now;
        existing.UpdatedUtc = now;
    }

    private async Task<LinkedBankCard> UpsertLinkedCardAsync(
        OpenBankingConnection connection,
        TrueLayerCardRecord providerCard,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var linkedCard = await dbContext.LinkedBankCards
            .SingleOrDefaultAsync(
                x => x.ConnectionId == connection.Id && x.ProviderCardId == providerCard.CardId,
                cancellationToken);

        if (linkedCard is null)
        {
            linkedCard = new LinkedBankCard
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                ProviderCardId = providerCard.CardId,
                CreatedUtc = now
            };
            dbContext.LinkedBankCards.Add(linkedCard);
        }

        linkedCard.ProviderAccountId = providerCard.ProviderAccountId;
        linkedCard.DisplayName = providerCard.DisplayName;
        linkedCard.Currency = providerCard.Currency;
        linkedCard.CardType = providerCard.CardType;
        linkedCard.CardNetwork = providerCard.CardNetwork;
        linkedCard.CardNumberLastFour = providerCard.CardNumberLastFour;
        linkedCard.NameOnCard = providerCard.NameOnCard;
        linkedCard.ValidFromUtc = providerCard.ValidFromUtc;
        linkedCard.ValidToUtc = providerCard.ValidToUtc;
        linkedCard.CurrentConnectionHealth = "healthy";
        linkedCard.RawPayloadJson = providerCard.RawPayloadJson;
        linkedCard.UpdatedUtc = now;

        return linkedCard;
    }

    private async Task<CardTransactionUpsertSummary> UpsertCardTransactionsAsync(
        LinkedBankCard linkedCard,
        IReadOnlyList<TrueLayerCardTransactionRecord> providerTransactions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingProviderIds = await dbContext.RawBankCardTransactions
            .Where(x => x.LinkedBankCardId == linkedCard.Id && x.ProviderTransactionId != null)
            .Select(x => x.ProviderTransactionId!)
            .ToHashSetAsync(cancellationToken);

        var existingDedupeKeys = await dbContext.RawBankCardTransactions
            .Where(x => x.LinkedBankCardId == linkedCard.Id)
            .Select(x => x.DedupeKey)
            .ToHashSetAsync(cancellationToken);

        var projectedFinancialAccountId = await ResolveProjectedFinancialAccountIdForCardAsync(linkedCard, cancellationToken);
        var existingAccountProviderIds = await dbContext.RawBankTransactions
            .Where(x => x.LinkedBankAccount != null
                && x.LinkedBankAccount.ConnectionId == linkedCard.ConnectionId
                && x.ProviderTransactionId != null)
            .Select(x => x.ProviderTransactionId!)
            .ToHashSetAsync(cancellationToken);
        var existingAccountDedupeKeys = await dbContext.RawBankTransactions
            .Where(x => x.LinkedBankAccount != null
                && x.LinkedBankAccount.ConnectionId == linkedCard.ConnectionId)
            .Select(x => x.DedupeKey)
            .ToHashSetAsync(cancellationToken);

        HashSet<string>? projectionFingerprints = null;
        var projectedInsertedCount = 0;
        var projectedTransactionIdsForDeterministicReclassification = new HashSet<Guid>();
        if (projectedFinancialAccountId.HasValue)
        {
            projectionFingerprints = await dbContext.Transactions
                .Where(x => x.FinancialAccountId == projectedFinancialAccountId.Value)
                .Select(x => CreateProjectionFingerprint(x.Amount, x.Currency, x.BookedAtUtc, x.Description))
                .ToHashSetAsync(cancellationToken);

            var existingCardRows = await dbContext.RawBankCardTransactions
                .Where(x => x.LinkedBankCardId == linkedCard.Id)
                .Select(x => new
                {
                    x.ProviderTransactionId,
                    x.DedupeKey,
                    x.Amount,
                    x.Currency,
                    x.BookedAtUtc,
                    x.Description,
                    x.TransactionStatus
                })
                .ToListAsync(cancellationToken);

            foreach (var row in existingCardRows)
            {
                if (!IsBookedProjectionStatus(row.TransactionStatus))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.ProviderTransactionId)
                    && existingAccountProviderIds.Contains(row.ProviderTransactionId))
                {
                    continue;
                }

                if (existingAccountDedupeKeys.Contains(row.DedupeKey))
                {
                    continue;
                }

                var projectionFingerprint = CreateProjectionFingerprint(
                    row.Amount,
                    row.Currency,
                    row.BookedAtUtc,
                    row.Description);

                if (!projectionFingerprints.Add(projectionFingerprint))
                {
                    continue;
                }

                var projectedTransaction = new Transaction
                {
                    Id = Guid.NewGuid(),
                    FinancialAccountId = projectedFinancialAccountId.Value,
                    Amount = row.Amount,
                    Currency = row.Currency,
                    Description = row.Description,
                    BookedAtUtc = row.BookedAtUtc,
                    CreatedUtc = now
                };
                dbContext.Transactions.Add(projectedTransaction);
                projectedInsertedCount++;
                projectedTransactionIdsForDeterministicReclassification.Add(projectedTransaction.Id);
            }
        }

        var importedCount = 0;
        var projectedSkippedUnbooked = 0;
        foreach (var providerTransaction in providerTransactions)
        {
            if (!string.IsNullOrWhiteSpace(providerTransaction.ProviderTransactionId)
                && existingProviderIds.Contains(providerTransaction.ProviderTransactionId))
            {
                continue;
            }

            if (existingDedupeKeys.Contains(providerTransaction.DedupeKey))
            {
                continue;
            }

            dbContext.RawBankCardTransactions.Add(new RawBankCardTransaction
            {
                Id = Guid.NewGuid(),
                LinkedBankCardId = linkedCard.Id,
                ProviderTransactionId = providerTransaction.ProviderTransactionId,
                DedupeKey = providerTransaction.DedupeKey,
                Amount = providerTransaction.Amount,
                Currency = providerTransaction.Currency,
                BookedAtUtc = providerTransaction.BookedAtUtc,
                Description = providerTransaction.Description,
                TransactionType = providerTransaction.TransactionType,
                TransactionStatus = providerTransaction.TransactionStatus,
                RawPayloadJson = providerTransaction.RawPayloadJson,
                ImportedUtc = now
            });

            if (!string.IsNullOrWhiteSpace(providerTransaction.ProviderTransactionId))
            {
                existingProviderIds.Add(providerTransaction.ProviderTransactionId);
            }

            if (projectedFinancialAccountId.HasValue && projectionFingerprints is not null)
            {
                if (!IsBookedProjectionStatus(providerTransaction.TransactionStatus))
                {
                    projectedSkippedUnbooked++;
                    existingDedupeKeys.Add(providerTransaction.DedupeKey);
                    importedCount++;
                    continue;
                }

                var existsInAccountFeed =
                    (!string.IsNullOrWhiteSpace(providerTransaction.ProviderTransactionId)
                     && existingAccountProviderIds.Contains(providerTransaction.ProviderTransactionId))
                    || existingAccountDedupeKeys.Contains(providerTransaction.DedupeKey);

                if (!existsInAccountFeed)
                {
                    var projectionFingerprint = CreateProjectionFingerprint(
                        providerTransaction.Amount,
                        providerTransaction.Currency,
                        providerTransaction.BookedAtUtc,
                        providerTransaction.Description);

                    if (projectionFingerprints.Add(projectionFingerprint))
                    {
                        var projectedTransaction = new Transaction
                        {
                            Id = Guid.NewGuid(),
                            FinancialAccountId = projectedFinancialAccountId.Value,
                            Amount = providerTransaction.Amount,
                            Currency = providerTransaction.Currency,
                            Description = providerTransaction.Description,
                            BookedAtUtc = providerTransaction.BookedAtUtc,
                            CreatedUtc = now
                        };
                        dbContext.Transactions.Add(projectedTransaction);
                        projectedInsertedCount++;
                        projectedTransactionIdsForDeterministicReclassification.Add(projectedTransaction.Id);
                    }
                }
            }

            existingDedupeKeys.Add(providerTransaction.DedupeKey);
            importedCount++;
        }

        if (projectedSkippedUnbooked > 0)
        {
            logger.LogInformation(
                "Skipped projecting non-booked card transactions into ledger linkedCardId={LinkedCardId} projectedSkippedUnbooked={ProjectedSkippedUnbooked}",
                linkedCard.Id,
                projectedSkippedUnbooked);
        }

        return new CardTransactionUpsertSummary(
            importedCount,
            projectedInsertedCount,
            projectedFinancialAccountId,
            projectedTransactionIdsForDeterministicReclassification.ToArray());
    }

    private async Task<Guid?> ResolveProjectedFinancialAccountIdForCardAsync(
        LinkedBankCard linkedCard,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(linkedCard.ProviderAccountId))
        {
            var matchedByProviderAccount = await dbContext.LinkedBankAccounts
                .Where(x =>
                    x.ConnectionId == linkedCard.ConnectionId
                    && x.ProviderAccountId == linkedCard.ProviderAccountId
                    && x.FinancialAccountId.HasValue)
                .Select(x => x.FinancialAccountId)
                .FirstOrDefaultAsync(cancellationToken);

            if (matchedByProviderAccount.HasValue)
            {
                return matchedByProviderAccount.Value;
            }
        }

        var candidateFinancialAccountIds = await dbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == linkedCard.ConnectionId && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);

        return candidateFinancialAccountIds.Count == 1 ? candidateFinancialAccountIds[0] : null;
    }

    private static string CreateProjectionFingerprint(
        decimal amount,
        string currency,
        DateTime bookedAtUtc,
        string description)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? "EUR"
            : currency.Trim().ToUpperInvariant();
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? "Imported transaction"
            : description.Trim();

        return $"{amount:0.00}|{normalizedCurrency}|{bookedAtUtc:O}|{normalizedDescription}";
    }

    private static bool IsBookedProjectionStatus(string? transactionStatus)
    {
        if (string.IsNullOrWhiteSpace(transactionStatus))
        {
            return true;
        }

        var normalized = transactionStatus.Trim().ToLowerInvariant();
        return normalized is "booked" or "posted" or "settled";
    }

    private sealed record ProjectionReconciliationState(
        HashSet<Guid> KnownProjectedTransactionIds,
        HashSet<Guid> LinkedProjectedTransactionIds,
        Dictionary<string, Queue<Guid>> FingerprintToProjectedIds,
        int ProjectedCandidateCount);

    private sealed record ProjectedTransactionSnapshot(
        Guid Id,
        decimal Amount,
        string Currency,
        DateTime BookedAtUtc,
        string Description);

    private readonly record struct NormalizedUpsertOutcome(
        Guid NormalizedTransactionId,
        bool WasCreated,
        bool DescriptionChanged,
        bool HadPriorInterpretation);

    private sealed record TransactionUpsertSummary(
        int Fetched,
        int RawInserted,
        int RawUpdated,
        int RawSkippedProviderId,
        int RawSkippedDedupe,
        int ProjectedFromNewRaw,
        int ProjectedFromStatusTransition,
        int ProjectedBackfilled,
        int ProjectedSkippedUnbookedFetched,
        int ProjectedSkippedUnbookedBackfill,
        int ProjectedSkippedDuplicate,
        int ProjectedDuplicateCheckAttempts,
        int ProjectedBackfillRowsEvaluated,
        int ProjectedBackfillRowsDeferred,
        int ProjectedCandidatePoolSize,
        IReadOnlyCollection<Guid> ProjectedTransactionIdsForDeterministicReclassification,
        IReadOnlyCollection<Guid> NormalizedTransactionIdsForMerchantResolution);

    private readonly record struct MerchantResolutionTriggerSummary(
        int TriggerCandidates,
        int Executed,
        int DedupedReuse,
        int SkippedIneligible,
        int MissingRows,
        int Failed,
        int Resolved,
        int Unresolved,
        int CooldownBlocked)
    {
        public static MerchantResolutionTriggerSummary Empty { get; } =
            new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private sealed record CardTransactionUpsertSummary(
        int ImportedCount,
        int ProjectedInsertedCount,
        Guid? ProjectedFinancialAccountId,
        IReadOnlyCollection<Guid> ProjectedTransactionIdsForDeterministicReclassification);

    private readonly record struct IngestionDeterministicKickoffOutcome(
        int InsertedRows,
        int MarkedRows,
        bool QueueAccepted);

    private sealed record AccountTransactionFetchResult(
        IReadOnlyList<TrueLayerTransactionRecord> Transactions,
        int SettledFetched,
        int PendingFetched,
        string PendingOutcome,
        int RequestWindowCount,
        int PotentiallyCappedWindowCount,
        int RepeatedWindowPayloadCount,
        DateTime? EarliestReturnedUtc,
        DateTime? LatestReturnedUtc);

    private sealed record AccountTransactionWindowFetchResult(
        IReadOnlyList<TrueLayerTransactionRecord> Transactions,
        int SettledFetched,
        int PendingFetched,
        int PendingSucceededWindowCount,
        int PendingUnsupportedWindowCount,
        int PendingFailedWindowCount,
        int RequestWindowCount,
        int PotentiallyCappedWindowCount,
        string SettledFingerprint);

    private async Task<int> UpsertDirectDebitsAsync(
        LinkedBankAccount linkedAccount,
        IReadOnlyList<TrueLayerDirectDebitRecord> providerDirectDebits,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.BankDirectDebits
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id)
            .ToDictionaryAsync(x => x.ProviderDirectDebitId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var debit in providerDirectDebits)
        {
            if (!seenIds.Add(debit.DirectDebitId))
            {
                continue;
            }

            if (!existing.TryGetValue(debit.DirectDebitId, out var entity))
            {
                entity = new BankDirectDebit
                {
                    Id = Guid.NewGuid(),
                    LinkedBankAccountId = linkedAccount.Id,
                    ProviderDirectDebitId = debit.DirectDebitId,
                    CreatedUtc = now
                };
                dbContext.BankDirectDebits.Add(entity);
                existing[debit.DirectDebitId] = entity;
            }

            entity.Status = debit.Status;
            entity.MandateType = debit.MandateType;
            entity.Reference = debit.Reference;
            entity.MerchantName = debit.MerchantName;
            entity.PreviousPaymentDateUtc = debit.PreviousPaymentDateUtc;
            entity.PreviousPaymentAmount = debit.PreviousPaymentAmount;
            entity.PreviousPaymentCurrency = debit.PreviousPaymentCurrency;
            entity.NextPaymentDateUtc = debit.NextPaymentDateUtc;
            entity.NextPaymentAmount = debit.NextPaymentAmount;
            entity.NextPaymentCurrency = debit.NextPaymentCurrency;
            entity.RawPayloadJson = debit.RawPayloadJson;
            entity.UpdatedUtc = now;
        }

        var removedCount = 0;
        foreach (var kvp in existing)
        {
            if (seenIds.Contains(kvp.Key))
            {
                continue;
            }

            dbContext.BankDirectDebits.Remove(kvp.Value);
            removedCount++;
        }

        return Math.Max(0, providerDirectDebits.Count - removedCount);
    }

    private async Task<int> UpsertStandingOrdersAsync(
        LinkedBankAccount linkedAccount,
        IReadOnlyList<TrueLayerStandingOrderRecord> providerStandingOrders,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.BankStandingOrders
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id)
            .ToDictionaryAsync(x => x.ProviderStandingOrderId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var standingOrder in providerStandingOrders)
        {
            if (!seenIds.Add(standingOrder.StandingOrderId))
            {
                continue;
            }

            if (!existing.TryGetValue(standingOrder.StandingOrderId, out var entity))
            {
                entity = new BankStandingOrder
                {
                    Id = Guid.NewGuid(),
                    LinkedBankAccountId = linkedAccount.Id,
                    ProviderStandingOrderId = standingOrder.StandingOrderId,
                    CreatedUtc = now
                };
                dbContext.BankStandingOrders.Add(entity);
                existing[standingOrder.StandingOrderId] = entity;
            }

            entity.Status = standingOrder.Status;
            entity.Frequency = standingOrder.Frequency;
            entity.Reference = standingOrder.Reference;
            entity.PayeeName = standingOrder.PayeeName;
            entity.FirstPaymentDateUtc = standingOrder.FirstPaymentDateUtc;
            entity.NextPaymentDateUtc = standingOrder.NextPaymentDateUtc;
            entity.FinalPaymentDateUtc = standingOrder.FinalPaymentDateUtc;
            entity.NextPaymentAmount = standingOrder.NextPaymentAmount;
            entity.NextPaymentCurrency = standingOrder.NextPaymentCurrency;
            entity.PayeeAccountMetadataJson = standingOrder.PayeeAccountMetadataJson;
            entity.RawPayloadJson = standingOrder.RawPayloadJson;
            entity.UpdatedUtc = now;
        }

        var removedCount = 0;
        foreach (var kvp in existing)
        {
            if (seenIds.Contains(kvp.Key))
            {
                continue;
            }

            dbContext.BankStandingOrders.Remove(kvp.Value);
            removedCount++;
        }

        return Math.Max(0, providerStandingOrders.Count - removedCount);
    }

    private async Task<DeterministicEnrichmentPassSummary> RunDeterministicEnrichmentPassAsync(
        OpenBankingConnection connection,
        DateTime now,
        bool isInitialBackfill,
        bool includeHistorical,
        CancellationToken cancellationToken)
    {
        EnsureDeterministicEnrichmentState(connection);
        var requiresHistoricalReclassification = connection.NeedsHistoricalReclassification
            || (connection.HistoricalEnrichmentVersion ?? 0) < DeterministicEnrichmentCurrentVersion;
        var shouldMarkTransactionsForReclassification = requiresHistoricalReclassification
            && !connection.HistoricalEnrichmentStartedUtc.HasValue;
        if (shouldMarkTransactionsForReclassification)
        {
            await MarkTransactionsForDeterministicReclassificationAsync(
                connection.UserId,
                cancellationToken);
        }

        var modeParts = new List<string>();
        var linkedTransfersMatched = 0;
        var relationshipRowsUpserted = 0;
        var rowsEvaluated = 0;
        var rowsRemaining = 0;
        var batchesProcessed = 0;
        var hasChanges = false;
        var remainingWorkSnapshot = DeterministicRemainingWorkSnapshot.Empty;

        var incrementalWindowStartUtc = now.AddDays(-DeterministicEnrichmentIncrementalLookbackDays);
        var hasStaleIncrementalRows = await HasStaleDeterministicRowsInWindowAsync(
            connection.UserId,
            incrementalWindowStartUtc,
            now,
            cancellationToken);
        var hasTransferLinkIntegrityDrift = await HasTransferLinkIntegrityDriftInWindowAsync(
            connection.UserId,
            incrementalWindowStartUtc,
            now,
            cancellationToken);

        if (hasStaleIncrementalRows || hasTransferLinkIntegrityDrift)
        {
            var incrementalContextStartUtc = incrementalWindowStartUtc.AddHours(-InternalTransferMatchMaxWindowHours);
            var incrementalContextEndUtc = now.AddHours(InternalTransferMatchMaxWindowHours);

            var incrementalMatched = await MatchLinkedInternalTransfersAsync(
                connection.UserId,
                now,
                incrementalContextStartUtc,
                incrementalContextEndUtc,
                cancellationToken);
            var incrementalRelationships = await ApplyTransactionRelationshipLayerAsync(
                connection.UserId,
                now,
                incrementalContextStartUtc,
                incrementalContextEndUtc,
                cancellationToken);
            var incrementalCategorization = await deterministicCategorizationService.CategorizeWindowAsync(
                connection.UserId,
                incrementalWindowStartUtc,
                now,
                incrementalContextStartUtc,
                incrementalContextEndUtc,
                now,
                cancellationToken);
            if (hasTransferLinkIntegrityDrift && incrementalMatched > 0)
            {
                deterministicMetrics.FalsePositiveCorrectionTotal.Add(incrementalMatched);
            }

            linkedTransfersMatched += incrementalMatched;
            relationshipRowsUpserted += incrementalRelationships + incrementalCategorization.RelationshipRowsUpserted;
            rowsEvaluated += incrementalCategorization.RowsEvaluated;
            batchesProcessed++;
            modeParts.Add("incremental_recent");
            hasChanges = hasChanges
                || incrementalMatched > 0
                || incrementalRelationships > 0
                || incrementalCategorization.HasChanges;

            logger.LogInformation(
                "Deterministic enrichment batch completed connectionId={ConnectionId} userId={UserId} mode={Mode} windowStartUtc={WindowStartUtc} windowEndUtc={WindowEndUtc} contextStartUtc={ContextStartUtc} contextEndUtc={ContextEndUtc} linkedTransfersMatched={LinkedTransfersMatched} relationshipRowsUpserted={RelationshipRowsUpserted} rowsEvaluated={RowsEvaluated} rowsTerminal={RowsTerminal} rowsDeferredCounterparty={RowsDeferredCounterparty} rowsRejectedAmbiguous={RowsRejectedAmbiguous} hasStaleRows={HasStaleRows} hasTransferLinkIntegrityDrift={HasTransferLinkIntegrityDrift}",
                connection.Id,
                connection.UserId,
                "incremental_recent",
                incrementalWindowStartUtc,
                now,
                incrementalContextStartUtc,
                incrementalContextEndUtc,
                incrementalMatched,
                incrementalRelationships + incrementalCategorization.RelationshipRowsUpserted,
                incrementalCategorization.RowsEvaluated,
                incrementalCategorization.RowsTerminal,
                incrementalCategorization.RowsDeferredCounterparty,
                incrementalCategorization.RowsRejectedAmbiguous,
                hasStaleIncrementalRows,
                hasTransferLinkIntegrityDrift);
        }

        remainingWorkSnapshot = await GetDeterministicRemainingWorkSnapshotAsync(connection.UserId, cancellationToken);
        var rowTruthRowsRemaining = remainingWorkSnapshot.RowsRemaining;
        var rowTruthActionableRowsRemaining = remainingWorkSnapshot.RowsActionableRemaining;
        var hasRowTruthHistoricalDebt = rowTruthRowsRemaining > 0;

        if (hasRowTruthHistoricalDebt && !connection.NeedsHistoricalReclassification)
        {
            logger.LogInformation(
                "Deterministic enrichment auto-reopened historical run from row-truth debt connectionId={ConnectionId} userId={UserId} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining} rowsVersionBehind={RowsVersionBehind} rowsMarkedForReclassification={RowsMarkedForReclassification} rowsSuperseded={RowsSuperseded} rowsNotEvaluated={RowsNotEvaluated} rowsEvaluating={RowsEvaluating}",
                connection.Id,
                connection.UserId,
                rowTruthRowsRemaining,
                rowTruthActionableRowsRemaining,
                remainingWorkSnapshot.RowsVersionBehind,
                remainingWorkSnapshot.RowsMarkedForReclassification,
                remainingWorkSnapshot.RowsSupersededRecomputeRequired,
                remainingWorkSnapshot.RowsNotEvaluated,
                remainingWorkSnapshot.RowsEvaluating);

            connection.NeedsHistoricalReclassification = true;
            connection.HistoricalEnrichmentStartedUtc = null;
            connection.HistoricalEnrichmentCompletedUtc = null;
            connection.HistoricalEnrichmentCheckpointUtc = null;
        }

        var hasDeterministicScopeRows = await HasAnyLinkedDeterministicScopeRowsAsync(connection.UserId, cancellationToken);
        var historicalEligible = connection.InitialBackfillCompletedUtc.HasValue || isInitialBackfill || hasDeterministicScopeRows;
        var historicalRequired = historicalEligible
            && (connection.NeedsHistoricalReclassification
                || !connection.HistoricalEnrichmentCompletedUtc.HasValue
                || (connection.HistoricalEnrichmentVersion ?? 0) < DeterministicEnrichmentCurrentVersion
                || hasRowTruthHistoricalDebt);

        var historicalInProgress = false;
        var historicalCompleted = connection.HistoricalEnrichmentCompletedUtc.HasValue
            && (connection.HistoricalEnrichmentVersion ?? 0) >= DeterministicEnrichmentCurrentVersion
            && !connection.NeedsHistoricalReclassification;

        if (historicalRequired && !includeHistorical)
        {
            connection.HistoricalEnrichmentStartedUtc ??= now;
            connection.HistoricalEnrichmentCompletedUtc = null;
            connection.NeedsHistoricalReclassification = true;
            remainingWorkSnapshot = await GetDeterministicRemainingWorkSnapshotAsync(connection.UserId, cancellationToken);
            rowsRemaining = remainingWorkSnapshot.RowsRemaining;
            historicalInProgress = remainingWorkSnapshot.RowsActionableRemaining > 0;
            historicalCompleted = rowsRemaining == 0;
            modeParts.Add("historical_deferred");
        }
        else if (historicalRequired)
        {
            connection.HistoricalEnrichmentStartedUtc ??= now;
            connection.HistoricalEnrichmentCompletedUtc = null;
            connection.NeedsHistoricalReclassification = true;

            var historicalBatch = await GetStaleDeterministicBatchAsync(
                connection.UserId,
                DeterministicEnrichmentHistoricalBatchSize,
                cancellationToken);

            if (historicalBatch.Count == 0)
            {
                connection.HistoricalEnrichmentCheckpointUtc = connection.EarliestImportedTransactionUtc ?? incrementalWindowStartUtc;
                connection.HistoricalEnrichmentCompletedUtc = now;
                connection.HistoricalEnrichmentVersion = DeterministicEnrichmentCurrentVersion;
                connection.NeedsHistoricalReclassification = false;
                historicalInProgress = false;
                historicalCompleted = true;
                rowsRemaining = 0;
                modeParts.Add("historical_caught_up");
            }
            else
            {
                var historicalWindowStartUtc = historicalBatch.Min(x => x.BookedAtUtc);
                var historicalWindowEndUtc = historicalBatch.Max(x => x.BookedAtUtc);
                var historicalContextStartUtc = historicalWindowStartUtc.AddDays(-DeterministicEnrichmentHistoricalContextPaddingDays);
                var historicalContextEndUtc = historicalWindowEndUtc.AddDays(DeterministicEnrichmentHistoricalContextPaddingDays);

                var historicalMatched = await MatchLinkedInternalTransfersAsync(
                    connection.UserId,
                    now,
                    historicalContextStartUtc,
                    historicalContextEndUtc,
                    cancellationToken);
                var historicalRelationships = await ApplyTransactionRelationshipLayerAsync(
                    connection.UserId,
                    now,
                    historicalContextStartUtc,
                    historicalContextEndUtc,
                    cancellationToken);
                var historicalCategorization = await deterministicCategorizationService.CategorizeTransactionsAsync(
                    connection.UserId,
                    historicalBatch.Select(x => x.TransactionId).ToArray(),
                    historicalContextStartUtc,
                    historicalContextEndUtc,
                    now,
                    cancellationToken);

                linkedTransfersMatched += historicalMatched;
                relationshipRowsUpserted += historicalRelationships + historicalCategorization.RelationshipRowsUpserted;
                rowsEvaluated += historicalCategorization.RowsEvaluated;
                batchesProcessed++;
                modeParts.Add("historical_backfill_batch");
                hasChanges = hasChanges
                    || historicalMatched > 0
                    || historicalRelationships > 0
                    || historicalCategorization.HasChanges;

                remainingWorkSnapshot = await GetDeterministicRemainingWorkSnapshotAsync(connection.UserId, cancellationToken);
                rowsRemaining = remainingWorkSnapshot.RowsRemaining;
                connection.HistoricalEnrichmentCheckpointUtc = rowsRemaining == 0
                    ? connection.EarliestImportedTransactionUtc ?? historicalWindowStartUtc
                    : await GetOldestStaleDeterministicBookedAtUtcAsync(connection.UserId, cancellationToken);

                historicalInProgress = remainingWorkSnapshot.RowsActionableRemaining > 0;
                historicalCompleted = rowsRemaining == 0;

                if (historicalCompleted)
                {
                    connection.HistoricalEnrichmentCompletedUtc = now;
                    connection.HistoricalEnrichmentVersion = DeterministicEnrichmentCurrentVersion;
                    connection.NeedsHistoricalReclassification = false;
                }

                logger.LogInformation(
                    "Deterministic enrichment batch completed connectionId={ConnectionId} userId={UserId} mode={Mode} windowStartUtc={WindowStartUtc} windowEndUtc={WindowEndUtc} contextStartUtc={ContextStartUtc} contextEndUtc={ContextEndUtc} linkedTransfersMatched={LinkedTransfersMatched} relationshipRowsUpserted={RelationshipRowsUpserted} rowsEvaluated={RowsEvaluated} rowsTerminal={RowsTerminal} rowsNoMatch={RowsNoMatch} rowsDeferredCounterparty={RowsDeferredCounterparty} rowsRejectedAmbiguous={RowsRejectedAmbiguous} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining}",
                    connection.Id,
                    connection.UserId,
                    "historical_backfill_batch",
                    historicalWindowStartUtc,
                    historicalWindowEndUtc,
                    historicalContextStartUtc,
                    historicalContextEndUtc,
                    historicalMatched,
                    historicalRelationships + historicalCategorization.RelationshipRowsUpserted,
                    historicalCategorization.RowsEvaluated,
                    historicalCategorization.RowsTerminal,
                    historicalCategorization.RowsNoMatch,
                    historicalCategorization.RowsDeferredCounterparty,
                    historicalCategorization.RowsRejectedAmbiguous,
                    rowsRemaining,
                    remainingWorkSnapshot.RowsActionableRemaining);
            }
        }

        remainingWorkSnapshot = await GetDeterministicRemainingWorkSnapshotAsync(connection.UserId, cancellationToken);
        rowsRemaining = remainingWorkSnapshot.RowsRemaining;
        var rowsActionableRemaining = remainingWorkSnapshot.RowsActionableRemaining;
        var hasVersionOrReclassificationDebt = remainingWorkSnapshot.RowsVersionBehind > 0
            || remainingWorkSnapshot.RowsMarkedForReclassification > 0
            || remainingWorkSnapshot.RowsSupersededRecomputeRequired > 0
            || remainingWorkSnapshot.RowsNotEvaluated > 0
            || remainingWorkSnapshot.RowsEvaluating > 0;

        if (historicalEligible)
        {
            if (rowsRemaining == 0)
            {
                connection.HistoricalEnrichmentCompletedUtc ??= now;
                connection.HistoricalEnrichmentVersion = DeterministicEnrichmentCurrentVersion;
                connection.NeedsHistoricalReclassification = false;
                connection.HistoricalEnrichmentCheckpointUtc ??= connection.EarliestImportedTransactionUtc ?? incrementalWindowStartUtc;
                historicalInProgress = false;
                historicalCompleted = true;
            }
            else
            {
                historicalInProgress = rowsActionableRemaining > 0;
                historicalCompleted = false;

                if (!hasVersionOrReclassificationDebt)
                {
                    connection.NeedsHistoricalReclassification = false;
                    connection.HistoricalEnrichmentVersion = DeterministicEnrichmentCurrentVersion;
                }
            }
        }
        else
        {
            historicalInProgress = rowsActionableRemaining > 0;
            historicalCompleted = rowsRemaining == 0 && !connection.NeedsHistoricalReclassification;
        }

        var progressPercent = await ComputeHistoricalEnrichmentProgressPercentAsync(
            connection.UserId,
            historicalCompleted,
            cancellationToken);

        logger.LogInformation(
            "Deterministic enrichment summary connectionId={ConnectionId} userId={UserId} mode={Mode} batchesProcessed={BatchesProcessed} rowsEvaluated={RowsEvaluated} rowsRemaining={RowsRemaining} rowsActionableRemaining={RowsActionableRemaining} rowsDeferredRemaining={RowsDeferredRemaining} deferredCounterparty={DeferredCounterparty} deferredMoreContext={DeferredMoreContext} deferredLegitimateWaiting={DeferredLegitimateWaiting} deferredReadyForTerminalization={DeferredReadyForTerminalization} rowsRejectedAmbiguous={RowsRejectedAmbiguous} rowsEvaluatedNoMatch={RowsEvaluatedNoMatch} notEvaluated={NotEvaluated} evaluating={Evaluating} versionBehind={VersionBehind} markedForReclassification={MarkedForReclassification} superseded={Superseded} fullCounterpartyUniversePresent={FullCounterpartyUniversePresent} deferredReasonBreakdown={DeferredReasonBreakdown} deferredFamilyBreakdown={DeferredFamilyBreakdown} historicalInProgress={HistoricalInProgress} historicalCompleted={HistoricalCompleted} checkpointUtc={CheckpointUtc} progressPercent={ProgressPercent}",
            connection.Id,
            connection.UserId,
            modeParts.Count == 0 ? "none" : string.Join("+", modeParts),
            batchesProcessed,
            rowsEvaluated,
            rowsRemaining,
            rowsActionableRemaining,
            remainingWorkSnapshot.RowsDeferredRemaining,
            remainingWorkSnapshot.RowsDeferredWaitingForCounterparty,
            remainingWorkSnapshot.RowsDeferredWaitingForMoreContext,
            remainingWorkSnapshot.RowsDeferredLegitimateWaiting,
            remainingWorkSnapshot.RowsDeferredReadyForTerminalization,
            remainingWorkSnapshot.RowsRejectedAmbiguous,
            remainingWorkSnapshot.RowsEvaluatedNoMatchingRule,
            remainingWorkSnapshot.RowsNotEvaluated,
            remainingWorkSnapshot.RowsEvaluating,
            remainingWorkSnapshot.RowsVersionBehind,
            remainingWorkSnapshot.RowsMarkedForReclassification,
            remainingWorkSnapshot.RowsSupersededRecomputeRequired,
            remainingWorkSnapshot.FullSameUserCounterpartyUniversePresent,
            remainingWorkSnapshot.DeferredReasonBreakdown,
            remainingWorkSnapshot.DeferredFamilyBreakdown,
            historicalInProgress,
            historicalCompleted,
            connection.HistoricalEnrichmentCheckpointUtc,
            progressPercent);

        return new DeterministicEnrichmentPassSummary(
            LinkedTransfersMatched: linkedTransfersMatched,
            RelationshipRowsUpserted: relationshipRowsUpserted,
            RowsEvaluated: rowsEvaluated,
            RowsRemaining: rowsRemaining,
            BatchesProcessed: batchesProcessed,
            Mode: modeParts.Count == 0 ? "none" : string.Join("+", modeParts),
            HistoricalEnrichmentInProgress: historicalInProgress,
            HistoricalEnrichmentCompleted: historicalCompleted,
            HistoricalEnrichmentProgressPercent: progressPercent,
            HistoricalEnrichmentCheckpointUtc: connection.HistoricalEnrichmentCheckpointUtc,
            RemainingWorkSnapshot: remainingWorkSnapshot,
            HasChanges: hasChanges);
    }

    private static void EnsureDeterministicEnrichmentState(OpenBankingConnection connection)
    {
        if ((connection.HistoricalEnrichmentVersion ?? 0) >= DeterministicEnrichmentCurrentVersion
            && !connection.NeedsHistoricalReclassification)
        {
            return;
        }

        if (connection.HistoricalEnrichmentVersion.HasValue
            && connection.HistoricalEnrichmentVersion.Value < DeterministicEnrichmentCurrentVersion)
        {
            connection.HistoricalEnrichmentStartedUtc = null;
            connection.HistoricalEnrichmentCompletedUtc = null;
            connection.HistoricalEnrichmentCheckpointUtc = null;
        }

        connection.NeedsHistoricalReclassification = true;
    }

    private async Task<IngestionDeterministicKickoffOutcome> TriggerDeterministicKickoffFromIngestionAsync(
        OpenBankingConnection connection,
        int insertedRows,
        IReadOnlyCollection<Guid> projectedTransactionIds,
        IReadOnlyCollection<Guid> affectedFinancialAccountIds,
        string reasonCode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (insertedRows <= 0 || projectedTransactionIds.Count == 0)
        {
            return new IngestionDeterministicKickoffOutcome(0, 0, false);
        }

        var markedRows = await MarkSpecificTransactionsForDeterministicReclassificationAsync(
            projectedTransactionIds,
            now,
            cancellationToken);

        var queueDeferred = connection.Status == BankConnectionStatuses.SyncPending;
        var queueTrigger = await reclassificationTriggerService.TriggerAsync(
            new DeterministicReclassificationTriggerRequest(
                UserId: connection.UserId,
                Source: "ingestion_kickoff",
                ReasonCode: reasonCode,
                SourceConnectionId: connection.Id,
                ConnectionIds: [connection.Id],
                MarkConnectionsForHistoricalReplay: markedRows > 0,
                QueueConnections: !queueDeferred),
            cancellationToken);
        var queueAccepted = !queueDeferred
            && queueTrigger.QueueRequestsAttempted > 0
            && queueTrigger.QueueFailures == 0;
        var affectedAccountIdsText = affectedFinancialAccountIds.Count == 0
            ? "none"
            : string.Join(
                ",",
                affectedFinancialAccountIds
                    .OrderBy(x => x)
                    .Take(12)
                    .Select(x => x.ToString("N")));

        logger.LogInformation(
            "Deterministic kickoff triggered from ingestion connectionId={ConnectionId} userId={UserId} insertedRows={InsertedRows} markedRows={MarkedRows} projectedRowCount={ProjectedRowCount} affectedFinancialAccountCount={AffectedFinancialAccountCount} affectedFinancialAccountIds={AffectedFinancialAccountIds} queueDeferred={QueueDeferred} connectionStatus={ConnectionStatus} queueAccepted={QueueAccepted} reason={Reason}",
            connection.Id,
            connection.UserId,
            insertedRows,
            markedRows,
            projectedTransactionIds.Count,
            affectedFinancialAccountIds.Count,
            affectedAccountIdsText,
            queueDeferred,
            connection.Status,
            queueAccepted,
            reasonCode);

        return new IngestionDeterministicKickoffOutcome(insertedRows, markedRows, queueAccepted);
    }

    private async Task MarkTransactionsForDeterministicReclassificationAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return;
        }

        var rows = await dbContext.Transactions
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && !x.NeedsDeterministicReclassification)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.NeedsDeterministicReclassification = true;
        }

        if (rows.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<int> MarkSpecificTransactionsForDeterministicReclassificationAsync(
        IReadOnlyCollection<Guid> transactionIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return 0;
        }

        var remainingIds = transactionIds.ToHashSet();
        var touched = 0;

        foreach (var trackedEntry in dbContext.ChangeTracker.Entries<Transaction>())
        {
            if (trackedEntry.State == EntityState.Deleted)
            {
                continue;
            }

            var row = trackedEntry.Entity;
            if (!remainingIds.Contains(row.Id))
            {
                continue;
            }

            touched += MarkTransactionForDeterministicReclassification(row, now);
            remainingIds.Remove(row.Id);
        }

        if (remainingIds.Count == 0)
        {
            return touched;
        }

        var persistedRows = await dbContext.Transactions
            .Where(x => remainingIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var row in persistedRows)
        {
            touched += MarkTransactionForDeterministicReclassification(row, now);
        }

        return touched;
    }

    private async Task<int> InvalidateTransferRowsForSameUserUniverseExpansionAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count < 2)
        {
            return 0;
        }

        var candidateRows = await dbContext.Transactions
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && x.DeterministicClassificationTerminal
                && !x.NeedsDeterministicReclassification)
            .ToListAsync(cancellationToken);

        var touched = 0;
        foreach (var row in candidateRows)
        {
            if (!ShouldInvalidateTransferRowForSameUserUniverseExpansion(row))
            {
                continue;
            }

            touched += MarkTransactionForDeterministicReclassification(row, now);
        }

        return touched;
    }

    private static int MarkTransactionForDeterministicReclassification(Transaction row, DateTime now)
    {
        var changed = false;
        if (!row.NeedsDeterministicReclassification)
        {
            row.NeedsDeterministicReclassification = true;
            changed = true;
        }

        var shouldSupersedeExistingResult = row.DeterministicClassificationVersion.HasValue
            || row.DeterministicClassificationTerminal
            || row.DeterministicClassificationStatus != DeterministicClassificationStatus.NotEvaluated;
        if (shouldSupersedeExistingResult)
        {
            if (row.DeterministicClassificationStatus != DeterministicClassificationStatus.SupersededRecomputeRequired)
            {
                row.DeterministicClassificationStatus = DeterministicClassificationStatus.SupersededRecomputeRequired;
                changed = true;
            }

            if (row.DeterministicClassificationTerminal)
            {
                row.DeterministicClassificationTerminal = false;
                changed = true;
            }

            if (!row.DeterministicDeferredRetryEligible)
            {
                row.DeterministicDeferredRetryEligible = true;
                changed = true;
            }
        }

        if (changed)
        {
            row.DeterministicLastRetryConsideredUtc = now;
        }

        return changed ? 1 : 0;
    }

    private static bool ShouldInvalidateTransferRowForSameUserUniverseExpansion(Transaction row)
    {
        if (!IsTransferFamilyRow(row))
        {
            return false;
        }

        if (!IsNoCounterpartTerminalOutcome(row))
        {
            return false;
        }

        return EvidenceShowsIncompleteSameUserUniverse(row.DeterministicReasonDetailJson)
               || IsGenericTransferLikeNoMatchOutcome(row);
    }

    private static bool IsTransferFamilyRow(Transaction row)
    {
        if (!string.IsNullOrWhiteSpace(row.DeterministicClassificationRuleKey)
            && (row.DeterministicClassificationRuleKey.Contains("bank_transfer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    row.DeterministicClassificationRuleKey,
                    "generic.no_matching_supported_family_v3",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var evidenceFamily = TryReadEvidenceFamily(row.DeterministicReasonDetailJson);
        if (string.Equals(evidenceFamily, "bank_account_transfer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return EvidenceShowsTransferLikeSignals(row.DeterministicReasonDetailJson);
    }

    private static bool IsNoCounterpartTerminalOutcome(Transaction row)
    {
        return string.Equals(row.DeterministicReasonCode, DeterministicClassificationReasonCodes.TransferRejectedNoCounterpart, StringComparison.Ordinal)
               || string.Equals(row.DeterministicReasonCode, DeterministicClassificationReasonCodes.TransferDeferredExpiredNoCounterpart, StringComparison.Ordinal)
               || IsGenericTransferLikeNoMatchOutcome(row);
    }

    private static bool IsGenericTransferLikeNoMatchOutcome(Transaction row)
    {
        return string.Equals(row.DeterministicClassificationRuleKey, "generic.no_matching_supported_family_v3", StringComparison.OrdinalIgnoreCase)
               && string.Equals(row.DeterministicReasonCode, DeterministicClassificationReasonCodes.EvaluatedUnsupportedFamily, StringComparison.Ordinal)
               && EvidenceShowsTransferLikeSignals(row.DeterministicReasonDetailJson);
    }

    private static bool EvidenceShowsIncompleteSameUserUniverse(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return EvidenceShowsIncompleteSameUserUniverse(document.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool EvidenceShowsTransferLikeSignals(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return EvidenceShowsTransferLikeSignals(document.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool EvidenceShowsTransferLikeSignals(JsonElement element, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 3)
        {
            return false;
        }

        if ((element.TryGetProperty("transferKeyword", out var transferKeywordElement)
             && transferKeywordElement.ValueKind == JsonValueKind.True)
            || (element.TryGetProperty("providerHint", out var providerHintElement)
                && providerHintElement.ValueKind == JsonValueKind.True)
            || (element.TryGetProperty("transferSignals", out var transferSignalsElement)
                && transferSignalsElement.ValueKind == JsonValueKind.True))
        {
            return true;
        }

        if (element.TryGetProperty("pendingEvidence", out var pendingEvidenceElement)
            && pendingEvidenceElement.ValueKind == JsonValueKind.String)
        {
            var nested = pendingEvidenceElement.GetString();
            if (!string.IsNullOrWhiteSpace(nested))
            {
                try
                {
                    using var nestedDocument = JsonDocument.Parse(nested);
                    if (EvidenceShowsTransferLikeSignals(nestedDocument.RootElement, depth + 1))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed nested evidence payloads.
                }
            }
        }

        return false;
    }

    private static bool EvidenceShowsIncompleteSameUserUniverse(JsonElement element, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 3)
        {
            return false;
        }

        if (element.TryGetProperty("sameUserCandidateUniverseSize", out var universeSizeElement)
            && universeSizeElement.ValueKind == JsonValueKind.Number
            && universeSizeElement.TryGetInt32(out var universeSize)
            && universeSize <= 1)
        {
            return true;
        }

        if (element.TryGetProperty("hasPlausibleSameUserCandidateUniverse", out var plausibleUniverseElement)
            && plausibleUniverseElement.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        if (element.TryGetProperty("fullCounterpartyUniversePresent", out var fullUniverseElement)
            && fullUniverseElement.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        if (element.TryGetProperty("hasCounterpartyAccounts", out var hasCounterpartyAccountsElement)
            && hasCounterpartyAccountsElement.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        if (element.TryGetProperty("pendingEvidence", out var pendingEvidenceElement)
            && pendingEvidenceElement.ValueKind == JsonValueKind.String)
        {
            var nested = pendingEvidenceElement.GetString();
            if (!string.IsNullOrWhiteSpace(nested))
            {
                try
                {
                    using var nestedDocument = JsonDocument.Parse(nested);
                    if (EvidenceShowsIncompleteSameUserUniverse(nestedDocument.RootElement, depth + 1))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed nested evidence payloads.
                }
            }
        }

        return false;
    }

    private async Task<bool> HasStaleDeterministicRowsInWindowAsync(
        Guid userId,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return false;
        }

        return await dbContext.Transactions
            .AsNoTracking()
            .AnyAsync(
                x =>
                    linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                    && x.BookedAtUtc >= windowStartUtc
                    && x.BookedAtUtc <= windowEndUtc
                    && (x.NeedsDeterministicReclassification
                        || !x.DeterministicClassificationVersion.HasValue
                        || x.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion
                        || !x.DeterministicClassificationTerminal
                        || x.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired),
                cancellationToken);
    }

    private async Task<bool> HasTransferLinkIntegrityDriftInWindowAsync(
        Guid userId,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return false;
        }

        var linkedRows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && x.BookedAtUtc >= windowStartUtc
                && x.BookedAtUtc <= windowEndUtc
                && x.TransferKind == TransactionTransferKind.LinkedInternal
                && x.LinkedTransferTransactionId.HasValue)
            .Select(x => new
            {
                x.Id,
                CounterpartId = x.LinkedTransferTransactionId!.Value
            })
            .ToListAsync(cancellationToken);

        if (linkedRows.Count == 0)
        {
            return false;
        }

        var counterpartIds = linkedRows
            .Select(x => x.CounterpartId)
            .Distinct()
            .ToList();

        if (counterpartIds.Count == 0)
        {
            return false;
        }

        var counterpartBackLinks = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => counterpartIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.LinkedTransferTransactionId
            })
            .ToDictionaryAsync(x => x.Id, x => x.LinkedTransferTransactionId, cancellationToken);

        foreach (var row in linkedRows)
        {
            if (!counterpartBackLinks.TryGetValue(row.CounterpartId, out var linkedBackReference)
                || linkedBackReference != row.Id)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct StaleDeterministicTransactionBatchRow(Guid TransactionId, DateTime BookedAtUtc);

    private async Task<List<StaleDeterministicTransactionBatchRow>> GetStaleDeterministicBatchAsync(
        Guid userId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && (x.NeedsDeterministicReclassification
                    || !x.DeterministicClassificationVersion.HasValue
                    || x.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion
                    || !x.DeterministicClassificationTerminal
                    || x.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired))
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Select(x => new StaleDeterministicTransactionBatchRow(x.Id, x.BookedAtUtc))
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<DateTime?> GetOldestStaleDeterministicBookedAtUtcAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return null;
        }

        return await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && (x.NeedsDeterministicReclassification
                    || !x.DeterministicClassificationVersion.HasValue
                    || x.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion
                    || !x.DeterministicClassificationTerminal
                    || x.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired))
            .Select(x => (DateTime?)x.BookedAtUtc)
            .MinAsync(cancellationToken);
    }

    private async Task<DeterministicRemainingWorkSnapshot> GetDeterministicRemainingWorkSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return DeterministicRemainingWorkSnapshot.Empty;
        }

        var rows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId))
            .Select(x => new
            {
                x.BookedAtUtc,
                x.NeedsDeterministicReclassification,
                x.DeterministicClassificationVersion,
                x.DeterministicClassificationTerminal,
                x.DeterministicClassificationStatus,
                x.DeterministicReasonCode,
                x.DeterministicClassificationRuleKey,
                x.DeterministicReasonDetailJson
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return DeterministicRemainingWorkSnapshot.Empty;
        }

        var rowsTerminalCurrentVersion = 0;
        var rowsRemaining = 0;
        var rowsDeferredRemaining = 0;
        var rowsDeferredWaitingForCounterparty = 0;
        var rowsDeferredWaitingForMoreContext = 0;
        var rowsDeferredLegitimateWaiting = 0;
        var rowsDeferredReadyForTerminalization = 0;
        var rowsRejectedAmbiguous = 0;
        var rowsEvaluatedNoMatchingRule = 0;
        var rowsNotEvaluated = 0;
        var rowsEvaluating = 0;
        var rowsVersionBehind = 0;
        var rowsMarkedForReclassification = 0;
        var rowsSupersededRecomputeRequired = 0;
        var rowsActionableRemaining = 0;
        var deferredReasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var deferredFamilyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var fullSameUserCounterpartyUniversePresent = linkedFinancialAccountIds.Count > 1;

        foreach (var row in rows)
        {
            var versionBehind = !row.DeterministicClassificationVersion.HasValue
                || row.DeterministicClassificationVersion.Value < DeterministicEnrichmentCurrentVersion;
            var terminalCurrentVersion = row.DeterministicClassificationVersion.HasValue
                && row.DeterministicClassificationVersion.Value >= DeterministicEnrichmentCurrentVersion
                && row.DeterministicClassificationTerminal;
            var superseded = row.DeterministicClassificationStatus == DeterministicClassificationStatus.SupersededRecomputeRequired;
            var remaining = row.NeedsDeterministicReclassification
                || versionBehind
                || !row.DeterministicClassificationTerminal
                || superseded;

            if (terminalCurrentVersion)
            {
                rowsTerminalCurrentVersion++;
            }

            if (versionBehind)
            {
                rowsVersionBehind++;
            }

            if (row.NeedsDeterministicReclassification)
            {
                rowsMarkedForReclassification++;
            }

            if (superseded)
            {
                rowsSupersededRecomputeRequired++;
            }

            if (row.DeterministicClassificationStatus == DeterministicClassificationStatus.NotEvaluated)
            {
                rowsNotEvaluated++;
            }
            else if (row.DeterministicClassificationStatus == DeterministicClassificationStatus.Evaluating)
            {
                rowsEvaluating++;
            }
            else if (row.DeterministicClassificationStatus == DeterministicClassificationStatus.RejectedAmbiguousMatch)
            {
                rowsRejectedAmbiguous++;
            }
            else if (row.DeterministicClassificationStatus == DeterministicClassificationStatus.EvaluatedNoMatchingRule)
            {
                rowsEvaluatedNoMatchingRule++;
            }

            if (remaining)
            {
                rowsRemaining++;

                var deferredCounterpartyCurrentVersion = row.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForCounterparty
                    && !versionBehind
                    && !row.NeedsDeterministicReclassification;
                var deferredMoreContextCurrentVersion = row.DeterministicClassificationStatus == DeterministicClassificationStatus.DeferredWaitingForMoreContext
                    && !versionBehind
                    && !row.NeedsDeterministicReclassification;

                if (deferredCounterpartyCurrentVersion)
                {
                    rowsDeferredRemaining++;
                    rowsDeferredWaitingForCounterparty++;
                    IncrementCount(
                        deferredReasonCounts,
                        string.IsNullOrWhiteSpace(row.DeterministicReasonCode)
                            ? "unknown_reason"
                            : row.DeterministicReasonCode);
                    IncrementCount(
                        deferredFamilyCounts,
                        ResolveDeferredFamily(row.DeterministicClassificationRuleKey, row.DeterministicReasonDetailJson));
                }

                if (deferredMoreContextCurrentVersion)
                {
                    rowsDeferredRemaining++;
                    rowsDeferredWaitingForMoreContext++;
                    IncrementCount(
                        deferredReasonCounts,
                        string.IsNullOrWhiteSpace(row.DeterministicReasonCode)
                            ? "unknown_reason"
                            : row.DeterministicReasonCode);
                    IncrementCount(
                        deferredFamilyCounts,
                        ResolveDeferredFamily(row.DeterministicClassificationRuleKey, row.DeterministicReasonDetailJson));
                }

                if (deferredCounterpartyCurrentVersion || deferredMoreContextCurrentVersion)
                {
                    var deferredStillLegitimate = IsLegitimateDeferredRow(
                        row.DeterministicClassificationStatus,
                        row.DeterministicReasonCode,
                        row.BookedAtUtc,
                        now,
                        fullSameUserCounterpartyUniversePresent);
                    if (deferredStillLegitimate)
                    {
                        rowsDeferredLegitimateWaiting++;
                    }
                    else
                    {
                        rowsDeferredReadyForTerminalization++;
                        rowsActionableRemaining++;
                    }
                }
                else
                {
                    rowsActionableRemaining++;
                }
            }
        }

        return new DeterministicRemainingWorkSnapshot(
            RowsTotal: rows.Count,
            RowsTerminalCurrentVersion: rowsTerminalCurrentVersion,
            RowsRemaining: rowsRemaining,
            RowsActionableRemaining: rowsActionableRemaining,
            RowsDeferredRemaining: rowsDeferredRemaining,
            RowsDeferredWaitingForCounterparty: rowsDeferredWaitingForCounterparty,
            RowsDeferredWaitingForMoreContext: rowsDeferredWaitingForMoreContext,
            RowsDeferredLegitimateWaiting: rowsDeferredLegitimateWaiting,
            RowsDeferredReadyForTerminalization: rowsDeferredReadyForTerminalization,
            RowsRejectedAmbiguous: rowsRejectedAmbiguous,
            RowsEvaluatedNoMatchingRule: rowsEvaluatedNoMatchingRule,
            RowsNotEvaluated: rowsNotEvaluated,
            RowsEvaluating: rowsEvaluating,
            RowsVersionBehind: rowsVersionBehind,
            RowsMarkedForReclassification: rowsMarkedForReclassification,
            RowsSupersededRecomputeRequired: rowsSupersededRecomputeRequired,
            FullSameUserCounterpartyUniversePresent: fullSameUserCounterpartyUniversePresent,
            DeferredReasonBreakdown: FormatTopCounts(deferredReasonCounts),
            DeferredFamilyBreakdown: FormatTopCounts(deferredFamilyCounts));
    }

    private static bool IsLegitimateDeferredRow(
        DeterministicClassificationStatus status,
        string? reasonCode,
        DateTime bookedAtUtc,
        DateTime nowUtc,
        bool fullSameUserCounterpartyUniversePresent)
    {
        var ageHours = Math.Max(0d, (nowUtc - bookedAtUtc).TotalHours);
        return status switch
        {
            DeterministicClassificationStatus.DeferredWaitingForCounterparty =>
                !fullSameUserCounterpartyUniversePresent
                && ageHours < DeferredCounterpartyExpiryHours
                && string.Equals(
                    reasonCode,
                    DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
                    StringComparison.Ordinal),
            DeterministicClassificationStatus.DeferredWaitingForMoreContext =>
                ageHours < DeferredMoreContextExpiryHours
                && string.Equals(
                    reasonCode,
                    DeterministicClassificationReasonCodes.DeferredPendingBookedContext,
                    StringComparison.Ordinal),
            _ => false
        };
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        if (counts.TryGetValue(key, out var current))
        {
            counts[key] = current + 1;
            return;
        }

        counts[key] = 1;
    }

    private static string ResolveDeferredFamily(string? ruleKey, string? evidenceJson)
    {
        var family = TryReadEvidenceFamily(evidenceJson);
        if (!string.IsNullOrWhiteSpace(family))
        {
            return family!;
        }

        var normalizedRuleKey = ruleKey?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedRuleKey))
        {
            return "unknown_family";
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

        return "unknown_family";
    }

    private static string? TryReadEvidenceFamily(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("family", out var familyElement))
            {
                return null;
            }

            return familyElement.ValueKind == JsonValueKind.String
                ? familyElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatTopCounts(Dictionary<string, int> counts, int maxItems = 5)
    {
        if (counts.Count == 0)
        {
            return "none";
        }

        return string.Join(
            ",",
            counts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(maxItems)
                .Select(x => $"{x.Key}:{x.Value}"));
    }

    private async Task<List<Guid>> LoadLinkedFinancialAccountIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == userId)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> LoadSameUserFinancialAccountIdsWithProjectedRowsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return [];
        }

        var accountIdsWithProjectedRows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => linkedFinancialAccountIds.Contains(x.FinancialAccountId))
            .Select(x => x.FinancialAccountId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return accountIdsWithProjectedRows.ToHashSet();
    }

    private async Task<bool> HasAnyLinkedDeterministicScopeRowsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return false;
        }

        return await dbContext.Transactions
            .AsNoTracking()
            .AnyAsync(x => linkedFinancialAccountIds.Contains(x.FinancialAccountId), cancellationToken);
    }

    private async Task<double?> ComputeHistoricalEnrichmentProgressPercentAsync(
        Guid userId,
        bool completed,
        CancellationToken cancellationToken)
    {
        if (completed)
        {
            return 100d;
        }

        var linkedFinancialAccountIds = await LoadLinkedFinancialAccountIdsAsync(userId, cancellationToken);
        if (linkedFinancialAccountIds.Count == 0)
        {
            return 0d;
        }

        var totalCount = await dbContext.Transactions
            .AsNoTracking()
            .CountAsync(x => linkedFinancialAccountIds.Contains(x.FinancialAccountId), cancellationToken);

        if (totalCount == 0)
        {
            return 0d;
        }

        var currentCount = await dbContext.Transactions
            .AsNoTracking()
            .CountAsync(
                x => linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                     && x.DeterministicClassificationVersion.HasValue
                     && x.DeterministicClassificationVersion.Value >= DeterministicEnrichmentCurrentVersion
                     && x.DeterministicClassificationTerminal,
                cancellationToken);

        var percent = Math.Clamp((currentCount / (double)totalCount) * 100d, 0d, 99.5d);

        return Math.Round(percent, 2, MidpointRounding.AwayFromZero);
    }

    private async Task<int> MatchLinkedInternalTransfersAsync(
        Guid userId,
        DateTime now,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var linkedFinancialAccountIds = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == userId)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedFinancialAccountIds.Count < 2)
        {
            return 0;
        }

        var accountHintRows = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == userId)
            .Select(x => new
            {
                FinancialAccountId = x.FinancialAccountId!.Value,
                x.DisplayName,
                ProviderDisplayName = x.Connection != null ? x.Connection.ProviderDisplayName : null,
                ProviderId = x.Connection != null ? x.Connection.ProviderId : null
            })
            .ToListAsync(cancellationToken);

        var accountProfilesByFinancialAccountId = new Dictionary<Guid, InternalTransferAccountMatchProfile>();
        foreach (var row in accountHintRows)
        {
            var tokens = BuildInternalTransferAccountHintTokens(
                row.DisplayName,
                row.ProviderDisplayName,
                row.ProviderId);
            var policy = ProviderSyncPolicyCatalog.ResolveForConnection(row.ProviderId, row.ProviderDisplayName);
            if (!accountProfilesByFinancialAccountId.TryGetValue(row.FinancialAccountId, out var current))
            {
                current = new InternalTransferAccountMatchProfile(
                    new HashSet<string>(StringComparer.Ordinal),
                    policy.TimestampPrecision,
                    policy.ProviderKey,
                    policy.ProviderFamily);
                accountProfilesByFinancialAccountId[row.FinancialAccountId] = current;
            }

            if (tokens.Count > 0)
            {
                current.HintTokens.UnionWith(tokens);
            }

            if (policy.TimestampPrecision == ProviderTimestampPrecisionMode.DateOnlyMidnight)
            {
                current.TimestampPrecision = ProviderTimestampPrecisionMode.DateOnlyMidnight;
            }
        }

        var candidates = await dbContext.Transactions
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && x.BookedAtUtc >= windowStartUtc
                && x.BookedAtUtc <= windowEndUtc
                && x.Amount != 0m)
            .ToListAsync(cancellationToken);

        if (candidates.Count < 2)
        {
            return 0;
        }

        var linkedCounterpartIds = candidates
            .Where(x => x.LinkedTransferTransactionId.HasValue)
            .Select(x => x.LinkedTransferTransactionId!.Value)
            .Distinct()
            .ToList();

        var linkedCounterparts = linkedCounterpartIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await dbContext.Transactions
                .AsNoTracking()
                .Where(x => linkedCounterpartIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.LinkedTransferTransactionId
                })
                .ToDictionaryAsync(x => x.Id, x => x.LinkedTransferTransactionId, cancellationToken);

        foreach (var candidate in candidates)
        {
            if (!candidate.LinkedTransferTransactionId.HasValue)
            {
                continue;
            }

            if (!linkedCounterparts.TryGetValue(candidate.LinkedTransferTransactionId.Value, out var linkedBackReference)
                || linkedBackReference != candidate.Id)
            {
                ResetLinkedTransferState(candidate);
            }
        }

        var outgoing = candidates
            .Where(x => x.Amount < 0m && IsAutoMatchCandidateForReconciliation(x))
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();
        var incoming = candidates
            .Where(x => x.Amount > 0m && IsAutoMatchCandidateForReconciliation(x))
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        if (outgoing.Count == 0 || incoming.Count == 0)
        {
            return 0;
        }

        var outgoingByAmountCurrency = outgoing
            .GroupBy(CreateInternalTransferAmountCurrencyKey)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var incomingByAmountCurrency = incoming
            .GroupBy(CreateInternalTransferAmountCurrencyKey)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var matchedPairs = 0;
        var clustersProcessed = 0;
        var clustersRepaired = 0;
        var candidatesEvaluated = 0;
        var candidatesAutoEligible = 0;
        var candidatesRejectedByAmbiguity = 0;
        var candidatesRejectedByMutualBest = 0;
        var noMatchAmountGroups = 0;

        foreach (var amountGroup in outgoingByAmountCurrency)
        {
            if (!incomingByAmountCurrency.TryGetValue(amountGroup.Key, out var incomingGroup)
                || incomingGroup.Count == 0)
            {
                noMatchAmountGroups++;
                continue;
            }

            var groupSummary = ReconcileInternalTransferAmountGroup(
                amountGroup.Key,
                amountGroup.Value,
                incomingGroup,
                accountProfilesByFinancialAccountId,
                now);

            matchedPairs += groupSummary.MatchedPairs;
            clustersProcessed += groupSummary.ClustersProcessed;
            clustersRepaired += groupSummary.ClustersRepaired;
            candidatesEvaluated += groupSummary.CandidatesEvaluated;
            candidatesAutoEligible += groupSummary.CandidatesAutoEligible;
            candidatesRejectedByAmbiguity += groupSummary.CandidatesRejectedByAmbiguity;
            candidatesRejectedByMutualBest += groupSummary.CandidatesRejectedByMutualBest;
        }

        var sameDayRepairs = RepairCrossDayLinkedPairsWithSameDayCandidates(
            candidates,
            accountProfilesByFinancialAccountId,
            now);
        matchedPairs += sameDayRepairs;
        if (sameDayRepairs > 0)
        {
            logger.LogInformation(
                "Linked transfer same-day repair completed userId={UserId} repairsApplied={RepairsApplied}",
                userId,
                sameDayRepairs);
        }

        logger.LogInformation(
            "Linked transfer cluster matching completed userId={UserId} windowStartUtc={WindowStartUtc} windowEndUtc={WindowEndUtc} matchedPairs={MatchedPairs} clustersProcessed={ClustersProcessed} clustersRepaired={ClustersRepaired} candidatesEvaluated={CandidatesEvaluated} candidatesAutoEligible={CandidatesAutoEligible} candidatesRejectedByAmbiguity={CandidatesRejectedByAmbiguity} candidatesRejectedByMutualBest={CandidatesRejectedByMutualBest} noMatchAmountGroups={NoMatchAmountGroups}",
            userId,
            windowStartUtc,
            windowEndUtc,
            matchedPairs,
            clustersProcessed,
            clustersRepaired,
            candidatesEvaluated,
            candidatesAutoEligible,
            candidatesRejectedByAmbiguity,
            candidatesRejectedByMutualBest,
            noMatchAmountGroups);

        return matchedPairs;
    }

    private int RepairCrossDayLinkedPairsWithSameDayCandidates(
        IReadOnlyList<Transaction> candidates,
        IReadOnlyDictionary<Guid, InternalTransferAccountMatchProfile> accountProfilesByFinancialAccountId,
        DateTime now)
    {
        var byId = candidates.ToDictionary(x => x.Id);
        var repairsApplied = 0;

        var incomingLinked = candidates
            .Where(x => x.Amount > 0m && x.LinkedTransferTransactionId.HasValue)
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        foreach (var credit in incomingLinked)
        {
            if (!credit.LinkedTransferTransactionId.HasValue
                || !byId.TryGetValue(credit.LinkedTransferTransactionId.Value, out var currentDebit)
                || currentDebit.Amount >= 0m
                || currentDebit.FinancialAccountId == credit.FinancialAccountId)
            {
                continue;
            }

            var creditWeak = IsWeakTimestampForMatching(credit, accountProfilesByFinancialAccountId);
            var currentDebitWeak = IsWeakTimestampForMatching(currentDebit, accountProfilesByFinancialAccountId);
            var creditMatchDate = GetTransferMatchingDate(credit.BookedAtUtc, creditWeak);
            var currentDebitMatchDate = GetTransferMatchingDate(currentDebit.BookedAtUtc, currentDebitWeak);
            if (creditMatchDate == currentDebitMatchDate)
            {
                continue;
            }

            var amountCurrencyKey = CreateInternalTransferAmountCurrencyKey(credit);
            var sameDayCandidate = candidates
                .Where(x =>
                    x.Amount < 0m
                    && x.Id != currentDebit.Id
                    && !x.LinkedTransferTransactionId.HasValue
                    && x.FinancialAccountId != credit.FinancialAccountId
                    && !LooksLikeSavingsPocketMovementDescription(x.Description)
                    && string.Equals(CreateInternalTransferAmountCurrencyKey(x), amountCurrencyKey, StringComparison.Ordinal))
                .Select(x => new
                {
                    Debit = x,
                    Score = ScoreInternalTransferPair(x, credit, accountProfilesByFinancialAccountId),
                    MatchDate = GetTransferMatchingDate(
                        x.BookedAtUtc,
                        IsWeakTimestampForMatching(x, accountProfilesByFinancialAccountId))
                })
                .Where(x =>
                    x.MatchDate == creditMatchDate
                    && x.Score.Score != int.MinValue
                    && x.Score.HasTransferEvidence
                    && x.Score.Score >= InternalTransferMatchMinimumScore)
                .OrderByDescending(x => x.Score.Score)
                .ThenBy(x => x.Score.Distance)
                .ThenBy(x => x.Debit.BookedAtUtc)
                .FirstOrDefault();

            if (sameDayCandidate is null)
            {
                continue;
            }

            ResetLinkedTransferState(credit);
            if (currentDebit.LinkedTransferTransactionId == credit.Id)
            {
                ResetLinkedTransferState(currentDebit);
            }

            var repairedScore = sameDayCandidate.Score with
            {
                DecisionReason = $"same_day_repair:{sameDayCandidate.Score.DecisionReason}"
            };
            ApplyLinkedInternalTransferPair(sameDayCandidate.Debit, credit, repairedScore, now);
            repairsApplied++;

            logger.LogInformation(
                "Linked transfer same-day repair applied creditId={CreditId} oldDebitId={OldDebitId} newDebitId={NewDebitId} creditDate={CreditDate} oldDebitDate={OldDebitDate} newDebitDate={NewDebitDate} score={Score}",
                credit.Id,
                currentDebit.Id,
                sameDayCandidate.Debit.Id,
                creditMatchDate,
                currentDebitMatchDate,
                sameDayCandidate.MatchDate,
                sameDayCandidate.Score.Score);
        }

        return repairsApplied;
    }

    private InternalTransferAmountGroupSummary ReconcileInternalTransferAmountGroup(
        string amountCurrencyKey,
        IReadOnlyList<Transaction> outgoingGroup,
        IReadOnlyList<Transaction> incomingGroup,
        IReadOnlyDictionary<Guid, InternalTransferAccountMatchProfile> accountProfilesByFinancialAccountId,
        DateTime now)
    {
        var outgoingOrdered = outgoingGroup
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();
        var incomingOrdered = incomingGroup
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        var outgoingOrderIndex = outgoingOrdered
            .Select((transaction, index) => new { transaction.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);
        var incomingOrderIndex = incomingOrdered
            .Select((transaction, index) => new { transaction.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);
        var isRepeatedAmountCluster = outgoingOrdered.Count > 1 && incomingOrdered.Count > 1;
        var weakTimestampByTransactionId = outgoingOrdered
            .Concat(incomingOrdered)
            .ToDictionary(
                x => x.Id,
                x => IsWeakTimestampForMatching(x, accountProfilesByFinancialAccountId));

        var edges = new List<InternalTransferCandidateEdge>();

        foreach (var debit in outgoingOrdered)
        {
            foreach (var credit in incomingOrdered)
            {
                if (debit.FinancialAccountId == credit.FinancialAccountId)
                {
                    continue;
                }

                var scoreResult = ScoreInternalTransferPair(
                    debit,
                    credit,
                    accountProfilesByFinancialAccountId);
                var isExistingReciprocal =
                    debit.LinkedTransferTransactionId == credit.Id
                    && credit.LinkedTransferTransactionId == debit.Id;

                if (scoreResult.Score == int.MinValue && !isExistingReciprocal)
                {
                    continue;
                }

                if (scoreResult.Score == int.MinValue && isExistingReciprocal)
                {
                    scoreResult = scoreResult with
                    {
                        Score = InternalTransferMatchMinimumScore - 1,
                        HasTransferEvidence = true,
                        DecisionReason = "existing_link_context"
                    };
                }

                var debitWeakTimestamp = weakTimestampByTransactionId[debit.Id];
                var creditWeakTimestamp = weakTimestampByTransactionId[credit.Id];
                var debitMatchDate = GetTransferMatchingDate(debit.BookedAtUtc, debitWeakTimestamp);
                var creditMatchDate = GetTransferMatchingDate(credit.BookedAtUtc, creditWeakTimestamp);
                var dayDistance = Math.Abs((debitMatchDate - creditMatchDate).Days);
                var distanceMinutes = (int)Math.Round(scoreResult.Distance.TotalMinutes, MidpointRounding.AwayFromZero);
                var sequenceDistance = Math.Abs(outgoingOrderIndex[debit.Id] - incomingOrderIndex[credit.Id]);
                var sequenceBonus = Math.Max(0, InternalTransferSequenceTieBreakBoost - sequenceDistance);
                var adjustedScore = scoreResult.Score + sequenceBonus;

                edges.Add(new InternalTransferCandidateEdge(
                    amountCurrencyKey,
                    debit,
                    credit,
                    scoreResult,
                    dayDistance,
                    IsSameDay: dayDistance == 0,
                    distanceMinutes,
                    sequenceBonus,
                    adjustedScore,
                    DetermineTransferConfidenceTier(adjustedScore, scoreResult.HasCounterpartyConfidence),
                    AutoEligible: false,
                    Weight: 0,
                    ScoreBreakdown: $"base={scoreResult.Score};sequenceBonus={sequenceBonus}"));
            }
        }

        if (edges.Count == 0)
        {
            return InternalTransferAmountGroupSummary.Empty;
        }

        var sameDayByDebit = edges
            .Where(x => x.IsSameDay && x.BaseScore.HasTransferEvidence)
            .Select(x => x.Debit.Id)
            .ToHashSet();
        var sameDayByCredit = edges
            .Where(x => x.IsSameDay && x.BaseScore.HasTransferEvidence)
            .Select(x => x.Credit.Id)
            .ToHashSet();
        var nearestDistanceByDebit = edges
            .GroupBy(x => x.Debit.Id)
            .ToDictionary(group => group.Key, group => group.Min(x => x.DistanceMinutes));
        var nearestDistanceByCredit = edges
            .GroupBy(x => x.Credit.Id)
            .ToDictionary(group => group.Key, group => group.Min(x => x.DistanceMinutes));

        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            var adjustedScore = edge.AdjustedScore;
            var scoreBreakdown = new List<string>(6)
            {
                $"base={edge.BaseScore.Score}",
                $"sequence={edge.SequenceBonus}"
            };

            if (edge.IsSameDay)
            {
                adjustedScore += 2;
                scoreBreakdown.Add("sameDayBoost=2");
            }
            else
            {
                if (sameDayByDebit.Contains(edge.Debit.Id))
                {
                    adjustedScore -= InternalTransferCrossDayPenaltyWhenSameDayExists;
                    scoreBreakdown.Add($"crossDayPenaltyByDebit={InternalTransferCrossDayPenaltyWhenSameDayExists}");
                }

                if (sameDayByCredit.Contains(edge.Credit.Id))
                {
                    adjustedScore -= InternalTransferCrossDayPenaltyWhenSameDayExists;
                    scoreBreakdown.Add($"crossDayPenaltyByCredit={InternalTransferCrossDayPenaltyWhenSameDayExists}");
                }
            }

            if (nearestDistanceByDebit.TryGetValue(edge.Debit.Id, out var nearestDebitDistance)
                && edge.DistanceMinutes > nearestDebitDistance + 120)
            {
                adjustedScore -= InternalTransferNearestNeighborPenalty;
                scoreBreakdown.Add($"debitNearestPenalty={InternalTransferNearestNeighborPenalty}");
            }

            if (nearestDistanceByCredit.TryGetValue(edge.Credit.Id, out var nearestCreditDistance)
                && edge.DistanceMinutes > nearestCreditDistance + 120)
            {
                adjustedScore -= InternalTransferNearestNeighborPenalty;
                scoreBreakdown.Add($"creditNearestPenalty={InternalTransferNearestNeighborPenalty}");
            }

            if (edge.DayDistance > 0
                && edge.BaseScore.HasWeakTimestampPrecision
                && !edge.BaseScore.HasCounterpartyConfidence)
            {
                adjustedScore -= InternalTransferWeakTimestampCrossDayPenalty;
                scoreBreakdown.Add($"weakTimestampCrossDayPenalty={InternalTransferWeakTimestampCrossDayPenalty}");
            }

            var adjustedTier = DetermineTransferConfidenceTier(adjustedScore, edge.BaseScore.HasCounterpartyConfidence);
            var hasAnySameDayAlternative =
                sameDayByDebit.Contains(edge.Debit.Id)
                || sameDayByCredit.Contains(edge.Credit.Id);
            var disallowCrossDayBecauseSameDayExists =
                !edge.IsSameDay
                && hasAnySameDayAlternative
                && (edge.BaseScore.HasWeakTimestampPrecision || isRepeatedAmountCluster);
            var allowCrossDayAutoLink =
                edge.IsSameDay
                || (
                    !edge.BaseScore.HasWeakTimestampPrecision
                    && !isRepeatedAmountCluster
                    && !hasAnySameDayAlternative
                    && edge.BaseScore.HasCounterpartyConfidence
                    && edge.DistanceMinutes <= 360);

            if (disallowCrossDayBecauseSameDayExists)
            {
                scoreBreakdown.Add("crossDayDisallowedWhenAnySameDayAlternative=true");
                if (isRepeatedAmountCluster)
                {
                    scoreBreakdown.Add("crossDayDisallowedReason=repeated_same_amount_cluster");
                }

                if (edge.BaseScore.HasWeakTimestampPrecision)
                {
                    scoreBreakdown.Add("crossDayDisallowedReason=weak_timestamp");
                }
            }
            else if (!allowCrossDayAutoLink)
            {
                if (!edge.IsSameDay)
                {
                    scoreBreakdown.Add("crossDayDisallowedByStrictAutoLinkPolicy=true");
                }
            }

            var autoEligible = !disallowCrossDayBecauseSameDayExists
                && allowCrossDayAutoLink
                && adjustedScore >= InternalTransferMatchMinimumScore
                && adjustedTier == TransferMatchConfidenceTier.High;
            var weight = autoEligible
                ? (adjustedScore * 100_000) - edge.DistanceMinutes - (edge.DayDistance * 1_000)
                : 0;

            edge = edge with
            {
                AdjustedScore = adjustedScore,
                AdjustedConfidenceTier = adjustedTier,
                AutoEligible = autoEligible,
                Weight = weight,
                ScoreBreakdown = string.Join(";", scoreBreakdown)
            };
            edges[index] = edge;

            logger.LogDebug(
                "Linked transfer candidate amountKey={AmountKey} debitId={DebitId} creditId={CreditId} baseScore={BaseScore} adjustedScore={AdjustedScore} confidenceTier={ConfidenceTier} autoEligible={AutoEligible} dayDistance={DayDistance} distanceMinutes={DistanceMinutes} hasCounterpartyConfidence={HasCounterpartyConfidence} hasTransferEvidence={HasTransferEvidence} reason={Reason} breakdown={Breakdown}",
                amountCurrencyKey,
                edge.Debit.Id,
                edge.Credit.Id,
                edge.BaseScore.Score,
                edge.AdjustedScore,
                edge.AdjustedConfidenceTier,
                edge.AutoEligible,
                edge.DayDistance,
                edge.DistanceMinutes,
                edge.BaseScore.HasCounterpartyConfidence,
                edge.BaseScore.HasTransferEvidence,
                edge.BaseScore.DecisionReason,
                edge.ScoreBreakdown);
        }

        var clusters = BuildInternalTransferClusters(amountCurrencyKey, outgoingOrdered, incomingOrdered, edges);
        var matchedPairs = 0;
        var clustersRepaired = 0;
        var candidatesAutoEligible = edges.Count(x => x.AutoEligible);
        var rejectedByAmbiguity = 0;
        var rejectedByMutualBest = 0;

        foreach (var cluster in clusters)
        {
            var clusterResult = ReconcileInternalTransferCluster(cluster, now);
            matchedPairs += clusterResult.MatchedPairs;
            rejectedByAmbiguity += clusterResult.RejectedByAmbiguity;
            rejectedByMutualBest += clusterResult.RejectedByMutualBest;
            if (clusterResult.Repaired)
            {
                clustersRepaired++;
            }
        }

        return new InternalTransferAmountGroupSummary(
            MatchedPairs: matchedPairs,
            ClustersProcessed: clusters.Count,
            ClustersRepaired: clustersRepaired,
            CandidatesEvaluated: edges.Count,
            CandidatesAutoEligible: candidatesAutoEligible,
            CandidatesRejectedByAmbiguity: rejectedByAmbiguity,
            CandidatesRejectedByMutualBest: rejectedByMutualBest);
    }

    private static List<InternalTransferCandidateCluster> BuildInternalTransferClusters(
        string amountCurrencyKey,
        IReadOnlyList<Transaction> outgoing,
        IReadOnlyList<Transaction> incoming,
        IReadOnlyList<InternalTransferCandidateEdge> edges)
    {
        var unionFind = new DisjointSet<Guid>();
        var nodeIds = new HashSet<Guid>();

        foreach (var edge in edges.Where(x => x.BaseScore.HasTransferEvidence))
        {
            nodeIds.Add(edge.Debit.Id);
            nodeIds.Add(edge.Credit.Id);
            unionFind.Union(edge.Debit.Id, edge.Credit.Id);
        }

        if (nodeIds.Count == 0)
        {
            return [];
        }

        var clusterNodeIds = nodeIds
            .GroupBy(id => unionFind.Find(id))
            .Select(group => group.ToHashSet())
            .ToList();

        var clusters = new List<InternalTransferCandidateCluster>(clusterNodeIds.Count);
        foreach (var ids in clusterNodeIds)
        {
            var clusterOutgoing = outgoing
                .Where(x => ids.Contains(x.Id))
                .OrderBy(x => x.BookedAtUtc)
                .ThenBy(x => x.Id)
                .ToList();
            var clusterIncoming = incoming
                .Where(x => ids.Contains(x.Id))
                .OrderBy(x => x.BookedAtUtc)
                .ThenBy(x => x.Id)
                .ToList();

            if (clusterOutgoing.Count == 0 || clusterIncoming.Count == 0)
            {
                continue;
            }

            var clusterEdges = edges
                .Where(x => ids.Contains(x.Debit.Id) && ids.Contains(x.Credit.Id))
                .ToList();
            if (clusterEdges.Count == 0)
            {
                continue;
            }

            var clusterStartUtc = clusterOutgoing
                .Concat(clusterIncoming)
                .Min(x => x.BookedAtUtc);
            var clusterEndUtc = clusterOutgoing
                .Concat(clusterIncoming)
                .Max(x => x.BookedAtUtc);
            var accountIds = clusterOutgoing
                .Select(x => x.FinancialAccountId)
                .Concat(clusterIncoming.Select(x => x.FinancialAccountId))
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            var accountPairLabel = accountIds.Length == 2
                ? $"{accountIds[0]:N}|{accountIds[1]:N}"
                : $"multi:{string.Join('|', accountIds.Select(x => x.ToString("N")))}";

            clusters.Add(new InternalTransferCandidateCluster(
                Key: $"{amountCurrencyKey}|{accountPairLabel}|{clusterStartUtc:O}|{clusterEndUtc:O}",
                AmountCurrencyKey: amountCurrencyKey,
                StartUtc: clusterStartUtc,
                EndUtc: clusterEndUtc,
                Outgoing: clusterOutgoing,
                Incoming: clusterIncoming,
                Edges: clusterEdges));
        }

        return clusters;
    }

    private ClusterReconcileResult ReconcileInternalTransferCluster(
        InternalTransferCandidateCluster cluster,
        DateTime now)
    {
        var autoEligibleEdges = cluster.Edges
            .Where(x => x.AutoEligible)
            .ToList();

        if (autoEligibleEdges.Count == 0)
        {
            logger.LogDebug(
                "Linked transfer cluster skipped clusterKey={ClusterKey} amountKey={AmountKey} reason=no_auto_eligible_candidates outgoing={OutgoingCount} incoming={IncomingCount}",
                cluster.Key,
                cluster.AmountCurrencyKey,
                cluster.Outgoing.Count,
                cluster.Incoming.Count);
            return ClusterReconcileResult.Empty;
        }

        var byPair = autoEligibleEdges.ToDictionary(
            x => new InternalTransferPairIdentity(x.Debit.Id, x.Credit.Id),
            x => x);
        var debitRanking = BuildCandidateRankingByDebit(autoEligibleEdges);
        var creditRanking = BuildCandidateRankingByCredit(autoEligibleEdges);
        var selected = SelectBestClusterPairs(cluster.Outgoing, cluster.Incoming, byPair);

        var accepted = new List<InternalTransferCandidateEdge>();
        var rejectedByAmbiguity = 0;
        var rejectedByMutualBest = 0;

        foreach (var edge in selected)
        {
            var debitBest = debitRanking[edge.Debit.Id];
            var creditBest = creditRanking[edge.Credit.Id];
            var debitMutualMarginSatisfied = !debitBest.SecondBestScore.HasValue
                || (debitBest.BestScore - debitBest.SecondBestScore.Value) >= InternalTransferMutualBestMinimumMargin;
            var creditMutualMarginSatisfied = !creditBest.SecondBestScore.HasValue
                || (creditBest.BestScore - creditBest.SecondBestScore.Value) >= InternalTransferMutualBestMinimumMargin;
            var isMutualBest =
                debitBest.BestCounterpartId == edge.Credit.Id
                && creditBest.BestCounterpartId == edge.Debit.Id
                && debitMutualMarginSatisfied
                && creditMutualMarginSatisfied;

            if (!isMutualBest)
            {
                rejectedByMutualBest++;
                logger.LogDebug(
                    "Linked transfer cluster candidate rejected clusterKey={ClusterKey} debitId={DebitId} creditId={CreditId} reason=mutual_best_required debitBestCounterpartId={DebitBestCounterpartId} creditBestCounterpartId={CreditBestCounterpartId} debitBestScore={DebitBestScore} debitSecondBestScore={DebitSecondBestScore} creditBestScore={CreditBestScore} creditSecondBestScore={CreditSecondBestScore}",
                    cluster.Key,
                    edge.Debit.Id,
                    edge.Credit.Id,
                    debitBest.BestCounterpartId,
                    creditBest.BestCounterpartId,
                    debitBest.BestScore,
                    debitBest.SecondBestScore,
                    creditBest.BestScore,
                    creditBest.SecondBestScore);
                continue;
            }

            var debitAmbiguous = debitBest.SecondBestScore.HasValue
                && (debitBest.BestScore - debitBest.SecondBestScore.Value) <= InternalTransferAmbiguityMaxScoreGap;
            var creditAmbiguous = creditBest.SecondBestScore.HasValue
                && (creditBest.BestScore - creditBest.SecondBestScore.Value) <= InternalTransferAmbiguityMaxScoreGap;

            if (debitAmbiguous || creditAmbiguous)
            {
                rejectedByAmbiguity++;
                logger.LogDebug(
                    "Linked transfer cluster candidate rejected clusterKey={ClusterKey} debitId={DebitId} creditId={CreditId} reason=ambiguous debitScoreGap={DebitScoreGap} creditScoreGap={CreditScoreGap}",
                    cluster.Key,
                    edge.Debit.Id,
                    edge.Credit.Id,
                    debitBest.SecondBestScore.HasValue ? debitBest.BestScore - debitBest.SecondBestScore.Value : int.MaxValue,
                    creditBest.SecondBestScore.HasValue ? creditBest.BestScore - creditBest.SecondBestScore.Value : int.MaxValue);
                continue;
            }

            accepted.Add(edge);
        }

        if (accepted.Count == 0)
        {
            logger.LogDebug(
                "Linked transfer cluster produced no accepted pairs clusterKey={ClusterKey} selectedByOptimizer={SelectedByOptimizer} rejectedByMutualBest={RejectedByMutualBest} rejectedByAmbiguity={RejectedByAmbiguity}",
                cluster.Key,
                selected.Count,
                rejectedByMutualBest,
                rejectedByAmbiguity);
        }

        foreach (var debit in cluster.Outgoing)
        {
            var candidatesForDebit = autoEligibleEdges
                .Where(x => x.Debit.Id == debit.Id)
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.DistanceMinutes)
                .ToList();
            if (candidatesForDebit.Count == 0 || candidatesForDebit.All(x => !x.IsSameDay))
            {
                continue;
            }

            if (!accepted.Any(x => x.Debit.Id == debit.Id))
            {
                continue;
            }

            var selectedForDebit = accepted.FirstOrDefault(x => x.Debit.Id == debit.Id);
            if (!selectedForDebit.IsSameDay)
            {
                var sameDayBest = candidatesForDebit.First(x => x.IsSameDay);
                logger.LogDebug(
                    "Linked transfer same-day candidate lost clusterKey={ClusterKey} debitId={DebitId} selectedCreditId={SelectedCreditId} selectedScore={SelectedScore} sameDayCreditId={SameDayCreditId} sameDayScore={SameDayScore}",
                    cluster.Key,
                    debit.Id,
                    selectedForDebit.Credit.Id,
                    selectedForDebit.AdjustedScore,
                    sameDayBest.Credit.Id,
                    sameDayBest.AdjustedScore);
            }
        }

        var clusterTransactionIds = cluster.Outgoing
            .Select(x => x.Id)
            .Concat(cluster.Incoming.Select(x => x.Id))
            .ToHashSet();

        var existingPairs = ExtractExistingClusterPairKeys(cluster.Outgoing, cluster.Incoming, clusterTransactionIds);
        var targetPairs = accepted
            .Select(x => BuildLinkedPairKey(x.Debit.Id, x.Credit.Id))
            .ToHashSet(StringComparer.Ordinal);
        var repaired = !existingPairs.SetEquals(targetPairs);

        var newCounterparts = accepted
            .SelectMany(x => new[]
            {
                new { Id = x.Debit.Id, Counterpart = x.Credit.Id },
                new { Id = x.Credit.Id, Counterpart = x.Debit.Id }
            })
            .ToDictionary(x => x.Id, x => x.Counterpart);

        foreach (var transaction in cluster.Outgoing.Concat(cluster.Incoming))
        {
            if (!transaction.LinkedTransferTransactionId.HasValue)
            {
                continue;
            }

            var currentCounterpartId = transaction.LinkedTransferTransactionId.Value;
            if (!clusterTransactionIds.Contains(currentCounterpartId))
            {
                continue;
            }

            if (newCounterparts.TryGetValue(transaction.Id, out var expectedCounterpartId)
                && expectedCounterpartId == currentCounterpartId)
            {
                continue;
            }

            ResetLinkedTransferState(transaction);
        }

        foreach (var edge in accepted)
        {
            var score = edge.BaseScore with
            {
                Score = edge.AdjustedScore,
                ConfidenceTier = edge.AdjustedConfidenceTier,
                DecisionReason = $"cluster_optimal:{edge.ScoreBreakdown}"
            };

            ApplyLinkedInternalTransferPair(edge.Debit, edge.Credit, score, now);

            logger.LogInformation(
                "Linked transfer pair selected clusterKey={ClusterKey} amountKey={AmountKey} debitId={DebitId} creditId={CreditId} adjustedScore={AdjustedScore} confidenceTier={ConfidenceTier} distanceMinutes={DistanceMinutes} dayDistance={DayDistance} reason={Reason}",
                cluster.Key,
                cluster.AmountCurrencyKey,
                edge.Debit.Id,
                edge.Credit.Id,
                edge.AdjustedScore,
                edge.AdjustedConfidenceTier,
                edge.DistanceMinutes,
                edge.DayDistance,
                edge.ScoreBreakdown);
        }

        logger.LogInformation(
            "Linked transfer cluster reconciled clusterKey={ClusterKey} amountKey={AmountKey} startUtc={StartUtc} endUtc={EndUtc} outgoing={OutgoingCount} incoming={IncomingCount} autoEligibleCandidates={AutoEligibleCandidates} acceptedPairs={AcceptedPairs} repaired={Repaired} rejectedByMutualBest={RejectedByMutualBest} rejectedByAmbiguity={RejectedByAmbiguity}",
            cluster.Key,
            cluster.AmountCurrencyKey,
            cluster.StartUtc,
            cluster.EndUtc,
            cluster.Outgoing.Count,
            cluster.Incoming.Count,
            autoEligibleEdges.Count,
            accepted.Count,
            repaired,
            rejectedByMutualBest,
            rejectedByAmbiguity);

        return new ClusterReconcileResult(
            MatchedPairs: accepted.Count,
            Repaired: repaired,
            RejectedByAmbiguity: rejectedByAmbiguity,
            RejectedByMutualBest: rejectedByMutualBest);
    }

    private static Dictionary<Guid, CounterpartRanking> BuildCandidateRankingByDebit(
        IReadOnlyList<InternalTransferCandidateEdge> candidates)
    {
        return candidates
            .GroupBy(x => x.Debit.Id)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group
                        .OrderByDescending(x => x.Weight)
                        .ThenBy(x => x.DistanceMinutes)
                        .ThenBy(x => x.Credit.BookedAtUtc)
                        .ToList();

                    return new CounterpartRanking(
                        BestCounterpartId: ordered[0].Credit.Id,
                        BestScore: ordered[0].AdjustedScore,
                        SecondBestScore: ordered.Count > 1 ? ordered[1].AdjustedScore : null);
                });
    }

    private static Dictionary<Guid, CounterpartRanking> BuildCandidateRankingByCredit(
        IReadOnlyList<InternalTransferCandidateEdge> candidates)
    {
        return candidates
            .GroupBy(x => x.Credit.Id)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group
                        .OrderByDescending(x => x.Weight)
                        .ThenBy(x => x.DistanceMinutes)
                        .ThenBy(x => x.Debit.BookedAtUtc)
                        .ToList();

                    return new CounterpartRanking(
                        BestCounterpartId: ordered[0].Debit.Id,
                        BestScore: ordered[0].AdjustedScore,
                        SecondBestScore: ordered.Count > 1 ? ordered[1].AdjustedScore : null);
                });
    }

    private static List<InternalTransferCandidateEdge> SelectBestClusterPairs(
        IReadOnlyList<Transaction> outgoing,
        IReadOnlyList<Transaction> incoming,
        IReadOnlyDictionary<InternalTransferPairIdentity, InternalTransferCandidateEdge> candidatesByPair)
    {
        var outgoingIndexById = outgoing
            .Select((transaction, index) => new { transaction.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);
        var incomingIndexById = incoming
            .Select((transaction, index) => new { transaction.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);

        var dimension = outgoing.Count + incoming.Count;
        var scores = new int[dimension, dimension];

        for (var row = 0; row < outgoing.Count; row++)
        {
            for (var col = 0; col < incoming.Count; col++)
            {
                var pair = new InternalTransferPairIdentity(outgoing[row].Id, incoming[col].Id);
                scores[row, col] = candidatesByPair.TryGetValue(pair, out var edge)
                    ? edge.Weight
                    : -1_000_000;
            }

            for (var col = incoming.Count; col < dimension; col++)
            {
                scores[row, col] = 0;
            }
        }

        for (var row = outgoing.Count; row < dimension; row++)
        {
            for (var col = 0; col < dimension; col++)
            {
                scores[row, col] = 0;
            }
        }

        var assignment = SolveMaximumWeightAssignment(scores);
        var selected = new List<InternalTransferCandidateEdge>(outgoing.Count);

        for (var row = 0; row < outgoing.Count; row++)
        {
            var column = assignment[row];
            if (column < 0 || column >= incoming.Count)
            {
                continue;
            }

            var pair = new InternalTransferPairIdentity(outgoing[row].Id, incoming[column].Id);
            if (!candidatesByPair.TryGetValue(pair, out var edge))
            {
                continue;
            }

            if (edge.Weight <= 0)
            {
                continue;
            }

            selected.Add(edge);
        }

        return selected;
    }

    private static int[] SolveMaximumWeightAssignment(int[,] weights)
    {
        var n = weights.GetLength(0);
        var m = weights.GetLength(1);
        if (n == 0 || m == 0)
        {
            return [];
        }

        var u = new int[n + 1];
        var v = new int[m + 1];
        var p = new int[m + 1];
        var way = new int[m + 1];

        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new int[m + 1];
            var used = new bool[m + 1];
            for (var j = 0; j <= m; j++)
            {
                minv[j] = int.MaxValue / 4;
            }

            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = int.MaxValue / 4;
                var j1 = 0;
                for (var j = 1; j <= m; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    var current = -weights[i0 - 1, j - 1] - u[i0] - v[j];
                    if (current < minv[j])
                    {
                        minv[j] = current;
                        way[j] = j0;
                    }

                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }

                for (var j = 0; j <= m; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            } while (p[j0] != 0);

            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        var assignment = Enumerable.Repeat(-1, n).ToArray();
        for (var j = 1; j <= m; j++)
        {
            if (p[j] != 0)
            {
                assignment[p[j] - 1] = j - 1;
            }
        }

        return assignment;
    }

    private static HashSet<string> ExtractExistingClusterPairKeys(
        IReadOnlyList<Transaction> outgoing,
        IReadOnlyList<Transaction> incoming,
        IReadOnlySet<Guid> clusterTransactionIds)
    {
        var outgoingById = outgoing.ToDictionary(x => x.Id);
        var incomingById = incoming.ToDictionary(x => x.Id);
        var existingPairs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var debit in outgoing)
        {
            if (!debit.LinkedTransferTransactionId.HasValue)
            {
                continue;
            }

            var creditId = debit.LinkedTransferTransactionId.Value;
            if (!clusterTransactionIds.Contains(creditId)
                || !incomingById.TryGetValue(creditId, out var credit)
                || credit.LinkedTransferTransactionId != debit.Id)
            {
                continue;
            }

            existingPairs.Add(BuildLinkedPairKey(debit.Id, creditId));
        }

        foreach (var credit in incoming)
        {
            if (!credit.LinkedTransferTransactionId.HasValue)
            {
                continue;
            }

            var debitId = credit.LinkedTransferTransactionId.Value;
            if (!clusterTransactionIds.Contains(debitId)
                || !outgoingById.TryGetValue(debitId, out var debit)
                || debit.LinkedTransferTransactionId != credit.Id)
            {
                continue;
            }

            existingPairs.Add(BuildLinkedPairKey(debitId, credit.Id));
        }

        return existingPairs;
    }

    private static string BuildLinkedPairKey(Guid debitId, Guid creditId)
        => $"{debitId:N}|{creditId:N}";

    private static bool IsAutoMatchCandidateForReconciliation(Transaction transaction)
    {
        if (IsAutoMatchEligible(transaction))
        {
            return true;
        }

        // Allow conservative rematching for transfer-looking rows that were already
        // metadata-enriched in earlier runs but are currently unlinked. Without this,
        // stale wrong links in repeated same-amount clusters can remain unrepaired.
        if (!transaction.LinkedTransferTransactionId.HasValue
            && transaction.Amount != 0m
            && LooksLikeInternalTransferDescription(transaction.Description)
            && !LooksLikeSavingsPocketMovementDescription(transaction.Description))
        {
            return true;
        }

        return transaction.TransferKind == TransactionTransferKind.LinkedInternal
            && transaction.LinkedTransferTransactionId.HasValue;
    }

    private async Task<int> ApplyTransactionRelationshipLayerAsync(
        Guid userId,
        DateTime now,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var linkedTransferRelationships = await UpsertLinkedInternalTransferRelationshipsAsync(
            userId,
            now,
            windowStartUtc,
            windowEndUtc,
            cancellationToken);
        var savingsMovementRelationships = await UpsertSavingsMovementRelationshipsAsync(
            userId,
            now,
            windowStartUtc,
            windowEndUtc,
            cancellationToken);
        var total = linkedTransferRelationships + savingsMovementRelationships;

        if (total > 0)
        {
            logger.LogInformation(
                "Transaction relationship layer updated userId={UserId} linkedTransferRelationships={LinkedTransferRelationships} savingsMovementRelationships={SavingsMovementRelationships} totalRelationshipsTouched={TotalRelationshipsTouched}",
                userId,
                linkedTransferRelationships,
                savingsMovementRelationships,
                total);
        }

        return total;
    }

    private async Task<int> UpsertLinkedInternalTransferRelationshipsAsync(
        Guid userId,
        DateTime now,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var linkedRows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccount != null
                && x.FinancialAccount.UserId == userId
                && x.TransferKind == TransactionTransferKind.LinkedInternal
                && x.LinkedTransferTransactionId.HasValue
                && x.Amount < 0m
                && x.BookedAtUtc >= windowStartUtc
                && x.BookedAtUtc <= windowEndUtc)
            .Select(x => new
            {
                x.Id,
                CounterpartId = x.LinkedTransferTransactionId!.Value,
                x.FinancialAccountId,
                x.TransferMatchConfidenceScore,
                x.TransferMatchConfidenceTier,
                x.TransferMatchReason
            })
            .ToListAsync(cancellationToken);

        if (linkedRows.Count == 0)
        {
            return 0;
        }

        var counterpartIds = linkedRows
            .Select(x => x.CounterpartId)
            .Distinct()
            .ToArray();

        var counterpartRows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => counterpartIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.LinkedTransferTransactionId,
                x.FinancialAccountId,
                x.TransferMatchConfidenceScore,
                x.TransferMatchConfidenceTier,
                x.TransferMatchReason
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var allTransactionIds = linkedRows
            .Select(x => x.Id)
            .Concat(linkedRows.Select(x => x.CounterpartId))
            .Distinct()
            .ToArray();

        var normalizedRefsByTransactionId = await LoadProjectedRawReferencesAsync(allTransactionIds, cancellationToken);

        var relationshipKeys = linkedRows
            .Select(x => BuildRelationshipKey(TransactionRelationshipType.InternalAccountTransfer, x.Id, x.CounterpartId))
            .Distinct()
            .ToArray();

        var existingByKey = await dbContext.TransactionRelationships
            .Where(x => relationshipKeys.Contains(x.RelationshipKey))
            .ToDictionaryAsync(x => x.RelationshipKey, StringComparer.Ordinal, cancellationToken);
        var relationshipKeySet = relationshipKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var trackedRelationship in dbContext.TransactionRelationships.Local)
        {
            if (dbContext.Entry(trackedRelationship).State == EntityState.Deleted
                || !relationshipKeySet.Contains(trackedRelationship.RelationshipKey))
            {
                continue;
            }

            existingByKey[trackedRelationship.RelationshipKey] = trackedRelationship;
        }

        var touched = 0;
        foreach (var linked in linkedRows)
        {
            if (!counterpartRows.TryGetValue(linked.CounterpartId, out var counterpart))
            {
                continue;
            }

            if (counterpart.LinkedTransferTransactionId != linked.Id)
            {
                continue;
            }

            normalizedRefsByTransactionId.TryGetValue(linked.Id, out var sourceRef);
            normalizedRefsByTransactionId.TryGetValue(linked.CounterpartId, out var targetRef);
            var confidenceScore = linked.TransferMatchConfidenceScore
                ?? counterpart.TransferMatchConfidenceScore
                ?? InternalTransferMatchMinimumScore;
            var confidenceTier = NormalizeRelationshipConfidenceTier(
                linked.TransferMatchConfidenceTier ?? counterpart.TransferMatchConfidenceTier,
                confidenceScore);
            var reason = linked.TransferMatchReason
                ?? counterpart.TransferMatchReason
                ?? "linked_internal_pair";

            var changed = UpsertTransactionRelationship(
                existingByKey,
                now,
                relationshipKey: BuildRelationshipKey(TransactionRelationshipType.InternalAccountTransfer, linked.Id, linked.CounterpartId),
                relationshipType: TransactionRelationshipType.InternalAccountTransfer,
                relationshipStatus: TransactionRelationshipStatus.Active,
                relationshipDirection: TransactionRelationshipDirection.OutflowToInflow,
                sourceTransactionId: linked.Id,
                targetTransactionId: linked.CounterpartId,
                sourceRawBankTransactionId: sourceRef.RawBankTransactionId,
                targetRawBankTransactionId: targetRef.RawBankTransactionId,
                sourceFinancialAccountId: linked.FinancialAccountId,
                targetFinancialAccountId: counterpart.FinancialAccountId,
                confidenceScore: confidenceScore,
                confidenceTier: confidenceTier,
                matchReasonsJson: JsonSerializer.Serialize(new
                {
                    reason,
                    source = "linked_internal_transfer"
                }),
                providerPolicyKey: sourceRef.PolicyKey ?? targetRef.PolicyKey,
                analyticsTreatment: "exclude_income_expense_internal_transfer",
                virtualDestinationLabel: null);

            if (changed)
            {
                touched++;
            }
        }

        return touched;
    }

    private async Task<int> UpsertSavingsMovementRelationshipsAsync(
        Guid userId,
        DateTime now,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var transactions = await dbContext.Transactions
            .Where(x =>
                x.FinancialAccount != null
                && x.FinancialAccount.UserId == userId
                && x.BookedAtUtc >= windowStartUtc
                && x.BookedAtUtc <= windowEndUtc
                && x.Amount != 0m)
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        if (transactions.Count == 0)
        {
            return 0;
        }

        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var normalizedRefsByTransactionId = await LoadProjectedRawReferencesAsync(transactionIds, cancellationToken);

        var relevantExisting = await dbContext.TransactionRelationships
            .Where(x =>
                transactionIds.Contains(x.SourceTransactionId)
                && (x.RelationshipType == TransactionRelationshipType.SavingsRoundup
                    || x.RelationshipType == TransactionRelationshipType.SavingsManualDeposit
                    || x.RelationshipType == TransactionRelationshipType.SavingsManualWithdrawal
                    || x.RelationshipType == TransactionRelationshipType.PossibleSavingsSuggestion))
            .ToListAsync(cancellationToken);
        var existingByKey = relevantExisting.ToDictionary(x => x.RelationshipKey, StringComparer.Ordinal);
        var sourceTransactionIdSet = transactionIds.ToHashSet();
        foreach (var trackedRelationship in dbContext.TransactionRelationships.Local)
        {
            if (dbContext.Entry(trackedRelationship).State == EntityState.Deleted
                || !sourceTransactionIdSet.Contains(trackedRelationship.SourceTransactionId)
                || (trackedRelationship.RelationshipType != TransactionRelationshipType.SavingsRoundup
                    && trackedRelationship.RelationshipType != TransactionRelationshipType.SavingsManualDeposit
                    && trackedRelationship.RelationshipType != TransactionRelationshipType.SavingsManualWithdrawal
                    && trackedRelationship.RelationshipType != TransactionRelationshipType.PossibleSavingsSuggestion))
            {
                continue;
            }

            existingByKey[trackedRelationship.RelationshipKey] = trackedRelationship;
        }
        var selectedRelationshipKeys = new HashSet<string>(StringComparer.Ordinal);

        var touched = 0;
        var outflowsByAccountId = transactions
            .Where(x => x.Amount < 0m)
            .GroupBy(x => x.FinancialAccountId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.BookedAtUtc).ToList());

        foreach (var transaction in transactions)
        {
            if (transaction.TransferKind == TransactionTransferKind.LinkedInternal)
            {
                continue;
            }

            if (!LooksLikeSavingsPocketMovementDescription(transaction.Description))
            {
                continue;
            }

            normalizedRefsByTransactionId.TryGetValue(transaction.Id, out var sourceRef);
            var providerPolicyKey = sourceRef.PolicyKey;

            if (transaction.Amount < 0m)
            {
                var roundUpCandidate = TryFindRoundupMerchantCounterpart(transaction, outflowsByAccountId);
                if (roundUpCandidate is not null)
                {
                    var roundUpScore = 11
                        + (roundUpCandidate.Value.Multiplier > 1 ? 1 : 0)
                        + (LooksLikeRoundUpDescription(transaction.Description) ? 2 : 0);
                    var roundUpTier = NormalizeRelationshipConfidenceTier(null, roundUpScore);
                    var relationshipStatus = roundUpTier == "high"
                        ? TransactionRelationshipStatus.Active
                        : TransactionRelationshipStatus.Suggested;

                    normalizedRefsByTransactionId.TryGetValue(roundUpCandidate.Value.MerchantTransaction.Id, out var targetRef);
                    var relationshipKey = BuildRelationshipKey(
                        roundUpTier == "high"
                            ? TransactionRelationshipType.SavingsRoundup
                            : TransactionRelationshipType.PossibleSavingsSuggestion,
                        transaction.Id,
                        roundUpCandidate.Value.MerchantTransaction.Id);
                    selectedRelationshipKeys.Add(relationshipKey);

                    var changed = UpsertTransactionRelationship(
                        existingByKey,
                        now,
                        relationshipKey,
                        roundUpTier == "high"
                            ? TransactionRelationshipType.SavingsRoundup
                            : TransactionRelationshipType.PossibleSavingsSuggestion,
                        relationshipStatus,
                        TransactionRelationshipDirection.OutflowToSavings,
                        sourceTransactionId: transaction.Id,
                        targetTransactionId: roundUpCandidate.Value.MerchantTransaction.Id,
                        sourceRawBankTransactionId: sourceRef.RawBankTransactionId,
                        targetRawBankTransactionId: targetRef.RawBankTransactionId,
                        sourceFinancialAccountId: transaction.FinancialAccountId,
                        targetFinancialAccountId: roundUpCandidate.Value.MerchantTransaction.FinancialAccountId,
                        confidenceScore: roundUpScore,
                        confidenceTier: roundUpTier,
                        matchReasonsJson: JsonSerializer.Serialize(new
                        {
                            reason = "roundup_pattern_match",
                            roundUpBase = roundUpCandidate.Value.RoundupBase,
                            multiplier = roundUpCandidate.Value.Multiplier,
                            merchantTransactionId = roundUpCandidate.Value.MerchantTransaction.Id
                        }),
                        providerPolicyKey: providerPolicyKey,
                        analyticsTreatment: roundUpTier == "high"
                            ? "exclude_income_expense_include_savings_roundup"
                            : "suggestion_only",
                        virtualDestinationLabel: ResolveSavingsDestinationLabel(transaction.Description, providerPolicyKey));

                    if (changed)
                    {
                        touched++;
                    }

                    if (roundUpTier == "high")
                    {
                        ApplySavingsMovementClassification(
                            transaction,
                            TransactionTransferKind.SavingsRoundup,
                            roundUpScore,
                            roundUpTier,
                            "roundup_pattern_match");
                    }

                    continue;
                }

                var manualDepositScore = HasStrongSavingsPocketSignal(transaction.Description)
                    ? 10
                    : LooksLikeRoundUpDescription(transaction.Description) ? 8 : 9;
                var manualDepositTier = NormalizeRelationshipConfidenceTier(null, manualDepositScore);
                var manualDepositStatus = manualDepositTier == "high"
                    ? TransactionRelationshipStatus.Active
                    : TransactionRelationshipStatus.Suggested;
                var manualDepositType = manualDepositTier == "high"
                    ? TransactionRelationshipType.SavingsManualDeposit
                    : TransactionRelationshipType.PossibleSavingsSuggestion;
                var manualDepositKey = BuildRelationshipKey(manualDepositType, transaction.Id, null);
                selectedRelationshipKeys.Add(manualDepositKey);

                if (UpsertTransactionRelationship(
                        existingByKey,
                        now,
                        manualDepositKey,
                        manualDepositType,
                        manualDepositStatus,
                        TransactionRelationshipDirection.OutflowToSavings,
                        sourceTransactionId: transaction.Id,
                        targetTransactionId: null,
                        sourceRawBankTransactionId: sourceRef.RawBankTransactionId,
                        targetRawBankTransactionId: null,
                        sourceFinancialAccountId: transaction.FinancialAccountId,
                        targetFinancialAccountId: null,
                        confidenceScore: manualDepositScore,
                        confidenceTier: manualDepositTier,
                        matchReasonsJson: JsonSerializer.Serialize(new
                        {
                            reason = "savings_keyword_outflow",
                            description = transaction.Description
                        }),
                        providerPolicyKey: providerPolicyKey,
                        analyticsTreatment: manualDepositTier == "high"
                            ? "exclude_income_expense_include_savings_flow"
                            : "suggestion_only",
                        virtualDestinationLabel: ResolveSavingsDestinationLabel(transaction.Description, providerPolicyKey)))
                {
                    touched++;
                }

                if (manualDepositTier == "high")
                {
                    ApplySavingsMovementClassification(
                        transaction,
                        TransactionTransferKind.SavingsManualDeposit,
                        manualDepositScore,
                        manualDepositTier,
                        "savings_keyword_outflow");
                }

                continue;
            }

            var withdrawalScore = HasStrongSavingsPocketSignal(transaction.Description) ? 10 : 9;
            var withdrawalTier = NormalizeRelationshipConfidenceTier(null, withdrawalScore);
            var withdrawalStatus = withdrawalTier == "high"
                ? TransactionRelationshipStatus.Active
                : TransactionRelationshipStatus.Suggested;
            var withdrawalType = withdrawalTier == "high"
                ? TransactionRelationshipType.SavingsManualWithdrawal
                : TransactionRelationshipType.PossibleSavingsSuggestion;
            var withdrawalKey = BuildRelationshipKey(withdrawalType, transaction.Id, null);
            selectedRelationshipKeys.Add(withdrawalKey);

            if (UpsertTransactionRelationship(
                    existingByKey,
                    now,
                    withdrawalKey,
                    withdrawalType,
                    withdrawalStatus,
                    TransactionRelationshipDirection.InflowFromSavings,
                    sourceTransactionId: transaction.Id,
                    targetTransactionId: null,
                    sourceRawBankTransactionId: sourceRef.RawBankTransactionId,
                    targetRawBankTransactionId: null,
                    sourceFinancialAccountId: transaction.FinancialAccountId,
                    targetFinancialAccountId: null,
                    confidenceScore: withdrawalScore,
                    confidenceTier: withdrawalTier,
                    matchReasonsJson: JsonSerializer.Serialize(new
                    {
                        reason = "savings_keyword_inflow",
                        description = transaction.Description
                    }),
                    providerPolicyKey: providerPolicyKey,
                    analyticsTreatment: withdrawalTier == "high"
                        ? "exclude_income_expense_include_savings_flow"
                        : "suggestion_only",
                    virtualDestinationLabel: ResolveSavingsDestinationLabel(transaction.Description, providerPolicyKey)))
            {
                touched++;
            }

            if (withdrawalTier == "high")
            {
                ApplySavingsMovementClassification(
                    transaction,
                    TransactionTransferKind.SavingsManualWithdrawal,
                    withdrawalScore,
                    withdrawalTier,
                    "savings_keyword_inflow");
            }
        }

        foreach (var existing in relevantExisting)
        {
            if (!selectedRelationshipKeys.Contains(existing.RelationshipKey)
                && existing.RelationshipStatus != TransactionRelationshipStatus.Dismissed)
            {
                existing.RelationshipStatus = TransactionRelationshipStatus.Dismissed;
                existing.UpdatedUtc = now;
                touched++;
            }
        }

        return touched;
    }

    private static void ApplySavingsMovementClassification(
        Transaction transaction,
        TransactionTransferKind transferKind,
        int confidenceScore,
        string confidenceTier,
        string reason)
    {
        transaction.TransferKind = transferKind;
        transaction.LinkedTransferTransactionId = null;
        transaction.LinkedTransferMatchedUtc = null;
        transaction.TransferMatchConfidenceScore = confidenceScore;
        transaction.TransferMatchConfidenceTier = confidenceTier;
        transaction.TransferMatchReason = reason;
        ApplyAutoSavingsTransferTaxonomy(transaction);
    }

    private async Task<Dictionary<Guid, ProjectedRawReference>> LoadProjectedRawReferencesAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return [];
        }

        return await dbContext.NormalizedBankTransactions
            .AsNoTracking()
            .Where(x => x.ProjectedTransactionId.HasValue && transactionIds.Contains(x.ProjectedTransactionId.Value))
            .Select(x => new
            {
                TransactionId = x.ProjectedTransactionId!.Value,
                x.RawBankTransactionId,
                x.NormalizationPolicyKey
            })
            .ToDictionaryAsync(
                x => x.TransactionId,
                x => new ProjectedRawReference(x.RawBankTransactionId, x.NormalizationPolicyKey),
                cancellationToken);
    }

    private bool UpsertTransactionRelationship(
        IDictionary<string, TransactionRelationship> existingByKey,
        DateTime now,
        string relationshipKey,
        TransactionRelationshipType relationshipType,
        TransactionRelationshipStatus relationshipStatus,
        TransactionRelationshipDirection relationshipDirection,
        Guid sourceTransactionId,
        Guid? targetTransactionId,
        Guid? sourceRawBankTransactionId,
        Guid? targetRawBankTransactionId,
        Guid sourceFinancialAccountId,
        Guid? targetFinancialAccountId,
        int confidenceScore,
        string confidenceTier,
        string? matchReasonsJson,
        string? providerPolicyKey,
        string? analyticsTreatment,
        string? virtualDestinationLabel)
    {
        if (!existingByKey.TryGetValue(relationshipKey, out var existing))
        {
            existing = new TransactionRelationship
            {
                Id = Guid.NewGuid(),
                RelationshipKey = relationshipKey,
                CreatedUtc = now
            };
            dbContext.TransactionRelationships.Add(existing);
            existingByKey[relationshipKey] = existing;
            UpdateRelationshipFields(
                existing,
                now,
                relationshipType,
                relationshipStatus,
                relationshipDirection,
                sourceTransactionId,
                targetTransactionId,
                sourceRawBankTransactionId,
                targetRawBankTransactionId,
                sourceFinancialAccountId,
                targetFinancialAccountId,
                confidenceScore,
                confidenceTier,
                matchReasonsJson,
                providerPolicyKey,
                analyticsTreatment,
                virtualDestinationLabel);
            return true;
        }

        return UpdateRelationshipFields(
            existing,
            now,
            relationshipType,
            relationshipStatus,
            relationshipDirection,
            sourceTransactionId,
            targetTransactionId,
            sourceRawBankTransactionId,
            targetRawBankTransactionId,
            sourceFinancialAccountId,
            targetFinancialAccountId,
            confidenceScore,
            confidenceTier,
            matchReasonsJson,
            providerPolicyKey,
            analyticsTreatment,
            virtualDestinationLabel);
    }

    private static bool UpdateRelationshipFields(
        TransactionRelationship relationship,
        DateTime now,
        TransactionRelationshipType relationshipType,
        TransactionRelationshipStatus relationshipStatus,
        TransactionRelationshipDirection relationshipDirection,
        Guid sourceTransactionId,
        Guid? targetTransactionId,
        Guid? sourceRawBankTransactionId,
        Guid? targetRawBankTransactionId,
        Guid sourceFinancialAccountId,
        Guid? targetFinancialAccountId,
        int confidenceScore,
        string confidenceTier,
        string? matchReasonsJson,
        string? providerPolicyKey,
        string? analyticsTreatment,
        string? virtualDestinationLabel)
    {
        var changed = false;

        if (relationship.RelationshipType != relationshipType)
        {
            relationship.RelationshipType = relationshipType;
            changed = true;
        }

        if (relationship.RelationshipStatus != relationshipStatus)
        {
            relationship.RelationshipStatus = relationshipStatus;
            changed = true;
        }

        if (relationship.RelationshipDirection != relationshipDirection)
        {
            relationship.RelationshipDirection = relationshipDirection;
            changed = true;
        }

        if (relationship.SourceTransactionId != sourceTransactionId)
        {
            relationship.SourceTransactionId = sourceTransactionId;
            changed = true;
        }

        if (relationship.TargetTransactionId != targetTransactionId)
        {
            relationship.TargetTransactionId = targetTransactionId;
            changed = true;
        }

        if (relationship.SourceRawBankTransactionId != sourceRawBankTransactionId)
        {
            relationship.SourceRawBankTransactionId = sourceRawBankTransactionId;
            changed = true;
        }

        if (relationship.TargetRawBankTransactionId != targetRawBankTransactionId)
        {
            relationship.TargetRawBankTransactionId = targetRawBankTransactionId;
            changed = true;
        }

        if (relationship.SourceFinancialAccountId != sourceFinancialAccountId)
        {
            relationship.SourceFinancialAccountId = sourceFinancialAccountId;
            changed = true;
        }

        if (relationship.TargetFinancialAccountId != targetFinancialAccountId)
        {
            relationship.TargetFinancialAccountId = targetFinancialAccountId;
            changed = true;
        }

        if (relationship.ConfidenceScore != confidenceScore)
        {
            relationship.ConfidenceScore = confidenceScore;
            changed = true;
        }

        if (!string.Equals(relationship.ConfidenceTier, confidenceTier, StringComparison.Ordinal))
        {
            relationship.ConfidenceTier = confidenceTier;
            changed = true;
        }

        if (!string.Equals(relationship.MatchReasonsJson, matchReasonsJson, StringComparison.Ordinal))
        {
            relationship.MatchReasonsJson = matchReasonsJson;
            changed = true;
        }

        if (!string.Equals(relationship.ProviderPolicyKey, providerPolicyKey, StringComparison.Ordinal))
        {
            relationship.ProviderPolicyKey = providerPolicyKey;
            changed = true;
        }

        if (!string.Equals(relationship.AnalyticsTreatment, analyticsTreatment, StringComparison.Ordinal))
        {
            relationship.AnalyticsTreatment = analyticsTreatment;
            changed = true;
        }

        if (!string.Equals(relationship.VirtualDestinationLabel, virtualDestinationLabel, StringComparison.Ordinal))
        {
            relationship.VirtualDestinationLabel = virtualDestinationLabel;
            changed = true;
        }

        if (changed)
        {
            relationship.UpdatedUtc = now;
        }

        return changed;
    }

    private static string BuildRelationshipKey(
        TransactionRelationshipType relationshipType,
        Guid sourceTransactionId,
        Guid? targetTransactionId)
    {
        return $"{relationshipType}:{sourceTransactionId:N}:{(targetTransactionId.HasValue ? targetTransactionId.Value.ToString("N") : "none")}";
    }

    private static RoundupCounterpartMatch? TryFindRoundupMerchantCounterpart(
        Transaction savingsTransaction,
        IReadOnlyDictionary<Guid, List<Transaction>> outflowsByAccountId)
    {
        if (!outflowsByAccountId.TryGetValue(savingsTransaction.FinancialAccountId, out var accountOutflows))
        {
            return null;
        }

        var savingsAmount = decimal.Round(Math.Abs(savingsTransaction.Amount), 2, MidpointRounding.AwayFromZero);
        var candidates = accountOutflows
            .Where(x =>
                x.Id != savingsTransaction.Id
                && x.Amount < 0m
                && x.BookedAtUtc <= savingsTransaction.BookedAtUtc
                && (savingsTransaction.BookedAtUtc - x.BookedAtUtc).TotalHours <= 6
                && !LooksLikeSavingsPocketMovementDescription(x.Description))
            .OrderByDescending(x => x.BookedAtUtc)
            .ToList();

        foreach (var candidate in candidates)
        {
            var merchantAmount = decimal.Round(Math.Abs(candidate.Amount), 2, MidpointRounding.AwayFromZero);
            var roundUpBase = decimal.Round(Math.Ceiling(merchantAmount) - merchantAmount, 2, MidpointRounding.AwayFromZero);
            if (roundUpBase <= 0m)
            {
                continue;
            }

            for (var multiplier = 1; multiplier <= 10; multiplier++)
            {
                var expected = decimal.Round(roundUpBase * multiplier, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(expected - savingsAmount) <= 0.01m)
                {
                    return new RoundupCounterpartMatch(candidate, roundUpBase, multiplier);
                }
            }
        }

        return null;
    }

    private static bool LooksLikeRoundUpDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var normalized = description.ToLowerInvariant();
        return normalized.Contains("round up", StringComparison.Ordinal)
            || normalized.Contains("round-up", StringComparison.Ordinal)
            || normalized.Contains("spare change", StringComparison.Ordinal);
    }

    private static string ResolveSavingsDestinationLabel(string description, string? _providerPolicyKey)
    {
        var normalized = description.ToLowerInvariant();
        if (normalized.Contains("flexible cash", StringComparison.Ordinal))
        {
            return "Savings destination";
        }

        if (normalized.Contains("pocket", StringComparison.Ordinal))
        {
            return "Savings pocket";
        }

        if (normalized.Contains("vault", StringComparison.Ordinal))
        {
            return "Savings vault";
        }

        return "Internal savings destination";
    }

    private static string NormalizeRelationshipConfidenceTier(string? existingTier, int confidenceScore)
    {
        if (!string.IsNullOrWhiteSpace(existingTier))
        {
            var normalized = existingTier.Trim().ToLowerInvariant();
            if (normalized is "low" or "medium" or "high")
            {
                return normalized;
            }
        }

        if (confidenceScore >= 10)
        {
            return "high";
        }

        if (confidenceScore >= 7)
        {
            return "medium";
        }

        return "low";
    }

    private static bool IsAutoMatchEligible(Transaction transaction)
    {
        if (transaction.TransferKind is
            TransactionTransferKind.Manual
            or TransactionTransferKind.SavingsRoundup
            or TransactionTransferKind.SavingsManualDeposit
            or TransactionTransferKind.SavingsManualWithdrawal)
        {
            return false;
        }

        if (transaction.LinkedTransferTransactionId.HasValue)
        {
            return false;
        }

        if (!TransferPolicyEngine.IsAutoLinkedMatchPolicyEligible(
                transaction.TaxonomyDomainId,
                transaction.TaxonomyCategoryId,
                transaction.TaxonomySubcategoryId,
                transaction.TransferKind))
        {
            return false;
        }

        if (transaction.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId)
        {
            return true;
        }

        return !transaction.MetadataUpdatedUtc.HasValue;
    }

    private static string CreateInternalTransferAmountCurrencyKey(Transaction transaction)
    {
        var amount = decimal.Round(Math.Abs(transaction.Amount), 2, MidpointRounding.AwayFromZero);
        var currency = string.IsNullOrWhiteSpace(transaction.Currency)
            ? "EUR"
            : transaction.Currency.Trim().ToUpperInvariant();
        return $"{amount:0.00}|{currency}";
    }

    private static InternalTransferPairScore ScoreInternalTransferPair(
        Transaction debit,
        Transaction credit,
        IReadOnlyDictionary<Guid, InternalTransferAccountMatchProfile> accountProfilesByFinancialAccountId)
    {
        var debitLooksTransfer = LooksLikeInternalTransferDescription(debit.Description);
        var creditLooksTransfer = LooksLikeInternalTransferDescription(credit.Description);
        var debitLooksSavingsPocket = LooksLikeSavingsPocketMovementDescription(debit.Description);
        var creditLooksSavingsPocket = LooksLikeSavingsPocketMovementDescription(credit.Description);
        var involvesSavingsPocketMovement = debitLooksSavingsPocket || creditLooksSavingsPocket;
        var hasTransferHint = debitLooksTransfer || creditLooksTransfer;
        var hasTransferTaxonomyHint =
            debit.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId
            || credit.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId;
        var hasCounterpartyAccountHint =
            DescriptionContainsCounterpartyAccountHint(
                debit.Description,
                credit.FinancialAccountId,
                accountProfilesByFinancialAccountId)
            || DescriptionContainsCounterpartyAccountHint(
                credit.Description,
                debit.FinancialAccountId,
                accountProfilesByFinancialAccountId);
        var hasSharedTransferToken = HasSharedTransferToken(debit.Description, credit.Description);
        var hasCounterpartyNameHint = HasStrongCounterpartyNameHint(debit.Description, credit.Description);
        var hasCounterpartyConfidence = hasCounterpartyAccountHint || hasCounterpartyNameHint;
        var hasWeakTimestampPrecision =
            IsWeakTimestampForMatching(debit, accountProfilesByFinancialAccountId)
            || IsWeakTimestampForMatching(credit, accountProfilesByFinancialAccountId);

        var hasTransferEvidence =
            hasTransferHint
            || hasTransferTaxonomyHint
            || hasCounterpartyAccountHint
            || hasCounterpartyNameHint
            || (hasSharedTransferToken && (hasTransferHint || hasCounterpartyConfidence));

        var distance = (debit.BookedAtUtc - credit.BookedAtUtc).Duration();
        if (distance.TotalHours > InternalTransferMatchMaxWindowHours)
        {
            return new InternalTransferPairScore(
                Score: int.MinValue,
                Distance: distance,
                ConfidenceTier: TransferMatchConfidenceTier.Low,
                HasTransferEvidence: hasTransferEvidence,
                HasCounterpartyConfidence: hasCounterpartyConfidence,
                HasWeakTimestampPrecision: hasWeakTimestampPrecision,
                InvolvesSavingsPocketMovement: involvesSavingsPocketMovement,
                DeferredByWeakTimestamp: false,
                DeferredBySavingsPocket: false,
                MissingCounterpartyConfidence: false,
                DecisionReason: "outside_time_window");
        }

        if (!hasTransferEvidence)
        {
            return new InternalTransferPairScore(
                Score: int.MinValue,
                Distance: distance,
                ConfidenceTier: TransferMatchConfidenceTier.Low,
                HasTransferEvidence: false,
                HasCounterpartyConfidence: hasCounterpartyConfidence,
                HasWeakTimestampPrecision: hasWeakTimestampPrecision,
                InvolvesSavingsPocketMovement: involvesSavingsPocketMovement,
                DeferredByWeakTimestamp: false,
                DeferredBySavingsPocket: false,
                MissingCounterpartyConfidence: !hasCounterpartyConfidence,
                DecisionReason: "no_transfer_evidence");
        }

        if (hasWeakTimestampPrecision
            && distance.TotalHours > 2
            && !hasCounterpartyConfidence
            && debit.BookedAtUtc.Date != credit.BookedAtUtc.Date)
        {
            return new InternalTransferPairScore(
                Score: int.MinValue,
                Distance: distance,
                ConfidenceTier: TransferMatchConfidenceTier.Low,
                HasTransferEvidence: hasTransferEvidence,
                HasCounterpartyConfidence: false,
                HasWeakTimestampPrecision: hasWeakTimestampPrecision,
                InvolvesSavingsPocketMovement: involvesSavingsPocketMovement,
                DeferredByWeakTimestamp: true,
                DeferredBySavingsPocket: false,
                MissingCounterpartyConfidence: true,
                DecisionReason: "deferred_weak_timestamp_low_counterparty_confidence");
        }

        if (involvesSavingsPocketMovement && !hasCounterpartyConfidence)
        {
            return new InternalTransferPairScore(
                Score: int.MinValue,
                Distance: distance,
                ConfidenceTier: TransferMatchConfidenceTier.Low,
                HasTransferEvidence: hasTransferEvidence,
                HasCounterpartyConfidence: false,
                HasWeakTimestampPrecision: hasWeakTimestampPrecision,
                InvolvesSavingsPocketMovement: involvesSavingsPocketMovement,
                DeferredByWeakTimestamp: false,
                DeferredBySavingsPocket: true,
                MissingCounterpartyConfidence: true,
                DecisionReason: "deferred_savings_pocket_low_counterparty_confidence");
        }

        var score = 0;
        if (distance.TotalHours <= 1)
        {
            score += 5;
        }
        else if (distance.TotalHours <= 3)
        {
            score += 4;
        }
        else if (distance.TotalHours <= 8)
        {
            score += 3;
        }
        else if (distance.TotalHours <= 18)
        {
            score += 2;
        }
        else if (distance.TotalHours <= 36)
        {
            score += 1;
        }
        else
        {
            score += 0;
        }

        if (hasTransferHint)
        {
            score += 1;
        }

        if (hasCounterpartyAccountHint)
        {
            score += 3;
        }

        if (hasCounterpartyNameHint)
        {
            score += 3;
        }

        if (hasSharedTransferToken)
        {
            score += 1;
        }

        if (debit.BookedAtUtc.Date == credit.BookedAtUtc.Date)
        {
            score += 1;
        }

        if (hasTransferTaxonomyHint)
        {
            score += 1;
        }

        if (hasWeakTimestampPrecision && !hasCounterpartyConfidence)
        {
            score -= 2;
        }

        if (involvesSavingsPocketMovement)
        {
            score -= 2;
        }

        if (!hasCounterpartyConfidence)
        {
            score -= 1;
        }

        var confidenceTier = DetermineTransferConfidenceTier(score, hasCounterpartyConfidence);

        return new InternalTransferPairScore(
            Score: score,
            Distance: distance,
            ConfidenceTier: confidenceTier,
            HasTransferEvidence: hasTransferEvidence,
            HasCounterpartyConfidence: hasCounterpartyConfidence,
            HasWeakTimestampPrecision: hasWeakTimestampPrecision,
            InvolvesSavingsPocketMovement: involvesSavingsPocketMovement,
            DeferredByWeakTimestamp: false,
            DeferredBySavingsPocket: false,
            MissingCounterpartyConfidence: !hasCounterpartyConfidence,
            DecisionReason: "scored");
    }

    private static TransferMatchConfidenceTier DetermineTransferConfidenceTier(
        int score,
        bool hasCounterpartyConfidence)
    {
        if (score >= InternalTransferMatchMinimumScore && hasCounterpartyConfidence)
        {
            return TransferMatchConfidenceTier.High;
        }

        if (score >= InternalTransferMatchMinimumScore)
        {
            return TransferMatchConfidenceTier.Medium;
        }

        return TransferMatchConfidenceTier.Low;
    }

    private static bool DescriptionContainsCounterpartyAccountHint(
        string description,
        Guid counterpartyFinancialAccountId,
        IReadOnlyDictionary<Guid, InternalTransferAccountMatchProfile> accountProfilesByFinancialAccountId)
    {
        if (!accountProfilesByFinancialAccountId.TryGetValue(counterpartyFinancialAccountId, out var accountProfile)
            || accountProfile.HintTokens.Count == 0)
        {
            return false;
        }

        var descriptionTokens = TokenizeTransferDescription(description);
        if (descriptionTokens.Count == 0)
        {
            return false;
        }

        foreach (var token in descriptionTokens)
        {
            if (accountProfile.HintTokens.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSharedTransferToken(string leftDescription, string rightDescription)
    {
        var leftTokens = TokenizeTransferDescription(leftDescription);
        if (leftTokens.Count == 0)
        {
            return false;
        }

        var rightTokens = TokenizeTransferDescription(rightDescription);
        if (rightTokens.Count == 0)
        {
            return false;
        }

        foreach (var token in leftTokens)
        {
            if (rightTokens.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStrongCounterpartyNameHint(string? leftDescription, string? rightDescription)
    {
        var leftTokens = ExtractCounterpartyNameTokens(leftDescription);
        if (leftTokens.Count == 0)
        {
            return false;
        }

        var rightTokens = ExtractCounterpartyNameTokens(rightDescription);
        if (rightTokens.Count == 0)
        {
            return false;
        }

        var overlap = 0;
        foreach (var token in leftTokens)
        {
            if (rightTokens.Contains(token))
            {
                overlap++;
            }
        }

        return overlap >= 2;
    }

    private static HashSet<string> TokenizeTransferDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        var separators = new[] { ' ', '-', '_', '/', '\\', '.', ',', '#', ':', ';', '(', ')' };
        return description
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Where(token => token is not "the" and not "and" and not "from" and not "into")
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ExtractCounterpartyNameTokens(string? description)
    {
        var tokens = TokenizeTransferDescription(description);
        tokens.RemoveWhere(token =>
            token.Length < 4
            || InternalTransferGenericNoiseTokens.Contains(token)
            || token.All(char.IsDigit));
        return tokens;
    }

    private static bool IsWeakTimestampForMatching(
        Transaction transaction,
        IReadOnlyDictionary<Guid, InternalTransferAccountMatchProfile> accountProfilesByFinancialAccountId)
    {
        if (!accountProfilesByFinancialAccountId.TryGetValue(transaction.FinancialAccountId, out var profile))
        {
            return false;
        }

        if (profile.TimestampPrecision != ProviderTimestampPrecisionMode.DateOnlyMidnight)
        {
            return false;
        }

        return true;
    }

    private static DateTime GetTransferMatchingDate(DateTime bookedAtUtc, bool weakTimestamp)
    {
        return weakTimestamp
            ? bookedAtUtc.AddHours(12).Date
            : bookedAtUtc.Date;
    }

    private static HashSet<string> BuildInternalTransferAccountHintTokens(
        string? displayName,
        string? providerDisplayName,
        string? providerId)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        AddTransferHintTokens(tokens, displayName);
        AddTransferHintTokens(tokens, providerDisplayName);
        AddTransferHintTokens(tokens, providerId);
        tokens.RemoveWhere(token =>
            InternalTransferAccountHintStopTokens.Contains(token)
            || token.All(char.IsDigit));
        return tokens;
    }

    private static void AddTransferHintTokens(HashSet<string> output, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var token in TokenizeTransferDescription(value))
        {
            output.Add(token);
        }
    }

    private static bool LooksLikeInternalTransferDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var normalized = description.ToLowerInvariant();
        return normalized.Contains("transfer", StringComparison.Ordinal)
            || normalized.Contains("top up", StringComparison.Ordinal)
            || normalized.Contains("top-up", StringComparison.Ordinal)
            || normalized.Contains("card payment", StringComparison.Ordinal)
            || normalized.Contains("move money", StringComparison.Ordinal)
            || normalized.Contains("bank to bank", StringComparison.Ordinal)
            || normalized.Contains("internal", StringComparison.Ordinal);
    }

    private static bool LooksLikeSavingsPocketMovementDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var normalized = description.ToLowerInvariant();
        return ContainsAnyKeyword(normalized, SavingsMovementSignalKeywords);
    }

    private static bool HasStrongSavingsPocketSignal(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var normalized = description.ToLowerInvariant();
        return ContainsAnyKeyword(normalized, StrongSavingsMovementSignalKeywords);
    }

    private static bool ContainsAnyKeyword(string normalizedText, IReadOnlyList<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (normalizedText.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private enum TransferMatchConfidenceTier
    {
        Low,
        Medium,
        High
    }

    private readonly record struct InternalTransferPairScore(
        int Score,
        TimeSpan Distance,
        TransferMatchConfidenceTier ConfidenceTier,
        bool HasTransferEvidence,
        bool HasCounterpartyConfidence,
        bool HasWeakTimestampPrecision,
        bool InvolvesSavingsPocketMovement,
        bool DeferredByWeakTimestamp,
        bool DeferredBySavingsPocket,
        bool MissingCounterpartyConfidence,
        string DecisionReason);

    private readonly record struct InternalTransferPairIdentity(
        Guid DebitId,
        Guid CreditId);

    private readonly record struct InternalTransferAmountGroupSummary(
        int MatchedPairs,
        int ClustersProcessed,
        int ClustersRepaired,
        int CandidatesEvaluated,
        int CandidatesAutoEligible,
        int CandidatesRejectedByAmbiguity,
        int CandidatesRejectedByMutualBest)
    {
        public static InternalTransferAmountGroupSummary Empty =>
            new(
                MatchedPairs: 0,
                ClustersProcessed: 0,
                ClustersRepaired: 0,
                CandidatesEvaluated: 0,
                CandidatesAutoEligible: 0,
                CandidatesRejectedByAmbiguity: 0,
                CandidatesRejectedByMutualBest: 0);
    }

    private readonly record struct InternalTransferCandidateEdge(
        string AmountCurrencyKey,
        Transaction Debit,
        Transaction Credit,
        InternalTransferPairScore BaseScore,
        int DayDistance,
        bool IsSameDay,
        int DistanceMinutes,
        int SequenceBonus,
        int AdjustedScore,
        TransferMatchConfidenceTier AdjustedConfidenceTier,
        bool AutoEligible,
        int Weight,
        string ScoreBreakdown);

    private readonly record struct InternalTransferCandidateCluster(
        string Key,
        string AmountCurrencyKey,
        DateTime StartUtc,
        DateTime EndUtc,
        IReadOnlyList<Transaction> Outgoing,
        IReadOnlyList<Transaction> Incoming,
        IReadOnlyList<InternalTransferCandidateEdge> Edges);

    private readonly record struct CounterpartRanking(
        Guid BestCounterpartId,
        int BestScore,
        int? SecondBestScore);

    private readonly record struct ClusterReconcileResult(
        int MatchedPairs,
        bool Repaired,
        int RejectedByAmbiguity,
        int RejectedByMutualBest)
    {
        public static ClusterReconcileResult Empty =>
            new(
                MatchedPairs: 0,
                Repaired: false,
                RejectedByAmbiguity: 0,
                RejectedByMutualBest: 0);
    }

    private sealed class DisjointSet<T>
        where T : notnull
    {
        private readonly Dictionary<T, T> parent = [];

        public T Find(T value)
        {
            if (!parent.TryGetValue(value, out var current))
            {
                parent[value] = value;
                return value;
            }

            if (!EqualityComparer<T>.Default.Equals(current, value))
            {
                parent[value] = Find(current);
            }

            return parent[value];
        }

        public void Union(T left, T right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (!EqualityComparer<T>.Default.Equals(leftRoot, rightRoot))
            {
                parent[rightRoot] = leftRoot;
            }
        }
    }

    private readonly record struct ProjectedRawReference(
        Guid RawBankTransactionId,
        string? PolicyKey);

    private readonly record struct RoundupCounterpartMatch(
        Transaction MerchantTransaction,
        decimal RoundupBase,
        int Multiplier);

    private readonly record struct DeterministicRemainingWorkSnapshot(
        int RowsTotal,
        int RowsTerminalCurrentVersion,
        int RowsRemaining,
        int RowsActionableRemaining,
        int RowsDeferredRemaining,
        int RowsDeferredWaitingForCounterparty,
        int RowsDeferredWaitingForMoreContext,
        int RowsDeferredLegitimateWaiting,
        int RowsDeferredReadyForTerminalization,
        int RowsRejectedAmbiguous,
        int RowsEvaluatedNoMatchingRule,
        int RowsNotEvaluated,
        int RowsEvaluating,
        int RowsVersionBehind,
        int RowsMarkedForReclassification,
        int RowsSupersededRecomputeRequired,
        bool FullSameUserCounterpartyUniversePresent,
        string DeferredReasonBreakdown,
        string DeferredFamilyBreakdown)
    {
        public static readonly DeterministicRemainingWorkSnapshot Empty = new(
            RowsTotal: 0,
            RowsTerminalCurrentVersion: 0,
            RowsRemaining: 0,
            RowsActionableRemaining: 0,
            RowsDeferredRemaining: 0,
            RowsDeferredWaitingForCounterparty: 0,
            RowsDeferredWaitingForMoreContext: 0,
            RowsDeferredLegitimateWaiting: 0,
            RowsDeferredReadyForTerminalization: 0,
            RowsRejectedAmbiguous: 0,
            RowsEvaluatedNoMatchingRule: 0,
            RowsNotEvaluated: 0,
            RowsEvaluating: 0,
            RowsVersionBehind: 0,
            RowsMarkedForReclassification: 0,
            RowsSupersededRecomputeRequired: 0,
            FullSameUserCounterpartyUniversePresent: false,
            DeferredReasonBreakdown: "none",
            DeferredFamilyBreakdown: "none");
    }

    private readonly record struct DeterministicEnrichmentPassSummary(
        int LinkedTransfersMatched,
        int RelationshipRowsUpserted,
        int RowsEvaluated,
        int RowsRemaining,
        int BatchesProcessed,
        string Mode,
        bool HistoricalEnrichmentInProgress,
        bool HistoricalEnrichmentCompleted,
        double? HistoricalEnrichmentProgressPercent,
        DateTime? HistoricalEnrichmentCheckpointUtc,
        DeterministicRemainingWorkSnapshot RemainingWorkSnapshot,
        bool HasChanges);

    private sealed class InternalTransferAccountMatchProfile(
        HashSet<string> hintTokens,
        ProviderTimestampPrecisionMode timestampPrecision,
        string providerKey,
        string providerFamily)
    {
        public HashSet<string> HintTokens { get; } = hintTokens;
        public ProviderTimestampPrecisionMode TimestampPrecision { get; set; } = timestampPrecision;
        public string ProviderKey { get; } = providerKey;
        public string ProviderFamily { get; } = providerFamily;
    }

    private static void ApplyLinkedInternalTransferPair(
        Transaction debit,
        Transaction credit,
        InternalTransferPairScore score,
        DateTime now)
    {
        debit.TransferKind = TransactionTransferKind.LinkedInternal;
        debit.LinkedTransferTransactionId = credit.Id;
        debit.LinkedTransferMatchedUtc = now;
        debit.TransferMatchConfidenceScore = score.Score;
        debit.TransferMatchConfidenceTier = score.ConfidenceTier.ToString().ToLowerInvariant();
        debit.TransferMatchReason = score.DecisionReason;
        ApplyAutoTransferTaxonomy(debit);

        credit.TransferKind = TransactionTransferKind.LinkedInternal;
        credit.LinkedTransferTransactionId = debit.Id;
        credit.LinkedTransferMatchedUtc = now;
        credit.TransferMatchConfidenceScore = score.Score;
        credit.TransferMatchConfidenceTier = score.ConfidenceTier.ToString().ToLowerInvariant();
        credit.TransferMatchReason = score.DecisionReason;
        ApplyAutoTransferTaxonomy(credit);
    }

    private static void ResetLinkedTransferState(Transaction transaction)
    {
        transaction.LinkedTransferTransactionId = null;
        transaction.LinkedTransferMatchedUtc = null;
        transaction.TransferMatchConfidenceScore = null;
        transaction.TransferMatchConfidenceTier = null;
        transaction.TransferMatchReason = null;

        if (transaction.TransferKind == TransactionTransferKind.LinkedInternal)
        {
            transaction.TransferKind = null;

            if (!transaction.MetadataUpdatedUtc.HasValue
                && transaction.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId
                && transaction.TaxonomyCategoryId == ExpenseTaxonomyService.TransferDefaultCategoryId
                && transaction.TaxonomySubcategoryId == ExpenseTaxonomyService.TransferDefaultSubcategoryId)
            {
                transaction.TaxonomyDomainId = null;
                transaction.TaxonomyCategoryId = null;
                transaction.TaxonomySubcategoryId = null;
            }
        }
    }

    private static void ApplyAutoTransferTaxonomy(Transaction transaction)
    {
        if (transaction.MetadataUpdatedUtc.HasValue)
        {
            return;
        }

        transaction.TaxonomyDomainId = ExpenseTaxonomyService.TransferDomainId;
        transaction.TaxonomyCategoryId = ExpenseTaxonomyService.TransferDefaultCategoryId;
        transaction.TaxonomySubcategoryId = ExpenseTaxonomyService.TransferDefaultSubcategoryId;
    }

    private static void ApplyAutoSavingsTransferTaxonomy(Transaction transaction)
    {
        if (transaction.MetadataUpdatedUtc.HasValue)
        {
            return;
        }

        transaction.TaxonomyDomainId = ExpenseTaxonomyService.SavingsAndInvestmentsDomainId;
        transaction.TaxonomyCategoryId = SavingsTransferCategoryId;
        transaction.TaxonomySubcategoryId = SavingsTransferSubcategoryId;
    }

    private static bool IsOptionalDatasetUnsupported(ServiceError? error)
    {
        if (error is null)
        {
            return false;
        }

        return error.StatusCode is StatusCodes.Status400BadRequest
            or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound
            or StatusCodes.Status405MethodNotAllowed;
    }

    private static void ApplyGrantedScopes(OpenBankingConnection connection, string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            if (string.IsNullOrWhiteSpace(connection.GrantedScopesCsv))
            {
                connection.GrantedScopesCsv = string.Join(' ', TrueLayerScopes.Default);
            }

            return;
        }

        var normalized = scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            connection.GrantedScopesCsv = string.Join(' ', TrueLayerScopes.Default);
            return;
        }

        connection.GrantedScopesCsv = string.Join(' ', normalized);
    }

    private static bool ShouldRequestScope(string? grantedScopesCsv, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(requiredScope))
        {
            return false;
        }

        var scopes = string.IsNullOrWhiteSpace(grantedScopesCsv)
            ? RequestedScopeSet
            : grantedScopesCsv
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return scopes.Contains(requiredScope);
    }

    private async Task<ServiceResult<BankSyncResult>> HandleSyncFailureAsync(
        OpenBankingConnection connection,
        ServiceError error,
        string trigger,
        string stageName,
        CancellationToken cancellationToken)
    {
        var nextStatus = error.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? BankConnectionStatuses.ReauthRequired
            : BankConnectionStatuses.Failed;
        var persistedErrorCode = error.StatusCode == StatusCodes.Status429TooManyRequests
            ? "provider_too_many_requests"
            : error.Code;

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            nextStatus,
            persistedErrorCode,
            error.Message,
            cancellationToken);

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: trigger == "initial_sync" ? "initial_sync_failed" : "manual_sync_failed",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new
            {
                ErrorCode = persistedErrorCode,
                status = nextStatus,
                stage = stageName
            },
            cancellationToken);

        logger.LogWarning(
            "Bank sync failed for connectionId={ConnectionId} trigger={Trigger} stage={Stage} code={Code}",
            connection.Id,
            trigger,
            stageName,
            persistedErrorCode);

        return ServiceResult<BankSyncResult>.Fail(error.Message, persistedErrorCode, error.StatusCode);
    }

    private static string MapAccountType(string? providerAccountType)
    {
        var normalized = (providerAccountType ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "TRANSACTION" => "Current",
            "SAVINGS" => "Savings",
            "CREDIT" => "Credit",
            "CASH" => "Cash",
            _ => "Other"
        };
    }

    private static string ResolveProviderAccountDisplayName(
        OpenBankingConnection connection,
        TrueLayerAccountRecord providerAccount)
    {
        var providerLabel = ResolveProviderDisplayLabel(providerAccount.ProviderDisplayName ?? connection.ProviderDisplayName);
        var candidate = NormalizeLabel(providerAccount.DisplayName);
        if (!string.IsNullOrWhiteSpace(candidate)
            && LooksLikeConnectedIdentity(candidate, connection.IdentityInfo?.FullName))
        {
            candidate = null;
        }

        var accountCore = !string.IsNullOrWhiteSpace(candidate)
            ? candidate
            : ResolveFriendlyAccountType(providerAccount.AccountSubType ?? providerAccount.AccountType);
        var maskedHint = ExtractMaskedAccountHint(providerAccount.AccountNumberMetadataJson);

        if (!string.IsNullOrWhiteSpace(providerLabel) && !string.IsNullOrWhiteSpace(maskedHint))
        {
            return $"{providerLabel} **{maskedHint}";
        }

        if (!string.IsNullOrWhiteSpace(providerLabel))
        {
            return providerLabel;
        }

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        if (!string.IsNullOrWhiteSpace(accountCore) && !string.IsNullOrWhiteSpace(maskedHint))
        {
            return $"{accountCore} **{maskedHint}";
        }

        if (!string.IsNullOrWhiteSpace(accountCore))
        {
            return accountCore;
        }

        if (!string.IsNullOrWhiteSpace(maskedHint))
        {
            return $"Account **{maskedHint}";
        }

        return BuildAccountFallbackLabel(providerAccount.Currency, providerAccount.AccountType);
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

    private static string BuildAccountFallbackLabel(string currency, string? accountType)
    {
        var resolvedCurrency = string.IsNullOrWhiteSpace(currency)
            ? "EUR"
            : currency.Trim().ToUpperInvariant();
        var friendlyType = ResolveFriendlyAccountType(accountType);
        return $"{resolvedCurrency} {friendlyType}";
    }

    private static bool EqualsNormalizedLabel(string? left, string? right)
    {
        var normalizedLeft = NormalizeLabel(left)?.ToLowerInvariant();
        var normalizedRight = NormalizeLabel(right)?.ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && !string.IsNullOrWhiteSpace(normalizedRight)
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
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

    private static string? NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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

    private static void ApplyProviderBrandingFromAccount(
        OpenBankingConnection connection,
        TrueLayerAccountRecord providerAccount,
        DateTime now)
    {
        var updated = false;

        if (!string.IsNullOrWhiteSpace(providerAccount.ProviderIconUri))
        {
            connection.ProviderIconUri = providerAccount.ProviderIconUri;
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(providerAccount.ProviderLogoUri))
        {
            connection.ProviderLogoUri = providerAccount.ProviderLogoUri;
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(providerAccount.ProviderBrandBgColor))
        {
            connection.ProviderBrandBgColor = providerAccount.ProviderBrandBgColor;
            updated = true;
        }

        if (updated)
        {
            connection.BrandingLastSyncedAtUtc = now;
        }
    }

    private static void ApplyProviderBrandingFromProviderLookup(
        OpenBankingConnection connection,
        TrueLayerProviderBranding branding,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(branding.ProviderId))
        {
            connection.ProviderId = branding.ProviderId;
        }

        if (!string.IsNullOrWhiteSpace(branding.ProviderDisplayName))
        {
            connection.ProviderDisplayName = branding.ProviderDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(branding.ProviderIconUri))
        {
            connection.ProviderIconUri = branding.ProviderIconUri;
        }

        if (!string.IsNullOrWhiteSpace(branding.ProviderLogoUri))
        {
            connection.ProviderLogoUri = branding.ProviderLogoUri;
        }

        if (!string.IsNullOrWhiteSpace(branding.ProviderBrandBgColor))
        {
            connection.ProviderBrandBgColor = branding.ProviderBrandBgColor;
        }

        connection.BrandingLastSyncedAtUtc = now;
    }

    private static bool ShouldRefreshProviderBranding(OpenBankingConnection connection, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(connection.ProviderId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(connection.ProviderIconUri))
        {
            return true;
        }

        if (!connection.BrandingLastSyncedAtUtc.HasValue)
        {
            return true;
        }

        return connection.BrandingLastSyncedAtUtc.Value < now.AddDays(-30);
    }

    private async Task PersistSyncStageChangesAsync(
        Guid connectionId,
        string? accountId,
        string stageName,
        CancellationToken cancellationToken)
    {
        if (!dbContext.ChangeTracker.HasChanges())
        {
            return;
        }

        var trackedEntryCount = dbContext.ChangeTracker.Entries().Count(entry =>
            entry.State is not EntityState.Unchanged and not EntityState.Detached);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Persisted banking sync stage connectionId={ConnectionId} accountId={AccountId} stage={Stage} trackedEntries={TrackedEntries} elapsedMs={ElapsedMs}",
                connectionId,
                accountId ?? "<none>",
                stageName,
                trackedEntryCount,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException exception)
        {
            logger.LogError(
                exception,
                "Persisting banking sync stage was canceled connectionId={ConnectionId} accountId={AccountId} stage={Stage} trackedEntries={TrackedEntries} elapsedMs={ElapsedMs} cancellationRequested={CancellationRequested}",
                connectionId,
                accountId ?? "<none>",
                stageName,
                trackedEntryCount,
                stopwatch.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    private async Task<ServiceResult<AccountTransactionFetchResult>> FetchAccountTransactionsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        TrueLayerAccountRecord providerAccount,
        ProviderTransactionSyncPolicy policy,
        TrueLayerTransactionQueryWindow baseWindow,
        bool isInitialBackfill,
        CancellationToken cancellationToken)
    {
        var requestWindows = BuildTransactionRequestWindows(baseWindow, policy, isInitialBackfill);
        var adaptiveSplitEnabled = !isInitialBackfill && policy.SettledResponseCap.HasValue;

        logger.LogInformation(
            "Transaction sync strategy accountId={AccountId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} mode={Mode} policy={PolicyName} policyKey={PolicyKey} policyFamily={PolicyFamily} pendingSupport={PendingSupport} timestampPrecision={TimestampPrecision} requestWindows={RequestWindows} adaptiveSplitEnabled={AdaptiveSplitEnabled} settledResponseCap={SettledResponseCap}",
            providerAccount.AccountId,
            providerAccount.ProviderId ?? "<unknown>",
            providerAccount.ProviderDisplayName ?? "<unknown>",
            baseWindow.Mode,
            baseWindow.PolicyName ?? "<none>",
            policy.ProviderKey,
            policy.ProviderFamily,
            policy.PendingSupport,
            policy.TimestampPrecision,
            requestWindows.Count,
            adaptiveSplitEnabled,
            policy.SettledResponseCap);

        var mergedRecords = new List<TrueLayerTransactionRecord>();
        var settledFetched = 0;
        var pendingFetched = 0;
        var pendingSucceededWindows = 0;
        var pendingUnsupportedWindows = 0;
        var pendingFailedWindows = 0;
        var requestWindowCount = 0;
        var potentiallyCappedWindowCount = 0;
        var repeatedWindowPayloadCount = 0;
        string? previousSettledFingerprint = null;

        foreach (var requestWindow in requestWindows)
        {
            var windowFetchResult = await FetchAccountTransactionsWindowAsync(
                configuration,
                accessToken,
                providerAccount,
                requestWindow,
                policy,
                adaptiveSplitEnabled,
                depth: 0,
                cancellationToken);

            if (!windowFetchResult.Succeeded)
            {
                return ServiceResult<AccountTransactionFetchResult>.Fail(
                    windowFetchResult.Error!.Message,
                    windowFetchResult.Error.Code,
                    windowFetchResult.Error.StatusCode);
            }

            var segment = windowFetchResult.Value!;
            mergedRecords.AddRange(segment.Transactions);
            settledFetched += segment.SettledFetched;
            pendingFetched += segment.PendingFetched;
            pendingSucceededWindows += segment.PendingSucceededWindowCount;
            pendingUnsupportedWindows += segment.PendingUnsupportedWindowCount;
            pendingFailedWindows += segment.PendingFailedWindowCount;
            requestWindowCount += segment.RequestWindowCount;
            potentiallyCappedWindowCount += segment.PotentiallyCappedWindowCount;

            if (!string.IsNullOrWhiteSpace(previousSettledFingerprint)
                && string.Equals(previousSettledFingerprint, segment.SettledFingerprint, StringComparison.Ordinal))
            {
                repeatedWindowPayloadCount++;
            }

            if (!string.IsNullOrWhiteSpace(segment.SettledFingerprint))
            {
                previousSettledFingerprint = segment.SettledFingerprint;
            }
        }

        var mergedUniqueTransactions = MergeFetchedTransactions(mergedRecords);
        var mergedBounds = ExtractObservedTransactionBounds(mergedUniqueTransactions);

        return ServiceResult<AccountTransactionFetchResult>.Ok(
            new AccountTransactionFetchResult(
                Transactions: mergedUniqueTransactions,
                SettledFetched: settledFetched,
                PendingFetched: pendingFetched,
                PendingOutcome: ResolvePendingOutcome(
                    pendingSucceededWindows,
                    pendingUnsupportedWindows,
                    pendingFailedWindows),
                RequestWindowCount: requestWindowCount,
                PotentiallyCappedWindowCount: potentiallyCappedWindowCount,
                RepeatedWindowPayloadCount: repeatedWindowPayloadCount,
                EarliestReturnedUtc: mergedBounds.EarliestBookedAtUtc,
                LatestReturnedUtc: mergedBounds.LatestBookedAtUtc));
    }

    private async Task<ServiceResult<AccountTransactionWindowFetchResult>> FetchAccountTransactionsWindowAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        TrueLayerAccountRecord providerAccount,
        TrueLayerTransactionQueryWindow requestWindow,
        ProviderTransactionSyncPolicy policy,
        bool adaptiveSplitEnabled,
        int depth,
        CancellationToken cancellationToken)
    {
        var settledResult = await dataService.GetTransactionsAsync(
            configuration,
            accessToken,
            providerAccount.AccountId,
            requestWindow.FromUtc,
            requestWindow.ToUtc,
            cancellationToken);

        if (!settledResult.Succeeded)
        {
            return ServiceResult<AccountTransactionWindowFetchResult>.Fail(
                settledResult.Error!.Message,
                settledResult.Error.Code,
                settledResult.Error.StatusCode);
        }

        var settledTransactions = settledResult.Value!;
        var settledBounds = ExtractObservedTransactionBounds(settledTransactions);
        var possibleCappedResponse = policy.SettledResponseCap.HasValue
            && settledTransactions.Count >= policy.SettledResponseCap.Value;
        var lagFromWindowEndHours = requestWindow.ToUtc.HasValue && settledBounds.LatestBookedAtUtc.HasValue
            ? (requestWindow.ToUtc.Value - settledBounds.LatestBookedAtUtc.Value).TotalHours
            : (double?)null;
        var looksStaleRelativeToWindowEnd = lagFromWindowEndHours.HasValue && lagFromWindowEndHours.Value > 24;

        logger.LogInformation(
            "Fetched settled bank transactions window accountId={AccountId} providerId={ProviderId} depth={Depth} fromUtc={FromUtc} toUtc={ToUtc} mode={Mode} policy={PolicyName} settledCount={SettledCount} earliestSettledUtc={EarliestSettledUtc} latestSettledUtc={LatestSettledUtc} possibleCapped={PossibleCapped} lagFromWindowEndHours={LagFromWindowEndHours} looksStaleRelativeToWindowEnd={LooksStale}",
            providerAccount.AccountId,
            providerAccount.ProviderId ?? "<unknown>",
            depth,
            requestWindow.FromUtc,
            requestWindow.ToUtc,
            requestWindow.Mode,
            requestWindow.PolicyName ?? "<none>",
            settledTransactions.Count,
            settledBounds.EarliestBookedAtUtc,
            settledBounds.LatestBookedAtUtc,
            possibleCappedResponse,
            lagFromWindowEndHours,
            looksStaleRelativeToWindowEnd);

        if (adaptiveSplitEnabled
            && possibleCappedResponse
            && TrySplitWindow(requestWindow, policy.MinAdaptiveWindow, out var leftWindow, out var rightWindow)
            && depth < policy.MaxAdaptiveSplitDepth)
        {
            logger.LogInformation(
                "Splitting potentially capped transaction window accountId={AccountId} providerId={ProviderId} depth={Depth} originalFromUtc={OriginalFromUtc} originalToUtc={OriginalToUtc} leftFromUtc={LeftFromUtc} leftToUtc={LeftToUtc} rightFromUtc={RightFromUtc} rightToUtc={RightToUtc}",
                providerAccount.AccountId,
                providerAccount.ProviderId ?? "<unknown>",
                depth,
                requestWindow.FromUtc,
                requestWindow.ToUtc,
                leftWindow.FromUtc,
                leftWindow.ToUtc,
                rightWindow.FromUtc,
                rightWindow.ToUtc);

            var leftResult = await FetchAccountTransactionsWindowAsync(
                configuration,
                accessToken,
                providerAccount,
                leftWindow,
                policy,
                adaptiveSplitEnabled,
                depth + 1,
                cancellationToken);

            if (!leftResult.Succeeded)
            {
                return ServiceResult<AccountTransactionWindowFetchResult>.Fail(
                    leftResult.Error!.Message,
                    leftResult.Error.Code,
                    leftResult.Error.StatusCode);
            }

            var rightResult = await FetchAccountTransactionsWindowAsync(
                configuration,
                accessToken,
                providerAccount,
                rightWindow,
                policy,
                adaptiveSplitEnabled,
                depth + 1,
                cancellationToken);

            if (!rightResult.Succeeded)
            {
                return ServiceResult<AccountTransactionWindowFetchResult>.Fail(
                    rightResult.Error!.Message,
                    rightResult.Error.Code,
                    rightResult.Error.StatusCode);
            }

            var mergedTransactions = new List<TrueLayerTransactionRecord>();
            mergedTransactions.AddRange(leftResult.Value!.Transactions);
            mergedTransactions.AddRange(rightResult.Value!.Transactions);

            return ServiceResult<AccountTransactionWindowFetchResult>.Ok(
                new AccountTransactionWindowFetchResult(
                    Transactions: mergedTransactions,
                    SettledFetched: settledTransactions.Count + leftResult.Value.SettledFetched + rightResult.Value.SettledFetched,
                    PendingFetched: leftResult.Value.PendingFetched + rightResult.Value.PendingFetched,
                    PendingSucceededWindowCount: leftResult.Value.PendingSucceededWindowCount + rightResult.Value.PendingSucceededWindowCount,
                    PendingUnsupportedWindowCount: leftResult.Value.PendingUnsupportedWindowCount + rightResult.Value.PendingUnsupportedWindowCount,
                    PendingFailedWindowCount: leftResult.Value.PendingFailedWindowCount + rightResult.Value.PendingFailedWindowCount,
                    RequestWindowCount: 1 + leftResult.Value.RequestWindowCount + rightResult.Value.RequestWindowCount,
                    PotentiallyCappedWindowCount: 1 + leftResult.Value.PotentiallyCappedWindowCount + rightResult.Value.PotentiallyCappedWindowCount,
                    SettledFingerprint: ComputeSettledWindowFingerprint(settledTransactions)));
        }

        var pendingFetched = 0;
        var pendingSucceededWindowCount = 0;
        var pendingUnsupportedWindowCount = 0;
        var pendingFailedWindowCount = 0;
        var pendingTransactions = Array.Empty<TrueLayerTransactionRecord>();

        if (policy.PendingSupport == ProviderPendingSupportMode.Unsupported)
        {
            pendingUnsupportedWindowCount = 1;
            logger.LogInformation(
                "Pending account transactions fetch skipped by provider policy accountId={AccountId} providerId={ProviderId} policyKey={PolicyKey} policyFamily={PolicyFamily} fromUtc={FromUtc} toUtc={ToUtc}",
                providerAccount.AccountId,
                providerAccount.ProviderId ?? "<unknown>",
                policy.ProviderKey,
                policy.ProviderFamily,
                requestWindow.FromUtc,
                requestWindow.ToUtc);
        }
        else
        {
            var pendingResult = await dataService.GetPendingTransactionsAsync(
                configuration,
                accessToken,
                providerAccount.AccountId,
                requestWindow.FromUtc,
                requestWindow.ToUtc,
                cancellationToken);

            if (pendingResult.Succeeded)
            {
                pendingTransactions = pendingResult.Value!.ToArray();
                pendingFetched = pendingTransactions.Length;
                pendingSucceededWindowCount = 1;
            }
            else if (IsOptionalDatasetUnsupported(pendingResult.Error))
            {
                pendingUnsupportedWindowCount = 1;
                logger.LogInformation(
                    "Pending account transactions endpoint unavailable accountId={AccountId} providerId={ProviderId} depth={Depth} fromUtc={FromUtc} toUtc={ToUtc} status={StatusCode}",
                    providerAccount.AccountId,
                    providerAccount.ProviderId ?? "<unknown>",
                    depth,
                    requestWindow.FromUtc,
                    requestWindow.ToUtc,
                    pendingResult.Error?.StatusCode);
            }
            else
            {
                pendingFailedWindowCount = 1;
                logger.LogWarning(
                    "Pending account transactions fetch failed accountId={AccountId} providerId={ProviderId} depth={Depth} fromUtc={FromUtc} toUtc={ToUtc} status={StatusCode} code={Code}",
                    providerAccount.AccountId,
                    providerAccount.ProviderId ?? "<unknown>",
                    depth,
                    requestWindow.FromUtc,
                    requestWindow.ToUtc,
                    pendingResult.Error?.StatusCode,
                    pendingResult.Error?.Code);
            }
        }

        var transactions = new List<TrueLayerTransactionRecord>(settledTransactions.Count + pendingTransactions.Length);
        transactions.AddRange(settledTransactions);
        transactions.AddRange(pendingTransactions);

        return ServiceResult<AccountTransactionWindowFetchResult>.Ok(
            new AccountTransactionWindowFetchResult(
                Transactions: transactions,
                SettledFetched: settledTransactions.Count,
                PendingFetched: pendingFetched,
                PendingSucceededWindowCount: pendingSucceededWindowCount,
                PendingUnsupportedWindowCount: pendingUnsupportedWindowCount,
                PendingFailedWindowCount: pendingFailedWindowCount,
                RequestWindowCount: 1,
                PotentiallyCappedWindowCount: possibleCappedResponse ? 1 : 0,
                SettledFingerprint: ComputeSettledWindowFingerprint(settledTransactions)));
    }

    private static ProviderTransactionSyncPolicy ResolveProviderTransactionSyncPolicy(TrueLayerAccountRecord account)
    {
        return ProviderSyncPolicyCatalog.ResolveForAccount(account);
    }

    private static ProviderTransactionSyncPolicy ResolveProviderTransactionSyncPolicy(OpenBankingConnection connection)
    {
        return ProviderSyncPolicyCatalog.ResolveForConnection(connection.ProviderId, connection.ProviderDisplayName);
    }

    private static IReadOnlyList<TrueLayerTransactionQueryWindow> BuildTransactionRequestWindows(
        TrueLayerTransactionQueryWindow baseWindow,
        ProviderTransactionSyncPolicy policy,
        bool isInitialBackfill)
    {
        if (isInitialBackfill
            || !policy.SettledResponseCap.HasValue
            || !baseWindow.FromUtc.HasValue
            || !baseWindow.ToUtc.HasValue)
        {
            return [baseWindow];
        }

        if (baseWindow.FromUtc.Value >= baseWindow.ToUtc.Value)
        {
            return [baseWindow];
        }

        var windows = new List<TrueLayerTransactionQueryWindow>();
        var chunkSpan = TimeSpan.FromDays(Math.Max(1, policy.IncrementalChunkDays));
        var cursor = baseWindow.FromUtc.Value;
        var toUtc = baseWindow.ToUtc.Value;
        var chunkPolicy = $"{baseWindow.PolicyName ?? "incremental"}|chunk_{policy.IncrementalChunkDays}d";

        while (cursor < toUtc)
        {
            var next = cursor + chunkSpan;
            if (next > toUtc)
            {
                next = toUtc;
            }

            windows.Add(new TrueLayerTransactionQueryWindow(
                FromUtc: cursor,
                ToUtc: next,
                Mode: baseWindow.Mode,
                PolicyName: chunkPolicy));

            if (next <= cursor)
            {
                break;
            }

            cursor = next;
        }

        return windows.Count > 0 ? windows : [baseWindow];
    }

    private static IReadOnlyList<TrueLayerTransactionRecord> MergeFetchedTransactions(
        IEnumerable<TrueLayerTransactionRecord> transactions)
    {
        var byKey = new Dictionary<string, TrueLayerTransactionRecord>(StringComparer.Ordinal);
        foreach (var transaction in transactions.OrderBy(x => x.BookedAtUtc))
        {
            var key = BuildFetchedTransactionMergeKey(transaction);
            if (!byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = transaction;
                continue;
            }

            byKey[key] = PreferFetchedTransactionRecord(existing, transaction);
        }

        return byKey.Values
            .OrderBy(x => x.BookedAtUtc)
            .ToList();
    }

    private static string BuildFetchedTransactionMergeKey(TrueLayerTransactionRecord transaction)
    {
        if (!string.IsNullOrWhiteSpace(transaction.ProviderTransactionId))
        {
            return $"provider:{transaction.ProviderTransactionId}";
        }

        return $"dedupe:{transaction.DedupeKey}";
    }

    private static TrueLayerTransactionRecord PreferFetchedTransactionRecord(
        TrueLayerTransactionRecord existing,
        TrueLayerTransactionRecord incoming)
    {
        var existingBooked = IsBookedProjectionStatus(existing.TransactionStatus);
        var incomingBooked = IsBookedProjectionStatus(incoming.TransactionStatus);

        if (!existingBooked && incomingBooked)
        {
            return incoming;
        }

        if (existingBooked && !incomingBooked)
        {
            return existing;
        }

        if (incoming.BookedAtUtc > existing.BookedAtUtc)
        {
            return incoming;
        }

        return existing;
    }

    private static bool TrySplitWindow(
        TrueLayerTransactionQueryWindow window,
        TimeSpan minWindowDuration,
        out TrueLayerTransactionQueryWindow leftWindow,
        out TrueLayerTransactionQueryWindow rightWindow)
    {
        leftWindow = window;
        rightWindow = window;

        if (!window.FromUtc.HasValue || !window.ToUtc.HasValue)
        {
            return false;
        }

        var fromUtc = window.FromUtc.Value;
        var toUtc = window.ToUtc.Value;
        if (toUtc <= fromUtc)
        {
            return false;
        }

        var duration = toUtc - fromUtc;
        if (duration <= minWindowDuration)
        {
            return false;
        }

        var midpointUtc = fromUtc + TimeSpan.FromTicks(duration.Ticks / 2);
        if (midpointUtc <= fromUtc || midpointUtc >= toUtc)
        {
            return false;
        }

        leftWindow = new TrueLayerTransactionQueryWindow(
            FromUtc: fromUtc,
            ToUtc: midpointUtc,
            Mode: window.Mode,
            PolicyName: $"{window.PolicyName ?? "window"}|split_left");

        rightWindow = new TrueLayerTransactionQueryWindow(
            FromUtc: midpointUtc,
            ToUtc: toUtc,
            Mode: window.Mode,
            PolicyName: $"{window.PolicyName ?? "window"}|split_right");

        return true;
    }

    private static string ResolvePendingOutcome(
        int pendingSucceededWindows,
        int pendingUnsupportedWindows,
        int pendingFailedWindows)
    {
        if (pendingSucceededWindows == 0
            && pendingUnsupportedWindows == 0
            && pendingFailedWindows == 0)
        {
            return "not_requested";
        }

        if (pendingFailedWindows > 0 && pendingSucceededWindows == 0 && pendingUnsupportedWindows == 0)
        {
            return "failed";
        }

        if (pendingFailedWindows > 0)
        {
            return "partial_failed";
        }

        if (pendingUnsupportedWindows > 0 && pendingSucceededWindows > 0)
        {
            return "partial_supported";
        }

        if (pendingUnsupportedWindows > 0)
        {
            return "unsupported";
        }

        return "succeeded";
    }

    private static string ComputeSettledWindowFingerprint(IReadOnlyList<TrueLayerTransactionRecord> settledTransactions)
    {
        if (settledTransactions.Count == 0)
        {
            return "empty";
        }

        var hash = new HashCode();
        foreach (var token in settledTransactions
                     .Select(x =>
                         $"{x.ProviderTransactionId ?? "_"}:{x.DedupeKey}:{x.TransactionStatus ?? "_"}:{x.BookedAtUtc:O}:{x.Amount:0.00}")
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            hash.Add(token, StringComparer.Ordinal);
        }

        hash.Add(settledTransactions.Count);
        return hash.ToHashCode().ToString();
    }

    private async Task<bool> IsInitialBackfillPendingAsync(
        OpenBankingConnection connection,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (connection.InitialBackfillCompletedUtc.HasValue)
        {
            return false;
        }

        var linkedAccountIds = dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => x.ConnectionId == connection.Id)
            .Select(x => x.Id);

        var hasTransactions = await dbContext.RawBankTransactions
            .AsNoTracking()
            .AnyAsync(x => linkedAccountIds.Contains(x.LinkedBankAccountId), cancellationToken);

        if (!hasTransactions)
        {
            return true;
        }

        var earliestImportedUtc = await dbContext.RawBankTransactions
            .AsNoTracking()
            .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
            .Select(x => (DateTime?)x.BookedAtUtc)
            .MinAsync(cancellationToken);

        var latestImportedUtc = await dbContext.RawBankTransactions
            .AsNoTracking()
            .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
            .Select(x => (DateTime?)x.BookedAtUtc)
            .MaxAsync(cancellationToken);

        connection.InitialBackfillStartedUtc ??= connection.CreatedUtc;
        connection.InitialBackfillCompletedUtc ??= connection.LastSuccessfulSyncUtc ?? now;
        connection.InitialBackfillWindowStartUtc ??= earliestImportedUtc;
        connection.EarliestImportedTransactionUtc = MinUtc(connection.EarliestImportedTransactionUtc, earliestImportedUtc);
        connection.LatestImportedTransactionUtc = MaxUtc(connection.LatestImportedTransactionUtc, latestImportedUtc);
        return false;
    }

    private static TrueLayerTransactionQueryWindow BuildTransactionWindow(
        OpenBankingConnection connection,
        ProviderTransactionSyncPolicy providerPolicy,
        DateTime now,
        bool isInitialBackfill)
    {
        if (isInitialBackfill)
        {
            var windowStartUtc = now.AddDays(-providerPolicy.InitialBackfillHistoryDays);
            return new TrueLayerTransactionQueryWindow(
                windowStartUtc,
                now,
                Mode: "initial_backfill",
                PolicyName: providerPolicy.InitialBackfillPolicyName);
        }

        var checkpointUtc = connection.LatestImportedTransactionUtc ?? connection.LastSuccessfulSyncUtc;
        var fromUtc = checkpointUtc.HasValue
            ? checkpointUtc.Value.AddDays(-providerPolicy.IncrementalLookbackDays)
            : now.AddDays(-providerPolicy.IncrementalFallbackDays);

        if (fromUtc > now)
        {
            fromUtc = now.AddDays(-1);
        }

        var incrementalPolicyName = checkpointUtc.HasValue
            ? $"latest_imported_checkpoint_{providerPolicy.ProviderKey}"
            : providerPolicy.ReScanVisibleSliceEachSync
                ? $"provider_visible_slice_rescan_{providerPolicy.ProviderKey}"
                : "incremental_fallback";

        return new TrueLayerTransactionQueryWindow(
            fromUtc,
            now,
            Mode: "incremental_sync",
            PolicyName: incrementalPolicyName);
    }

    private static TrueLayerTransactionQueryWindow BuildCardTransactionWindow(
        OpenBankingConnection connection,
        ProviderTransactionSyncPolicy providerPolicy,
        DateTime now,
        bool isInitialBackfill)
    {
        if (isInitialBackfill)
        {
            return new TrueLayerTransactionQueryWindow(
                now.AddDays(-providerPolicy.CardInitialBackfillHistoryDays),
                now,
                Mode: "initial_backfill",
                PolicyName: $"{providerPolicy.InitialBackfillPolicyName}|card");
        }

        var checkpointUtc = connection.LatestImportedTransactionUtc ?? connection.LastSuccessfulSyncUtc;
        var fromUtc = checkpointUtc.HasValue
            ? checkpointUtc.Value.AddDays(-providerPolicy.IncrementalLookbackDays)
            : now.AddDays(-providerPolicy.IncrementalFallbackDays);

        if (fromUtc > now)
        {
            fromUtc = now.AddDays(-1);
        }

        return new TrueLayerTransactionQueryWindow(
            fromUtc,
            now,
            Mode: "incremental_sync",
            PolicyName: checkpointUtc.HasValue
                ? $"card_latest_imported_checkpoint_{providerPolicy.ProviderKey}"
                : $"card_incremental_fallback_{providerPolicy.ProviderKey}");
    }

    private static (DateTime? EarliestBookedAtUtc, DateTime? LatestBookedAtUtc) ExtractObservedTransactionBounds(
        IReadOnlyList<TrueLayerTransactionRecord> transactions)
    {
        if (transactions.Count == 0)
        {
            return (null, null);
        }

        DateTime? min = null;
        DateTime? max = null;

        foreach (var transaction in transactions)
        {
            min = MinUtc(min, transaction.BookedAtUtc);
            max = MaxUtc(max, transaction.BookedAtUtc);
        }

        return (min, max);
    }

    private static DateTime? MinUtc(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value <= right.Value ? left : right;
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

    private static string ResolveSyncReclassificationReason(
        string trigger,
        bool universeExpanded,
        bool remapOrDedupeCorrection)
    {
        if (universeExpanded)
        {
            return DeterministicReclassificationTriggerReasons.ConnectionCreatedUniverseExpansion;
        }

        if (remapOrDedupeCorrection)
        {
            return DeterministicReclassificationTriggerReasons.ProjectedRowRemapOrDedupeCorrection;
        }

        return trigger switch
        {
            "initial_sync" => DeterministicReclassificationTriggerReasons.SyncChangesInitialImport,
            "manual_sync" => DeterministicReclassificationTriggerReasons.SyncChangesManualRefresh,
            _ => DeterministicReclassificationTriggerReasons.SyncChangesAutoRefresh
        };
    }
}

