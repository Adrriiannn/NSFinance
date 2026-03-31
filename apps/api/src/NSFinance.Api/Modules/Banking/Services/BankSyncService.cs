using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.ExpenseTracker.Services;
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
    ILogger<BankSyncService> logger)
{
    private const int IncrementalLookbackDays = 35;
    private const int IncrementalFallbackDays = 120;
    private const int InitialBackfillDefaultDays = 365 * 6;
    private const int InternalTransferMatchLookbackDays = 21;
    private const int InternalTransferMatchMaxWindowHours = 72;
    private const int InternalTransferMatchMinimumScore = 5;
    private static readonly TimeSpan RevolutMaxHistoryWindow = TimeSpan.FromMinutes(5);
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
    private static readonly HashSet<string> RequestedScopeSet = TrueLayerScopes.Default
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        CancellationToken cancellationToken)
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
            trigger: "manual_sync",
            cancellationToken);
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

        var accountsResult = await dataService.GetAccountsAsync(configuration, accessToken, cancellationToken);
        if (!accountsResult.Succeeded)
        {
            return await HandleSyncFailureAsync(connection, accountsResult.Error!, trigger, "accounts_refresh", cancellationToken);
        }

        var accountsSynced = 0;
        var balancesSynced = 0;
        var transactionsImported = 0;
        var cardsSynced = 0;
        var cardBalancesSynced = 0;
        var cardTransactionsImported = 0;
        var projectedTransactionsFromNewRaw = 0;
        var projectedTransactionsBackfilled = 0;
        var projectedTransactionsPromoted = 0;
        var projectedTransactionsSkippedUnbookedFetched = 0;
        var projectedTransactionsSkippedUnbookedBackfill = 0;
        var projectedTransactionsSkippedDuplicate = 0;
        var directDebitsSynced = 0;
        var standingOrdersSynced = 0;
        var identityInfoSynced = false;
        var linkedTransfersMatched = 0;
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

            var transactionWindow = BuildTransactionWindow(
                connection,
                providerAccount,
                now,
                isInitialBackfill);

            logger.LogInformation(
                "Fetching transactions accountId={AccountId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} mode={Mode} fromUtc={FromUtc} toUtc={ToUtc} policy={PolicyName}",
                providerAccount.AccountId,
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                transactionWindow.Mode,
                transactionWindow.FromUtc,
                transactionWindow.ToUtc,
                transactionWindow.PolicyName ?? "<none>");

            var transactionsFetchResult = await FetchAccountTransactionsAsync(
                configuration,
                accessToken,
                providerAccount,
                transactionWindow,
                isInitialBackfill,
                cancellationToken);

            if (!transactionsFetchResult.Succeeded)
            {
                return await HandleSyncFailureAsync(connection, transactionsFetchResult.Error!, trigger, "account_transactions_import", cancellationToken);
            }

            var fetchedTransactions = transactionsFetchResult.Value!;

            logger.LogInformation(
                "Fetched bank transactions accountId={AccountId} connectionId={ConnectionId} settledFetched={SettledFetched} pendingFetched={PendingFetched} pendingOutcome={PendingOutcome} totalFetched={TotalFetched} requestWindows={RequestWindows} potentiallyCappedWindows={PotentiallyCappedWindows} repeatedWindowPayloads={RepeatedWindowPayloads} earliestReturnedUtc={EarliestReturnedUtc} latestReturnedUtc={LatestReturnedUtc}",
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
                fetchedTransactions.LatestReturnedUtc);

            if (isInitialBackfill
                && string.Equals(transactionWindow.PolicyName, "revolut_initial_6y", StringComparison.Ordinal)
                && now - connection.CreatedUtc > RevolutMaxHistoryWindow)
            {
                logger.LogWarning(
                    "Revolut initial backfill window may be reduced because sync began after the first 5 minutes connectionId={ConnectionId} elapsedSeconds={ElapsedSeconds}",
                    connection.Id,
                    (now - connection.CreatedUtc).TotalSeconds);
            }

            requestedBackfillWindowStartUtc = MinUtc(requestedBackfillWindowStartUtc, transactionWindow.FromUtc);

            var observedForAccount = ExtractObservedTransactionBounds(fetchedTransactions.Transactions);
            syncObservedEarliestBookedAtUtc = MinUtc(syncObservedEarliestBookedAtUtc, observedForAccount.EarliestBookedAtUtc);
            syncObservedLatestBookedAtUtc = MaxUtc(syncObservedLatestBookedAtUtc, observedForAccount.LatestBookedAtUtc);

            var transactionUpsert = await UpsertTransactionsAsync(
                linkedAccount,
                providerAccount,
                fetchedTransactions.Transactions,
                now,
                cancellationToken);
            transactionsImported += transactionUpsert.RawInserted + transactionUpsert.RawUpdated;
            projectedTransactionsFromNewRaw += transactionUpsert.ProjectedFromNewRaw;
            projectedTransactionsPromoted += transactionUpsert.ProjectedFromStatusTransition;
            projectedTransactionsBackfilled += transactionUpsert.ProjectedBackfilled;
            projectedTransactionsSkippedUnbookedFetched += transactionUpsert.ProjectedSkippedUnbookedFetched;
            projectedTransactionsSkippedUnbookedBackfill += transactionUpsert.ProjectedSkippedUnbookedBackfill;
            projectedTransactionsSkippedDuplicate += transactionUpsert.ProjectedSkippedDuplicate;

            logger.LogInformation(
                "Bank transaction lifecycle accountId={AccountId} connectionId={ConnectionId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} fetched={Fetched} rawInserted={RawInserted} rawUpdated={RawUpdated} rawSkippedProviderId={RawSkippedProviderId} rawSkippedDedupe={RawSkippedDedupe} projectedFromNewRaw={ProjectedFromNewRaw} projectedFromStatusTransition={ProjectedFromStatusTransition} projectedBackfilled={ProjectedBackfilled} projectedSkippedUnbookedFetched={ProjectedSkippedUnbookedFetched} projectedSkippedUnbookedBackfill={ProjectedSkippedUnbookedBackfill} projectedSkippedDuplicate={ProjectedSkippedDuplicate}",
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
                transactionUpsert.ProjectedSkippedDuplicate);

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
        }

        if (ShouldRequestScope(connection.GrantedScopesCsv, "cards"))
        {
            var cardsResult = await dataService.GetCardsAsync(configuration, accessToken, cancellationToken);
            if (cardsResult.Succeeded)
            {
                cardsSupported = true;
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
                        var cardTransactionWindow = BuildCardTransactionWindow(connection, now, isInitialBackfill);
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

                            cardTransactionsImported += await UpsertCardTransactionsAsync(
                                linkedCard,
                                allCardTransactions,
                                now,
                                cancellationToken);
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

        linkedTransfersMatched = await MatchLinkedInternalTransfersAsync(connection.UserId, now, cancellationToken);
        if (linkedTransfersMatched > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var dataChanged =
            transactionsImported > 0
            || cardTransactionsImported > 0
            || projectedTransactionsPromoted > 0
            || projectedTransactionsBackfilled > 0
            || balancesSynced > 0
            || cardBalancesSynced > 0
            || linkedTransfersMatched > 0;

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.Synced,
            errorCode: null,
            errorReason: null,
            cancellationToken);

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
                cardsSynced,
                cardBalancesSynced,
                cardTransactionsImported,
                directDebitsSynced,
                standingOrdersSynced,
                identityInfoSynced,
                linkedTransfersMatched,
                dataChanged,
                transactionMode = transactionSyncMode,
                requestedBackfillWindowStartUtc,
                observedEarliestTransactionUtc = syncObservedEarliestBookedAtUtc,
                observedLatestTransactionUtc = syncObservedLatestBookedAtUtc,
                initialBackfillCompletedUtc = connection.InitialBackfillCompletedUtc
            },
            cancellationToken);

        logger.LogInformation(
            "Bank sync completed for connectionId={ConnectionId} accountsSynced={AccountsSynced} balancesSynced={BalancesSynced} rawTransactionsChanged={RawTransactionsChanged} projectedTransactionsFromNewRaw={ProjectedTransactionsFromNewRaw} projectedTransactionsPromoted={ProjectedTransactionsPromoted} projectedTransactionsBackfilled={ProjectedTransactionsBackfilled} projectedTransactionsSkippedUnbookedFetched={ProjectedTransactionsSkippedUnbookedFetched} projectedTransactionsSkippedUnbookedBackfill={ProjectedTransactionsSkippedUnbookedBackfill} projectedTransactionsSkippedDuplicate={ProjectedTransactionsSkippedDuplicate} cardsSynced={CardsSynced} cardBalancesSynced={CardBalancesSynced} cardTransactionsImported={CardTransactionsImported} directDebitsSynced={DirectDebitsSynced} standingOrdersSynced={StandingOrdersSynced} infoSynced={InfoSynced} linkedTransfersMatched={LinkedTransfersMatched} dataChanged={DataChanged} transactionMode={TransactionMode} initialBackfillCompletedUtc={InitialBackfillCompletedUtc} earliestImportedUtc={EarliestImportedUtc} latestImportedUtc={LatestImportedUtc}",
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
            cardsSynced,
            cardBalancesSynced,
            cardTransactionsImported,
            directDebitsSynced,
            standingOrdersSynced,
            identityInfoSynced,
            linkedTransfersMatched,
            dataChanged,
            transactionSyncMode,
            connection.InitialBackfillCompletedUtc,
            connection.EarliestImportedTransactionUtc,
            connection.LatestImportedTransactionUtc);

        return ServiceResult<BankSyncResult>.Ok(
            new BankSyncResult(
                connection.Id,
                accountsSynced,
                balancesSynced,
                transactionsImported,
                BankConnectionStatuses.Synced,
                DateTime.UtcNow,
                dataChanged));
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
        IReadOnlyList<TrueLayerTransactionRecord> providerTransactions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var fetchedCount = providerTransactions.Count;
        var existingRawRows = await dbContext.RawBankTransactions
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id)
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
        var projectionFingerprints = new HashSet<string>(StringComparer.Ordinal);

        if (linkedAccount.FinancialAccountId.HasValue)
        {
            var projectedAccountId = linkedAccount.FinancialAccountId.Value;
            projectionFingerprints = await dbContext.Transactions
                .Where(x => x.FinancialAccountId == projectedAccountId)
                .Select(x => CreateProjectionFingerprint(x.Amount, x.Currency, x.BookedAtUtc, x.Description))
                .ToHashSetAsync(cancellationToken);

            foreach (var row in existingRawRows)
            {
                if (!IsBookedProjectionStatus(row.TransactionStatus))
                {
                    projectedSkippedUnbookedBackfill++;
                    continue;
                }

                var projectionFingerprint = CreateProjectionFingerprint(
                    row.Amount,
                    row.Currency,
                    row.BookedAtUtc,
                    row.Description);

                if (!projectionFingerprints.Add(projectionFingerprint))
                {
                    projectedSkippedDuplicate++;
                    continue;
                }

                dbContext.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    FinancialAccountId = projectedAccountId,
                    Amount = row.Amount,
                    Currency = row.Currency,
                    Description = row.Description,
                    BookedAtUtc = row.BookedAtUtc,
                    CreatedUtc = now
                });
                projectedBackfilled++;
            }
        }

        foreach (var providerTransaction in providerTransactions)
        {
            logger.LogDebug(
                "Bank transaction normalization providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} sourceEndpoint={SourceEndpoint} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey} amount={Amount} currency={Currency} bookedAtUtc={BookedAtUtc} providerStatus={ProviderStatus} normalizedStatus={NormalizedStatus} normalizationReason={NormalizationReason}",
                providerAccount.ProviderId ?? "<unknown>",
                providerAccount.ProviderDisplayName ?? "<unknown>",
                linkedAccount.ConnectionId,
                providerAccount.AccountId,
                providerTransaction.SourceEndpoint,
                providerTransaction.ProviderTransactionId ?? "<none>",
                providerTransaction.DedupeKey,
                providerTransaction.Amount,
                providerTransaction.Currency,
                providerTransaction.BookedAtUtc,
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
                var changed = ApplyRawTransactionUpdate(existingRaw, providerTransaction, now);
                if (changed)
                {
                    rawUpdated++;
                }
                else if (matchedByProviderId)
                {
                    rawSkippedProviderId++;
                }
                else
                {
                    rawSkippedDedupe++;
                }

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
                if (!wasBooked
                    && isNowBooked
                    && linkedAccount.FinancialAccountId.HasValue)
                {
                    var projectionFingerprint = CreateProjectionFingerprint(
                        existingRaw.Amount,
                        existingRaw.Currency,
                        existingRaw.BookedAtUtc,
                        existingRaw.Description);

                    if (projectionFingerprints.Add(projectionFingerprint))
                    {
                        dbContext.Transactions.Add(new Transaction
                        {
                            Id = Guid.NewGuid(),
                            FinancialAccountId = linkedAccount.FinancialAccountId.Value,
                            Amount = existingRaw.Amount,
                            Currency = existingRaw.Currency,
                            Description = existingRaw.Description,
                            BookedAtUtc = existingRaw.BookedAtUtc,
                            CreatedUtc = now
                        });
                        projectedFromStatusTransition++;
                        logger.LogDebug(
                            "Bank transaction projected after status transition providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey} fromStatus={FromStatus} toStatus={ToStatus}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            existingRaw.ProviderTransactionId ?? "<none>",
                            existingRaw.DedupeKey,
                            wasBooked ? "booked" : "unbooked",
                            isNowBooked ? "booked" : "unbooked");
                    }
                    else
                    {
                        projectedSkippedDuplicate++;
                        logger.LogDebug(
                            "Bank transaction projection skipped due to duplicate fingerprint after status transition providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            existingRaw.ProviderTransactionId ?? "<none>",
                            existingRaw.DedupeKey);
                    }
                }
                else if (!isNowBooked)
                {
                    projectedSkippedUnbookedFetched++;
                    logger.LogDebug(
                        "Bank transaction projection skipped because normalized status is unbooked providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey} normalizedStatus={NormalizedStatus} sourceEndpoint={SourceEndpoint} providerStatus={ProviderStatus} normalizationReason={NormalizationReason}",
                        providerAccount.ProviderId ?? "<unknown>",
                        providerAccount.ProviderDisplayName ?? "<unknown>",
                        linkedAccount.ConnectionId,
                        providerAccount.AccountId,
                        providerTransaction.ProviderTransactionId ?? "<none>",
                        providerTransaction.DedupeKey,
                        providerTransaction.TransactionStatus ?? "<null>",
                        providerTransaction.SourceEndpoint,
                        providerTransaction.ProviderStatus ?? "<none>",
                        providerTransaction.StatusNormalizationReason);
                }

                continue;
            }

            var rawTransaction = new RawBankTransaction
            {
                Id = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
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
            };
            dbContext.RawBankTransactions.Add(rawTransaction);

            if (linkedAccount.FinancialAccountId.HasValue)
            {
                if (IsBookedProjectionStatus(providerTransaction.TransactionStatus))
                {
                    var projectionFingerprint = CreateProjectionFingerprint(
                        providerTransaction.Amount,
                        providerTransaction.Currency,
                        providerTransaction.BookedAtUtc,
                        providerTransaction.Description);

                    if (projectionFingerprints.Add(projectionFingerprint))
                    {
                        dbContext.Transactions.Add(new Transaction
                        {
                            Id = Guid.NewGuid(),
                            FinancialAccountId = linkedAccount.FinancialAccountId.Value,
                            Amount = providerTransaction.Amount,
                            Currency = providerTransaction.Currency,
                            Description = providerTransaction.Description,
                            BookedAtUtc = providerTransaction.BookedAtUtc,
                            CreatedUtc = now
                        });
                        projectedFromNewRaw++;
                        logger.LogDebug(
                            "Bank transaction projected from new raw row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            providerTransaction.ProviderTransactionId ?? "<none>",
                            providerTransaction.DedupeKey);
                    }
                    else
                    {
                        projectedSkippedDuplicate++;
                        logger.LogDebug(
                            "Bank transaction projection skipped due to duplicate fingerprint providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey}",
                            providerAccount.ProviderId ?? "<unknown>",
                            providerAccount.ProviderDisplayName ?? "<unknown>",
                            linkedAccount.ConnectionId,
                            providerAccount.AccountId,
                            providerTransaction.ProviderTransactionId ?? "<none>",
                            providerTransaction.DedupeKey);
                    }
                }
                else
                {
                    projectedSkippedUnbookedFetched++;
                    logger.LogDebug(
                        "Bank transaction projection skipped because normalized status is unbooked for new raw row providerId={ProviderId} providerDisplayName={ProviderDisplayName} connectionId={ConnectionId} accountId={AccountId} providerTransactionId={ProviderTransactionId} dedupeKey={DedupeKey} normalizedStatus={NormalizedStatus} sourceEndpoint={SourceEndpoint} providerStatus={ProviderStatus} normalizationReason={NormalizationReason}",
                        providerAccount.ProviderId ?? "<unknown>",
                        providerAccount.ProviderDisplayName ?? "<unknown>",
                        linkedAccount.ConnectionId,
                        providerAccount.AccountId,
                        providerTransaction.ProviderTransactionId ?? "<none>",
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
            projectedSkippedDuplicate);
    }

    private static bool ApplyRawTransactionUpdate(
        RawBankTransaction existing,
        TrueLayerTransactionRecord incoming,
        DateTime importedUtc)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(incoming.ProviderTransactionId)
            && !string.Equals(existing.ProviderTransactionId, incoming.ProviderTransactionId, StringComparison.Ordinal))
        {
            existing.ProviderTransactionId = incoming.ProviderTransactionId;
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

    private async Task<int> UpsertCardTransactionsAsync(
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

                dbContext.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    FinancialAccountId = projectedFinancialAccountId.Value,
                    Amount = row.Amount,
                    Currency = row.Currency,
                    Description = row.Description,
                    BookedAtUtc = row.BookedAtUtc,
                    CreatedUtc = now
                });
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
                        dbContext.Transactions.Add(new Transaction
                        {
                            Id = Guid.NewGuid(),
                            FinancialAccountId = projectedFinancialAccountId.Value,
                            Amount = providerTransaction.Amount,
                            Currency = providerTransaction.Currency,
                            Description = providerTransaction.Description,
                            BookedAtUtc = providerTransaction.BookedAtUtc,
                            CreatedUtc = now
                        });
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

        return importedCount;
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
        int ProjectedSkippedDuplicate);

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

    private async Task<int> MatchLinkedInternalTransfersAsync(
        Guid userId,
        DateTime now,
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

        var accountHintTokensByFinancialAccountId = new Dictionary<Guid, HashSet<string>>();
        foreach (var row in accountHintRows)
        {
            var tokens = BuildInternalTransferAccountHintTokens(
                row.DisplayName,
                row.ProviderDisplayName,
                row.ProviderId);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (!accountHintTokensByFinancialAccountId.TryGetValue(row.FinancialAccountId, out var current))
            {
                current = new HashSet<string>(StringComparer.Ordinal);
                accountHintTokensByFinancialAccountId[row.FinancialAccountId] = current;
            }

            current.UnionWith(tokens);
        }

        var windowStartUtc = now.AddDays(-InternalTransferMatchLookbackDays);

        var candidates = await dbContext.Transactions
            .Where(x =>
                linkedFinancialAccountIds.Contains(x.FinancialAccountId)
                && x.BookedAtUtc >= windowStartUtc
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
            .Where(x => x.Amount < 0m && IsAutoMatchEligible(x))
            .OrderBy(x => x.BookedAtUtc)
            .ToList();
        var incoming = candidates
            .Where(x => x.Amount > 0m && IsAutoMatchEligible(x))
            .OrderBy(x => x.BookedAtUtc)
            .ToList();

        if (outgoing.Count == 0 || incoming.Count == 0)
        {
            return 0;
        }

        var incomingByKey = incoming
            .GroupBy(CreateInternalTransferAmountCurrencyKey)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var usedIncomingIds = new HashSet<Guid>();
        var matchedPairs = 0;
        var unmatchedNoAmountCurrencyKey = 0;
        var unmatchedNoCandidatesAfterFilters = 0;
        var unmatchedNoTransferEvidence = 0;
        var unmatchedBelowThreshold = 0;

        foreach (var debit in outgoing)
        {
            var key = CreateInternalTransferAmountCurrencyKey(debit);
            if (!incomingByKey.TryGetValue(key, out var incomingCandidates))
            {
                unmatchedNoAmountCurrencyKey++;
                continue;
            }

            Transaction? bestCredit = null;
            var bestScore = int.MinValue;
            var bestTimeDistance = TimeSpan.MaxValue;
            var hadEligibleCounterpartyCandidate = false;
            var hadTransferEvidenceCandidate = false;

            foreach (var credit in incomingCandidates)
            {
                if (usedIncomingIds.Contains(credit.Id) || credit.FinancialAccountId == debit.FinancialAccountId)
                {
                    continue;
                }

                hadEligibleCounterpartyCandidate = true;
                var scoreResult = ScoreInternalTransferPair(
                    debit,
                    credit,
                    accountHintTokensByFinancialAccountId);
                if (scoreResult.HasTransferEvidence)
                {
                    hadTransferEvidenceCandidate = true;
                }

                var score = scoreResult.Score;
                var distance = scoreResult.Distance;
                if (score < InternalTransferMatchMinimumScore)
                {
                    continue;
                }

                if (score > bestScore || (score == bestScore && distance < bestTimeDistance))
                {
                    bestCredit = credit;
                    bestScore = score;
                    bestTimeDistance = distance;
                }
            }

            if (bestCredit is null)
            {
                if (!hadEligibleCounterpartyCandidate)
                {
                    unmatchedNoCandidatesAfterFilters++;
                }
                else if (!hadTransferEvidenceCandidate)
                {
                    unmatchedNoTransferEvidence++;
                }
                else
                {
                    unmatchedBelowThreshold++;
                }
                continue;
            }

            ApplyLinkedInternalTransferPair(debit, bestCredit, now);
            usedIncomingIds.Add(bestCredit.Id);
            matchedPairs++;
        }

        if (matchedPairs > 0)
        {
            logger.LogInformation(
                "Matched linked internal transfers userId={UserId} matchedPairs={MatchedPairs} lookbackDays={LookbackDays} outgoingCandidates={OutgoingCandidates} incomingCandidates={IncomingCandidates} unmatchedNoAmountCurrencyKey={UnmatchedNoAmountCurrencyKey} unmatchedNoCandidatesAfterFilters={UnmatchedNoCandidatesAfterFilters} unmatchedNoTransferEvidence={UnmatchedNoTransferEvidence} unmatchedBelowThreshold={UnmatchedBelowThreshold}",
                userId,
                matchedPairs,
                InternalTransferMatchLookbackDays,
                outgoing.Count,
                incoming.Count,
                unmatchedNoAmountCurrencyKey,
                unmatchedNoCandidatesAfterFilters,
                unmatchedNoTransferEvidence,
                unmatchedBelowThreshold);
        }
        else
        {
            logger.LogInformation(
                "No linked internal transfer pairs matched userId={UserId} lookbackDays={LookbackDays} outgoingCandidates={OutgoingCandidates} incomingCandidates={IncomingCandidates} unmatchedNoAmountCurrencyKey={UnmatchedNoAmountCurrencyKey} unmatchedNoCandidatesAfterFilters={UnmatchedNoCandidatesAfterFilters} unmatchedNoTransferEvidence={UnmatchedNoTransferEvidence} unmatchedBelowThreshold={UnmatchedBelowThreshold}",
                userId,
                InternalTransferMatchLookbackDays,
                outgoing.Count,
                incoming.Count,
                unmatchedNoAmountCurrencyKey,
                unmatchedNoCandidatesAfterFilters,
                unmatchedNoTransferEvidence,
                unmatchedBelowThreshold);
        }

        return matchedPairs;
    }

    private static bool IsAutoMatchEligible(Transaction transaction)
    {
        if (transaction.TransferKind == TransactionTransferKind.Manual)
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
        IReadOnlyDictionary<Guid, HashSet<string>> accountHintTokensByFinancialAccountId)
    {
        var debitLooksTransfer = LooksLikeInternalTransferDescription(debit.Description);
        var creditLooksTransfer = LooksLikeInternalTransferDescription(credit.Description);
        var hasTransferHint = debitLooksTransfer || creditLooksTransfer;
        var hasTransferTaxonomyHint =
            debit.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId
            || credit.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId;
        var hasCounterpartyAccountHint =
            DescriptionContainsCounterpartyAccountHint(
                debit.Description,
                credit.FinancialAccountId,
                accountHintTokensByFinancialAccountId)
            || DescriptionContainsCounterpartyAccountHint(
                credit.Description,
                debit.FinancialAccountId,
                accountHintTokensByFinancialAccountId);
        var hasSharedTransferToken = HasSharedTransferToken(debit.Description, credit.Description);
        var hasTransferEvidence =
            hasTransferHint
            || hasTransferTaxonomyHint
            || hasCounterpartyAccountHint
            || (hasSharedTransferToken && (hasTransferHint || hasCounterpartyAccountHint));

        var distance = (debit.BookedAtUtc - credit.BookedAtUtc).Duration();
        if (distance.TotalHours > InternalTransferMatchMaxWindowHours)
        {
            return new InternalTransferPairScore(int.MinValue, distance, hasTransferEvidence);
        }

        if (!hasTransferEvidence)
        {
            return new InternalTransferPairScore(int.MinValue, distance, HasTransferEvidence: false);
        }

        var score = 0;
        if (distance.TotalHours <= 2)
        {
            score += 5;
        }
        else if (distance.TotalHours <= 12)
        {
            score += 4;
        }
        else if (distance.TotalHours <= 24)
        {
            score += 3;
        }
        else
        {
            score += 2;
        }

        if (hasTransferHint)
        {
            score += 2;
        }

        if (hasCounterpartyAccountHint)
        {
            score += 2;
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

        return new InternalTransferPairScore(score, distance, hasTransferEvidence);
    }

    private static bool DescriptionContainsCounterpartyAccountHint(
        string description,
        Guid counterpartyFinancialAccountId,
        IReadOnlyDictionary<Guid, HashSet<string>> accountHintTokensByFinancialAccountId)
    {
        if (!accountHintTokensByFinancialAccountId.TryGetValue(counterpartyFinancialAccountId, out var hintTokens)
            || hintTokens.Count == 0)
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
            if (hintTokens.Contains(token))
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
            || normalized.Contains("cash fund", StringComparison.Ordinal)
            || normalized.Contains("vault", StringComparison.Ordinal)
            || normalized.Contains("saving", StringComparison.Ordinal)
            || normalized.Contains("move money", StringComparison.Ordinal)
            || normalized.Contains("bank to bank", StringComparison.Ordinal)
            || normalized.Contains("internal", StringComparison.Ordinal);
    }

    private readonly record struct InternalTransferPairScore(
        int Score,
        TimeSpan Distance,
        bool HasTransferEvidence);

    private static void ApplyLinkedInternalTransferPair(Transaction debit, Transaction credit, DateTime now)
    {
        debit.TransferKind = TransactionTransferKind.LinkedInternal;
        debit.LinkedTransferTransactionId = credit.Id;
        debit.LinkedTransferMatchedUtc = now;
        ApplyAutoTransferTaxonomy(debit);

        credit.TransferKind = TransactionTransferKind.LinkedInternal;
        credit.LinkedTransferTransactionId = debit.Id;
        credit.LinkedTransferMatchedUtc = now;
        ApplyAutoTransferTaxonomy(credit);
    }

    private static void ResetLinkedTransferState(Transaction transaction)
    {
        transaction.LinkedTransferTransactionId = null;
        transaction.LinkedTransferMatchedUtc = null;

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

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            nextStatus,
            error.Code,
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
                error.Code,
                status = nextStatus,
                stage = stageName
            },
            cancellationToken);

        logger.LogWarning(
            "Bank sync failed for connectionId={ConnectionId} trigger={Trigger} stage={Stage} code={Code}",
            connection.Id,
            trigger,
            stageName,
            error.Code);

        return ServiceResult<BankSyncResult>.Fail(error.Message, error.Code, error.StatusCode);
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
        var candidate = NormalizeLabel(providerAccount.DisplayName);
        if (!string.IsNullOrWhiteSpace(candidate)
            && !LooksLikeConnectedIdentity(candidate, connection.IdentityInfo?.FullName))
        {
            return candidate;
        }

        return BuildAccountFallbackLabel(providerAccount.Currency, providerAccount.AccountType);
    }

    private static string BuildAccountFallbackLabel(string currency, string? accountType)
    {
        var resolvedCurrency = string.IsNullOrWhiteSpace(currency)
            ? "EUR"
            : currency.Trim().ToUpperInvariant();
        var friendlyType = ResolveFriendlyAccountType(accountType);
        return $"{resolvedCurrency} {friendlyType}";
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

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Persisted banking sync stage connectionId={ConnectionId} accountId={AccountId} stage={Stage}",
            connectionId,
            accountId ?? "<none>",
            stageName);
    }

    private async Task<ServiceResult<AccountTransactionFetchResult>> FetchAccountTransactionsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        TrueLayerAccountRecord providerAccount,
        TrueLayerTransactionQueryWindow baseWindow,
        bool isInitialBackfill,
        CancellationToken cancellationToken)
    {
        var policy = ResolveProviderTransactionSyncPolicy(providerAccount);
        var requestWindows = BuildTransactionRequestWindows(baseWindow, policy, isInitialBackfill);
        var adaptiveSplitEnabled = !isInitialBackfill && policy.SettledResponseCap.HasValue;

        logger.LogInformation(
            "Transaction sync strategy accountId={AccountId} providerId={ProviderId} providerDisplayName={ProviderDisplayName} mode={Mode} policy={PolicyName} requestWindows={RequestWindows} adaptiveSplitEnabled={AdaptiveSplitEnabled} settledResponseCap={SettledResponseCap}",
            providerAccount.AccountId,
            providerAccount.ProviderId ?? "<unknown>",
            providerAccount.ProviderDisplayName ?? "<unknown>",
            baseWindow.Mode,
            baseWindow.PolicyName ?? "<none>",
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
        TrueLayerAccountRecord account,
        DateTime now,
        bool isInitialBackfill)
    {
        var providerPolicy = ResolveProviderTransactionSyncPolicy(account);

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
        DateTime now,
        bool isInitialBackfill)
    {
        if (isInitialBackfill)
        {
            var historyDays = ResolveCardInitialHistoryDays(connection);
            return new TrueLayerTransactionQueryWindow(
                now.AddDays(-historyDays),
                now,
                Mode: "initial_backfill",
                PolicyName: "card_initial_backfill");
        }

        var checkpointUtc = connection.LatestImportedTransactionUtc ?? connection.LastSuccessfulSyncUtc;
        var fromUtc = checkpointUtc.HasValue
            ? checkpointUtc.Value.AddDays(-IncrementalLookbackDays)
            : now.AddDays(-IncrementalFallbackDays);

        if (fromUtc > now)
        {
            fromUtc = now.AddDays(-1);
        }

        return new TrueLayerTransactionQueryWindow(
            fromUtc,
            now,
            Mode: "incremental_sync",
            PolicyName: checkpointUtc.HasValue ? "card_latest_imported_checkpoint" : "card_incremental_fallback");
    }

    private static int ResolveCardInitialHistoryDays(OpenBankingConnection connection)
    {
        var provider = $"{connection.ProviderId} {connection.ProviderDisplayName}".ToLowerInvariant();
        if (provider.Contains("revolut", StringComparison.Ordinal)
            || provider.Contains("ulster", StringComparison.Ordinal))
        {
            return 365 * 6;
        }

        if (provider.Contains("bank of ireland", StringComparison.Ordinal))
        {
            return 366;
        }

        if (provider.Contains("ptsb", StringComparison.Ordinal)
            || provider.Contains("permanent tsb", StringComparison.Ordinal))
        {
            return 95;
        }

        return InitialBackfillDefaultDays;
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
}

