using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Banking.Services.Models;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Banking.Services;

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

    public async Task<ServiceResult<BankSyncResult>> SyncConnectionAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var configResult = configurationService.Resolve();
        if (!configResult.Succeeded)
        {
            return ServiceResult<BankSyncResult>.Fail(
                configResult.Error!.Message,
                configResult.Error.Code,
                configResult.Error.StatusCode);
        }

        var connectionResult = await bankConnectionService.GetConnectionForSyncAsync(userId, connectionId, cancellationToken);
        if (!connectionResult.Succeeded)
        {
            return ServiceResult<BankSyncResult>.Fail(
                connectionResult.Error!.Message,
                connectionResult.Error.Code,
                connectionResult.Error.StatusCode);
        }

        var connection = connectionResult.Value!;
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
        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.SyncPending,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        var accountsResult = await dataService.GetAccountsAsync(configuration, accessToken, cancellationToken);
        if (!accountsResult.Succeeded)
        {
            return await HandleSyncFailureAsync(connection, accountsResult.Error!, trigger, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var accountsSynced = 0;
        var balancesSynced = 0;
        var transactionsImported = 0;

        foreach (var providerAccount in accountsResult.Value!)
        {
            var linkedAccount = await UpsertLinkedAccountAsync(connection, providerAccount, now, cancellationToken);
            accountsSynced++;

            if (!string.IsNullOrWhiteSpace(providerAccount.ProviderId))
            {
                connection.ProviderConnectionReference = providerAccount.ProviderId;
            }

            if (!string.IsNullOrWhiteSpace(providerAccount.ProviderDisplayName))
            {
                connection.ProviderDisplayName = providerAccount.ProviderDisplayName;
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

            var transactionsResult = await dataService.GetTransactionsAsync(
                configuration,
                accessToken,
                providerAccount.AccountId,
                cancellationToken);

            if (!transactionsResult.Succeeded)
            {
                return await HandleSyncFailureAsync(connection, transactionsResult.Error!, trigger, cancellationToken);
            }

            var importedForAccount = await UpsertTransactionsAsync(
                linkedAccount,
                transactionsResult.Value!,
                now,
                cancellationToken);
            transactionsImported += importedForAccount;
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
                transactionsImported
            },
            cancellationToken);

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
}
