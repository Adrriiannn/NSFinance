using Microsoft.AspNetCore.WebUtilities;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Validators;

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
        var connection = await bankConnectionService.CreateConnectionStartedAsync(
            userId,
            BankingProviders.TrueLayer,
            configuration.Environment,
            cancellationToken);

        var scopes = BuildScopes();
        var providers = BuildProviders(configuration.Environment);
        var authLink = BuildAuthorizationLink(
            configuration.AuthBaseUrl,
            configuration.ClientId,
            configuration.RedirectUri,
            connection.AuthStateNonce!,
            scopes,
            providers);

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
                null);
        }

        var connection = await bankConnectionService.FindConnectionByStateAsync(query.State!, cancellationToken);
        if (connection is null)
        {
            logger.LogWarning("TrueLayer callback rejected because state was invalid or expired.");
            return new TrueLayerCallbackOutcome(
                false,
                "callback_state_invalid",
                "The authorization state is invalid or expired. Restart connection from the app.",
                StatusCodes.Status400BadRequest,
                null);
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
                connection.Id);
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
                connection.Id);
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
                connection.Id);
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
                connection.Id);
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
                connection.Id);
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
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "TrueLayer callback could not queue initial sync for connectionId={ConnectionId}",
                connection.Id);
        }

        return new TrueLayerCallbackOutcome(
            true,
            BankConnectionStatuses.ConnectedPendingSync,
            "Bank linked successfully. Return to the app while we start the first sync.",
            StatusCodes.Status200OK,
            connection.Id);
    }

    public static IReadOnlyList<string> BuildScopes()
    {
        return TrueLayerScopes.Default;
    }

    public static IReadOnlyList<string> BuildProviders(string environment)
    {
        return string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            ? TrueLayerProviders.SandboxDefault
            : [];
    }

    public static string BuildAuthorizationLink(
        string authBaseUrl,
        string clientId,
        string redirectUri,
        string state,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> providers)
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

        return QueryHelpers.AddQueryString(uriBuilder.Uri.ToString(), queryValues);
    }
}
