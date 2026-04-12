using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Validators;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class TrueLayerAuthService(
    TrueLayerConfigurationService configurationService,
    BankConnectionService bankConnectionService,
    TrueLayerTokenService tokenService,
    BankSyncService bankSyncService,
    ITrueLayerSyncQueue trueLayerSyncQueue,
    IAuditService auditService,
    ILogger<TrueLayerAuthService> logger)
{
    public async Task<ServiceResult<StartTrueLayerLinkResponse>> StartLinkAsync(
        Guid userId,
        string? appReturnUri,
        Guid? reconnectConnectionId,
        CancellationToken cancellationToken)
    {
        var configResult = configurationService.Resolve();
        if (!configResult.Succeeded)
        {
            return ServiceResult<StartTrueLayerLinkResponse>.Fail(
                configResult.Error!.Message,
                configResult.Error.Code,
                configResult.Error.StatusCode);
        }

        var configuration = configResult.Value!;
        var normalizedAppReturnUri = TrueLayerReturnUriContract.Normalize(appReturnUri);
        OpenBankingConnection connection;
        if (reconnectConnectionId.HasValue)
        {
            var reconnectResult = await bankConnectionService.PrepareConnectionReconfirmAsync(
                userId,
                reconnectConnectionId.Value,
                configuration.Environment,
                cancellationToken);

            if (!reconnectResult.Succeeded)
            {
                return ServiceResult<StartTrueLayerLinkResponse>.Fail(
                    reconnectResult.Error!.Message,
                    reconnectResult.Error.Code,
                    reconnectResult.Error.StatusCode);
            }

            connection = reconnectResult.Value!;
        }
        else
        {
            connection = await bankConnectionService.CreateConnectionStartedAsync(
                userId,
                BankingProviders.TrueLayer,
                configuration.Environment,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(normalizedAppReturnUri) && !string.IsNullOrWhiteSpace(connection.AuthStateNonce))
        {
            var callbackState = BuildCallbackState(connection.AuthStateNonce, normalizedAppReturnUri);
            await bankConnectionService.UpdateAuthStateAsync(
                connection,
                callbackState,
                connection.AuthStateExpiresUtc,
                cancellationToken);
        }

        var scopes = BuildScopes();
        var providers = BuildProviders(configuration.Environment);
        var countryId = BuildCountryId(configuration.Environment);
        logger.LogInformation(
            "TrueLayer link started connectionId={ConnectionId} userId={UserId} environment={Environment} reconnectRequested={ReconnectRequested} hasAppReturnUri={HasAppReturnUri} normalizedAppReturnUri={AppReturnUri} hasProviders={HasProviders} providers={Providers} hasCountryId={HasCountryId} countryId={CountryId}",
            connection.Id,
            userId,
            configuration.Environment,
            reconnectConnectionId.HasValue,
            !string.IsNullOrWhiteSpace(normalizedAppReturnUri),
            normalizedAppReturnUri ?? "<none>",
            providers.Count > 0,
            providers.Count > 0 ? string.Join(' ', providers) : "<none>",
            !string.IsNullOrWhiteSpace(countryId),
            countryId ?? "<none>");

        var authLink = BuildAuthorizationLink(
            configuration.AuthBaseUrl,
            configuration.ClientId,
            configuration.RedirectUri,
            connection.AuthStateNonce!,
            scopes,
            providers,
            countryId);

        return ServiceResult<StartTrueLayerLinkResponse>.Ok(
            new StartTrueLayerLinkResponse(
                connection.Id,
                BankingProviders.TrueLayer,
                configuration.Environment,
                authLink,
                scopes,
                connection.AuthStateExpiresUtc ?? DateTime.UtcNow.AddMinutes(15)));
    }

    public async Task<TrueLayerCallbackOutcome> HandleCallbackAsync(
        TrueLayerCallbackQuery query,
        CancellationToken cancellationToken)
    {
        var errors = TrueLayerCallbackQueryValidator.Validate(query);
        if (errors.Count > 0)
        {
            return new TrueLayerCallbackOutcome(
                false,
                "callback_query_invalid",
                "The callback query is invalid. Please restart the bank connection flow.",
                StatusCodes.Status400BadRequest,
                null,
                null,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "callback_query_invalid");
        }

        var appReturnUri = ExtractAppReturnUri(query.State);
        var connection = await bankConnectionService.FindConnectionByStateAsync(query.State!, cancellationToken);
        if (connection is null)
        {
            logger.LogWarning("TrueLayer callback rejected because state was invalid or expired.");
            return new TrueLayerCallbackOutcome(
                false,
                "callback_state_invalid",
                "This callback has already been handled or expired. Return to NSFinance to continue.",
                StatusCodes.Status400BadRequest,
                null,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                CallbackLifecycleReason: "callback_state_invalid_or_already_consumed");
        }

        logger.LogInformation(
            "TrueLayer callback received for connectionId={ConnectionId} hasCode={HasCode} hasError={HasError}",
            connection.Id,
            !string.IsNullOrWhiteSpace(query.Code),
            !string.IsNullOrWhiteSpace(query.Error));

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "bank_connection_callback_received",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new
            {
                hasCode = !string.IsNullOrWhiteSpace(query.Code),
                hasError = !string.IsNullOrWhiteSpace(query.Error)
            },
            cancellationToken);

        connection.AuthStateNonce = null;
        connection.AuthStateExpiresUtc = null;

        logger.LogInformation(
            "TrueLayer callback state consumed for connectionId={ConnectionId}",
            connection.Id);

        if (!string.IsNullOrWhiteSpace(query.Error))
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                query.Error,
                query.ErrorDescription,
                cancellationToken);

            await auditService.WriteEventAsync(
                category: "banking",
                eventName: "token_exchange_failure",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: connection.UserId,
                actorType: "user",
                metadata: new { error = query.Error },
                cancellationToken);

            return new TrueLayerCallbackOutcome(
                false,
                "callback_provider_error",
                "Bank consent was not completed. Please reconnect from the app.",
                StatusCodes.Status400BadRequest,
                connection.Id,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "provider_declined_or_cancelled");
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

            return new TrueLayerCallbackOutcome(
                false,
                configResult.Error!.Code,
                "Banking provider configuration is incomplete. Contact support.",
                configResult.Error.StatusCode,
                connection.Id,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "provider_configuration_unavailable");
        }

        if (!string.Equals(connection.ProviderEnvironment, configResult.Value!.Environment, StringComparison.OrdinalIgnoreCase))
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                "truelayer_environment_mismatch",
                "Connection environment does not match server configuration.",
                cancellationToken);

            return new TrueLayerCallbackOutcome(
                false,
                "truelayer_environment_mismatch",
                "Environment mismatch detected. Restart the connect flow in the active environment.",
                StatusCodes.Status409Conflict,
                connection.Id,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "environment_mismatch");
        }

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.ConsentInProgress,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        var tokenResult = await tokenService.ExchangeAuthorizationCodeAsync(
            configResult.Value,
            query.Code!,
            cancellationToken);
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
                eventName: "token_exchange_failure",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: connection.UserId,
                actorType: "user",
                metadata: new
                {
                    code = tokenResult.Error?.Code,
                    status = nextStatus
                },
                cancellationToken);

            logger.LogWarning(
                "TrueLayer callback token exchange failed connectionId={ConnectionId} code={Code}",
                connection.Id,
                tokenResult.Error?.Code);

            return new TrueLayerCallbackOutcome(
                false,
                tokenResult.Error!.Code,
                "Authorization code could not be exchanged. Please reconnect from the app.",
                tokenResult.Error.StatusCode,
                connection.Id,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "token_exchange_failed");
        }

        logger.LogInformation(
            "TrueLayer token exchange succeeded for connectionId={ConnectionId}",
            connection.Id);

        var persistResult = await bankSyncService.PersistTokenAsync(
            connection,
            tokenResult.Value!,
            cancellationToken);
        if (!persistResult.Succeeded)
        {
            logger.LogError(
                "TrueLayer token persistence failed for connectionId={ConnectionId} code={Code}",
                connection.Id,
                persistResult.Error?.Code);

            return new TrueLayerCallbackOutcome(
                false,
                persistResult.Error!.Code,
                "Bank linked, but secure token storage failed. Please reconnect from the app.",
                persistResult.Error.StatusCode,
                connection.Id,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "token_persistence_failed");
        }

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.ConnectedPendingSync,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        logger.LogInformation(
            "Bank connection marked as {Status} for connectionId={ConnectionId}",
            BankConnectionStatuses.ConnectedPendingSync,
            connection.Id);

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "token_exchange_success",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new { provider = connection.ProviderName },
            cancellationToken);

        try
        {
            await trueLayerSyncQueue.QueueInitialSyncAsync(connection.UserId, connection.Id, cancellationToken);
            logger.LogInformation(
                "TrueLayer callback queued initial sync for connectionId={ConnectionId}",
                connection.Id);

            await auditService.WriteEventAsync(
                category: "banking",
                eventName: "initial_sync_queued",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: connection.UserId,
                actorType: "user",
                metadata: new { connectionId = connection.Id },
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "TrueLayer callback could not queue initial sync for connectionId={ConnectionId}",
                connection.Id);

            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                "initial_sync_queue_unavailable",
                "Bank linked, but automatic sync could not be queued. Open the app and run sync manually.",
                cancellationToken);

            await auditService.WriteEventAsync(
                category: "banking",
                eventName: "initial_sync_queue_failed",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: connection.UserId,
                actorType: "user",
                metadata: new
                {
                    connectionId = connection.Id,
                    status = BankConnectionStatuses.Failed
                },
                cancellationToken);

            return new TrueLayerCallbackOutcome(
                true,
                "initial_sync_queue_unavailable",
                "Bank linked successfully. Return to the app and tap Sync now to complete the first import.",
                StatusCodes.Status200OK,
                connection.Id,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: true,
                CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                CallbackLifecycleReason: "initial_sync_queue_unavailable");
        }

        return new TrueLayerCallbackOutcome(
            true,
            BankConnectionStatuses.ConnectedPendingSync,
            "Bank linked successfully. Return to the app while we start the first sync.",
            StatusCodes.Status200OK,
            connection.Id,
            appReturnUri,
            SafeToClose: true,
            ShouldAutoReturn: true,
            CallbackLifecycleStage: BankConnectionLifecycleStages.ReturnedToApp,
            CallbackLifecycleReason: "authorization_completed");
    }

    private static string BuildCallbackState(string nonce, string appReturnUri)
    {
        return $"{nonce}.{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(appReturnUri))}";
    }

    public static string? ExtractAppReturnUri(string? callbackState)
    {
        if (string.IsNullOrWhiteSpace(callbackState))
        {
            return null;
        }

        var separatorIndex = callbackState.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == callbackState.Length - 1)
        {
            return null;
        }

        try
        {
            var encoded = callbackState[(separatorIndex + 1)..];
            var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return TrueLayerReturnUriContract.Normalize(decoded);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> BuildScopes()
    {
        return TrueLayerScopes.Default;
    }

    public static IReadOnlyList<string> BuildProviders(string environment)
    {
        if (string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return TrueLayerProviders.SandboxDefault;
        }

        if (string.Equals(environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            return TrueLayerProviders.LiveIrelandDefault;
        }

        return [];
    }

    public static string? BuildCountryId(string environment)
    {
        return string.Equals(environment, "live", StringComparison.OrdinalIgnoreCase)
            ? TrueLayerCountryIds.Ireland
            : null;
    }

    public static string BuildAuthorizationLink(
        string authBaseUrl,
        string clientId,
        string redirectUri,
        string state,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> providers,
        string? countryId = null)
    {
        var uriBuilder = new UriBuilder(authBaseUrl)
        {
            Path = "/",
            Query = string.Empty
        };

        var queryValues = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', scopes),
            ["state"] = state
        };

        if (providers.Count > 0)
        {
            queryValues["providers"] = string.Join(' ', providers);
        }

        if (!string.IsNullOrWhiteSpace(countryId))
        {
            queryValues["country_id"] = countryId;
        }

        return QueryHelpers.AddQueryString(uriBuilder.Uri.ToString(), queryValues);
    }
}
