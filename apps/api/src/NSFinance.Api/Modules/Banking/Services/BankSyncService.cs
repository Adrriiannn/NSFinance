using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.Services.Models;
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
    private static readonly TimeSpan RevolutMaxHistoryWindow = TimeSpan.FromMinutes(5);
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
            return await HandleSyncFailureAsync(connection, accountsResult.Error!, trigger, cancellationToken);
        }

        var accountsSynced = 0;
        var balancesSynced = 0;
        var transactionsImported = 0;
        var cardsSynced = 0;
        var cardBalancesSynced = 0;
        var cardTransactionsImported = 0;
        var directDebitsSynced = 0;
        var standingOrdersSynced = 0;
        var identityInfoSynced = false;
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
                return await HandleSyncFailureAsync(connection, balanceResult.Error!, trigger, cancellationToken);
            }

            if (balanceResult.Value is not null)
            {
                dbContext.BankBalanceSnapshots.Add(new BankBalanceSnapshot
                {
                    Id = Guid.NewGuid(),
                    LinkedBankAccountId = linkedAccount.Id,
                    Available = balanceResult.Value.Available,
                    Current = balanceResult.Value.Current,
                    Overdraft = balanceResult.Value.Overdraft,
                    Currency = balanceResult.Value.Currency,
                    CapturedUtc = balanceResult.Value.CapturedAtUtc,
                    RawPayloadJson = balanceResult.Value.RawPayloadJson
                });
                balancesSynced++;
            }

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

            var transactionsResult = await dataService.GetTransactionsAsync(
                configuration,
                accessToken,
                providerAccount.AccountId,
                transactionWindow.FromUtc,
                transactionWindow.ToUtc,
                cancellationToken);

            if (!transactionsResult.Succeeded)
            {
                return await HandleSyncFailureAsync(connection, transactionsResult.Error!, trigger, cancellationToken);
            }

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

            var observedForAccount = ExtractObservedTransactionBounds(transactionsResult.Value!);
            syncObservedEarliestBookedAtUtc = MinUtc(syncObservedEarliestBookedAtUtc, observedForAccount.EarliestBookedAtUtc);
            syncObservedLatestBookedAtUtc = MaxUtc(syncObservedLatestBookedAtUtc, observedForAccount.LatestBookedAtUtc);

            var importedForAccount = await UpsertTransactionsAsync(
                linkedAccount,
                transactionsResult.Value!,
                now,
                cancellationToken);
            transactionsImported += importedForAccount;

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
                            cardTransactionsImported += await UpsertCardTransactionsAsync(
                                linkedCard,
                                cardTransactionsResult.Value!,
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
                cardsSynced,
                cardBalancesSynced,
                cardTransactionsImported,
                directDebitsSynced,
                standingOrdersSynced,
                identityInfoSynced,
                transactionMode = transactionSyncMode,
                requestedBackfillWindowStartUtc,
                observedEarliestTransactionUtc = syncObservedEarliestBookedAtUtc,
                observedLatestTransactionUtc = syncObservedLatestBookedAtUtc,
                initialBackfillCompletedUtc = connection.InitialBackfillCompletedUtc
            },
            cancellationToken);

        logger.LogInformation(
            "Bank sync completed for connectionId={ConnectionId} accountsSynced={AccountsSynced} balancesSynced={BalancesSynced} transactionsImported={TransactionsImported} cardsSynced={CardsSynced} cardBalancesSynced={CardBalancesSynced} cardTransactionsImported={CardTransactionsImported} directDebitsSynced={DirectDebitsSynced} standingOrdersSynced={StandingOrdersSynced} infoSynced={InfoSynced} transactionMode={TransactionMode} initialBackfillCompletedUtc={InitialBackfillCompletedUtc} earliestImportedUtc={EarliestImportedUtc} latestImportedUtc={LatestImportedUtc}",
            connection.Id,
            accountsSynced,
            balancesSynced,
            transactionsImported,
            cardsSynced,
            cardBalancesSynced,
            cardTransactionsImported,
            directDebitsSynced,
            standingOrdersSynced,
            identityInfoSynced,
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
                DateTime.UtcNow));
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

        linkedAccount.DisplayName = providerAccount.DisplayName;
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
                Name = providerAccount.DisplayName,
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
            linkedAccount.FinancialAccount.Name = providerAccount.DisplayName;
            linkedAccount.FinancialAccount.Type = MapAccountType(providerAccount.AccountType);
            linkedAccount.FinancialAccount.Currency = providerAccount.Currency;
        }

        return linkedAccount;
    }

    private async Task<int> UpsertTransactionsAsync(
        LinkedBankAccount linkedAccount,
        IReadOnlyList<TrueLayerTransactionRecord> providerTransactions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingProviderIds = await dbContext.RawBankTransactions
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id && x.ProviderTransactionId != null)
            .Select(x => x.ProviderTransactionId!)
            .ToHashSetAsync(cancellationToken);

        var existingDedupeKeys = await dbContext.RawBankTransactions
            .Where(x => x.LinkedBankAccountId == linkedAccount.Id)
            .Select(x => x.DedupeKey)
            .ToHashSetAsync(cancellationToken);

        var importedCount = 0;
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
            }

            if (!string.IsNullOrWhiteSpace(providerTransaction.ProviderTransactionId))
            {
                existingProviderIds.Add(providerTransaction.ProviderTransactionId);
            }

            existingDedupeKeys.Add(providerTransaction.DedupeKey);
            importedCount++;
        }

        return importedCount;
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

        var importedCount = 0;
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

            existingDedupeKeys.Add(providerTransaction.DedupeKey);
            importedCount++;
        }

        return importedCount;
    }

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

    private static bool IsOptionalDatasetUnsupported(ServiceError? error)
    {
        if (error is null)
        {
            return false;
        }

        return error.StatusCode is StatusCodes.Status400BadRequest
            or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound;
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
                status = nextStatus
            },
            cancellationToken);

        logger.LogWarning(
            "Bank sync failed for connectionId={ConnectionId} trigger={Trigger} code={Code}",
            connection.Id,
            trigger,
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
        if (isInitialBackfill)
        {
            var (historyDays, policyName) = ResolveInitialHistoryPolicy(account);
            var windowStartUtc = now.AddDays(-historyDays);
            return new TrueLayerTransactionQueryWindow(
                windowStartUtc,
                now,
                Mode: "initial_backfill",
                PolicyName: policyName);
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
            PolicyName: checkpointUtc.HasValue ? "latest_imported_checkpoint" : "incremental_fallback");
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

    private static (int HistoryDays, string PolicyName) ResolveInitialHistoryPolicy(TrueLayerAccountRecord account)
    {
        var providerId = (account.ProviderId ?? string.Empty).Trim().ToLowerInvariant();
        var providerDisplayName = (account.ProviderDisplayName ?? string.Empty).Trim().ToLowerInvariant();
        var providerComposite = $"{providerId} {providerDisplayName}";

        if (providerComposite.Contains("revolut", StringComparison.Ordinal))
        {
            return (365 * 6, "revolut_initial_6y");
        }

        if (providerComposite.Contains("ulster", StringComparison.Ordinal))
        {
            return (365 * 6, "ulster_initial_6y");
        }

        if (providerComposite.Contains("bank of ireland", StringComparison.Ordinal))
        {
            return (366, "boi_initial_1y");
        }

        if (providerComposite.Contains("permanent tsb", StringComparison.Ordinal)
            || providerComposite.Contains("ptsb", StringComparison.Ordinal))
        {
            return (95, "ptsb_initial_90d");
        }

        if (providerComposite.Contains("allied irish bank", StringComparison.Ordinal)
            || providerComposite.Contains(" aib", StringComparison.Ordinal)
            || providerId.StartsWith("aib", StringComparison.Ordinal))
        {
            return (366, "aib_initial_1y");
        }

        return (InitialBackfillDefaultDays, "default_initial_6y");
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

