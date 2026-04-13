using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.Banking.Validators;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class TrueLayerAuthService(
    TrueLayerConfigurationService configurationService,
    BankConnectionService bankConnectionService,
    BankConnectionAttemptService attemptService,
    TrueLayerTokenService tokenService,
    BankSyncService bankSyncService,
    DeterministicReclassificationTriggerService reclassificationTriggerService,
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

        if (string.IsNullOrWhiteSpace(connection.AuthStateNonce))
        {
            return ServiceResult<StartTrueLayerLinkResponse>.Fail(
                "Unable to initialize callback state for this connection attempt.",
                "bank_connection_attempt_state_unavailable",
                StatusCodes.Status500InternalServerError);
        }

        var attempt = await attemptService.CreateAttemptAsync(
            userId,
            connection.Id,
            connection.ProviderName,
            connection.ProviderEnvironment,
            connection.AuthStateNonce,
            normalizedAppReturnUri,
            connection.AuthStateExpiresUtc,
            reconnectConnectionId.HasValue,
            cancellationToken);

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
                attempt.Id,
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

        var attempt = await attemptService.FindByCallbackStateAsync(query.State, cancellationToken);
        if (attempt is null)
        {
            logger.LogWarning("TrueLayer callback rejected because state was invalid or expired.");
            return new TrueLayerCallbackOutcome(
                false,
                "callback_state_invalid",
                "This callback cannot be matched to a valid connection attempt.",
                StatusCodes.Status400BadRequest,
                null,
                ExtractAppReturnUri(query.State),
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                CallbackLifecycleReason: "callback_state_invalid_or_already_consumed");
        }

        return await attemptService.WithAttemptLockAsync(
            attempt.Id,
            async () =>
            {
                var refreshedAttempt = await attemptService.FindByCallbackStateAsync(query.State, cancellationToken);
                if (refreshedAttempt is null)
                {
                    return new TrueLayerCallbackOutcome(
                        false,
                        "callback_state_invalid",
                        "This callback cannot be matched to a valid connection attempt.",
                        StatusCodes.Status400BadRequest,
                        null,
                        ExtractAppReturnUri(query.State),
                        SafeToClose: true,
                        ShouldAutoReturn: false,
                        CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                        CallbackLifecycleReason: "callback_state_invalid_or_already_consumed");
                }

                attempt = refreshedAttempt;
                var appReturnUri = attempt.AppReturnUri ?? ExtractAppReturnUri(query.State);
        if (attempt.Status == BankConnectionAttemptStatuses.Superseded)
        {
            return new TrueLayerCallbackOutcome(
                true,
                "callback_attempt_superseded",
                "This connection attempt was replaced by a newer one.",
                StatusCodes.Status200OK,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                CallbackLifecycleReason: "attempt_superseded",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        if (attempt.Status == BankConnectionAttemptStatuses.Expired)
        {
            return new TrueLayerCallbackOutcome(
                false,
                "callback_attempt_expired",
                "This connection attempt expired. Open NSFinance to start a new one.",
                StatusCodes.Status410Gone,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                CallbackLifecycleReason: "attempt_expired",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        if (attempt.Status == BankConnectionAttemptStatuses.Completed)
        {
            return new TrueLayerCallbackOutcome(
                true,
                "callback_attempt_completed",
                "This connection is already complete.",
                StatusCodes.Status200OK,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Completed,
                CallbackLifecycleReason: "attempt_already_completed",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        if (attempt.Status == BankConnectionAttemptStatuses.Failed)
        {
            return new TrueLayerCallbackOutcome(
                false,
                "callback_attempt_failed",
                "This connection attempt needs attention in NSFinance.",
                StatusCodes.Status200OK,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: attempt.FailureCode ?? "attempt_failed",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        if (attempt.Status is BankConnectionAttemptStatuses.CallbackReceived
            or BankConnectionAttemptStatuses.AppReturnInitiated
            or BankConnectionAttemptStatuses.AppReturnConfirmed
            or BankConnectionAttemptStatuses.ConnectionCreated
            or BankConnectionAttemptStatuses.Processing)
        {
            var safeToClose = attempt.Status is not (BankConnectionAttemptStatuses.CallbackReceived or BankConnectionAttemptStatuses.AppReturnInitiated);
            return new TrueLayerCallbackOutcome(
                true,
                "callback_already_handled",
                safeToClose
                    ? "This callback is already being handled in NSFinance."
                    : "Returning to NSFinance. Your connection is already being handled.",
                StatusCodes.Status200OK,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: safeToClose,
                ShouldAutoReturn: !safeToClose,
                CallbackLifecycleStage: safeToClose
                    ? BankConnectionLifecycleStages.ReturnedToApp
                    : BankConnectionLifecycleStages.DeepLinkReturnInitiated,
                CallbackLifecycleReason: "callback_already_handled",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        if (!BankConnectionAttemptService.IsAwaitingCallbackStatus(attempt.Status))
        {
            return new TrueLayerCallbackOutcome(
                true,
                "callback_already_handled",
                "This callback is already being handled in NSFinance.",
                StatusCodes.Status200OK,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.ReturnedToApp,
                CallbackLifecycleReason: "callback_already_handled",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        var connection = await bankConnectionService.FindConnectionForUserAsync(
            attempt.UserId,
            attempt.ConnectionId,
            cancellationToken);
        if (connection is null)
        {
            await attemptService.MarkFailedAsync(
                attempt,
                "attempt_connection_missing",
                "The bank connection referenced by this attempt no longer exists.",
                cancellationToken);
            return new TrueLayerCallbackOutcome(
                false,
                "attempt_connection_missing",
                "This connection attempt can no longer continue. Open NSFinance to start again.",
                StatusCodes.Status404NotFound,
                attempt.ConnectionId,
                appReturnUri,
                SafeToClose: true,
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.Failed,
                CallbackLifecycleReason: "attempt_connection_missing",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        logger.LogInformation(
            "TrueLayer callback received for connectionId={ConnectionId} attemptId={AttemptId} hasCode={HasCode} hasError={HasError}",
            connection.Id,
            attempt.Id,
            !string.IsNullOrWhiteSpace(query.Code),
            !string.IsNullOrWhiteSpace(query.Error));

        await attemptService.MarkCallbackReceivedAsync(attempt, cancellationToken);

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "bank_connection_callback_received",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new
            {
                attemptId = attempt.Id,
                hasCode = !string.IsNullOrWhiteSpace(query.Code),
                hasError = !string.IsNullOrWhiteSpace(query.Error)
            },
            cancellationToken);

        connection.AuthStateNonce = null;
        connection.AuthStateExpiresUtc = null;

        logger.LogInformation(
            "TrueLayer callback state consumed for connectionId={ConnectionId} attemptId={AttemptId}",
            connection.Id,
            attempt.Id);

        if (!string.IsNullOrWhiteSpace(query.Error))
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                query.Error,
                query.ErrorDescription,
                cancellationToken);
            await attemptService.MarkFailedAsync(
                attempt,
                query.Error,
                query.ErrorDescription ?? "Bank consent was not completed.",
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
                    attemptId = attempt.Id,
                    error = query.Error
                },
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
                CallbackLifecycleReason: "provider_declined_or_cancelled",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
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
            await attemptService.MarkFailedAsync(
                attempt,
                configResult.Error.Code,
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
                CallbackLifecycleReason: "provider_configuration_unavailable",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        if (!string.Equals(connection.ProviderEnvironment, configResult.Value!.Environment, StringComparison.OrdinalIgnoreCase))
        {
            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                "truelayer_environment_mismatch",
                "Connection environment does not match server configuration.",
                cancellationToken);
            await attemptService.MarkFailedAsync(
                attempt,
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
                CallbackLifecycleReason: "environment_mismatch",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
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
            await attemptService.MarkFailedAsync(
                attempt,
                tokenResult.Error?.Code,
                tokenResult.Error?.Message ?? "Authorization code could not be exchanged.",
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
                    attemptId = attempt.Id,
                    code = tokenResult.Error?.Code,
                    status = nextStatus
                },
                cancellationToken);

            logger.LogWarning(
                "TrueLayer callback token exchange failed connectionId={ConnectionId} attemptId={AttemptId} code={Code}",
                connection.Id,
                attempt.Id,
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
                CallbackLifecycleReason: "token_exchange_failed",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        logger.LogInformation(
            "TrueLayer token exchange succeeded for connectionId={ConnectionId} attemptId={AttemptId}",
            connection.Id,
            attempt.Id);

        var persistResult = await bankSyncService.PersistTokenAsync(
            connection,
            tokenResult.Value!,
            cancellationToken);
        if (!persistResult.Succeeded)
        {
            logger.LogError(
                "TrueLayer token persistence failed for connectionId={ConnectionId} attemptId={AttemptId} code={Code}",
                connection.Id,
                attempt.Id,
                persistResult.Error?.Code);
            await attemptService.MarkFailedAsync(
                attempt,
                persistResult.Error?.Code,
                persistResult.Error?.Message ?? "Secure token storage failed.",
                cancellationToken);

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
                CallbackLifecycleReason: "token_persistence_failed",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.ConnectedPendingSync,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        logger.LogInformation(
            "Bank connection marked as {Status} for connectionId={ConnectionId} attemptId={AttemptId}",
            BankConnectionStatuses.ConnectedPendingSync,
            connection.Id,
            attempt.Id);

        try
        {
            await reclassificationTriggerService.TriggerAsync(
                new DeterministicReclassificationTriggerRequest(
                    UserId: connection.UserId,
                    Source: "callback_reconnect_relink",
                    ReasonCode: DeterministicReclassificationTriggerReasons.ReconnectRelink,
                    SourceConnectionId: connection.Id,
                    ConnectionIds: [connection.Id],
                    MarkConnectionsForHistoricalReplay: true,
                    QueueConnections: true,
                    RequireImportedFootprint: true),
                cancellationToken);
        }
        catch (Exception triggerException)
        {
            logger.LogWarning(
                triggerException,
                "Reconnect/relink deterministic trigger failed connectionId={ConnectionId} attemptId={AttemptId}",
                connection.Id,
                attempt.Id);
        }

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "token_exchange_success",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new
            {
                attemptId = attempt.Id,
                provider = connection.ProviderName
            },
            cancellationToken);

        try
        {
            await trueLayerSyncQueue.QueueInitialSyncAsync(connection.UserId, connection.Id, cancellationToken);
            logger.LogInformation(
                "TrueLayer callback queued initial sync for connectionId={ConnectionId} attemptId={AttemptId}",
                connection.Id,
                attempt.Id);

            await auditService.WriteEventAsync(
                category: "banking",
                eventName: "initial_sync_queued",
                targetEntityType: "open_banking_connection",
                targetEntityId: connection.Id.ToString(),
                actorId: connection.UserId,
                actorType: "user",
                metadata: new
                {
                    attemptId = attempt.Id,
                    connectionId = connection.Id
                },
                cancellationToken);

            await attemptService.MarkAppReturnInitiatedAsync(attempt, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "TrueLayer callback could not queue initial sync for connectionId={ConnectionId} attemptId={AttemptId}",
                connection.Id,
                attempt.Id);

            await bankConnectionService.MarkConnectionStateAsync(
                connection,
                BankConnectionStatuses.Failed,
                "initial_sync_queue_unavailable",
                "Bank linked, but automatic sync could not be queued. Open the app and run sync manually.",
                cancellationToken);
            await attemptService.MarkFailedAsync(
                attempt,
                "initial_sync_queue_unavailable",
                "Initial sync could not be queued automatically.",
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
                    attemptId = attempt.Id,
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
                ShouldAutoReturn: false,
                CallbackLifecycleStage: BankConnectionLifecycleStages.CompletedWithWarnings,
                CallbackLifecycleReason: "initial_sync_queue_unavailable",
                AttemptId: attempt.Id,
                AttemptStatus: attempt.Status,
                AttemptPublicToken: attempt.PublicToken);
        }

                return new TrueLayerCallbackOutcome(
                    true,
                    BankConnectionStatuses.ConnectedPendingSync,
                    "Your bank is connected. We are finishing setup in NSFinance.",
                    StatusCodes.Status200OK,
                    connection.Id,
                    appReturnUri,
                    SafeToClose: false,
                    ShouldAutoReturn: true,
                    CallbackLifecycleStage: BankConnectionLifecycleStages.DeepLinkReturnInitiated,
                    CallbackLifecycleReason: "authorization_completed",
                    AttemptId: attempt.Id,
                    AttemptStatus: attempt.Status,
                    AttemptPublicToken: attempt.PublicToken);
            });
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
