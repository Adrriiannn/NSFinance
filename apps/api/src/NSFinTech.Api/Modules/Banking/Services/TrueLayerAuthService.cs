using Microsoft.AspNetCore.WebUtilities;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Banking.DTOs;
using NSFinTech.Api.Modules.Banking.Validators;

namespace NSFinTech.Api.Modules.Banking.Services;

public sealed class TrueLayerAuthService(
    TrueLayerConfigurationService configurationService,
    BankConnectionService bankConnectionService,
    TrueLayerTokenService tokenService,
    BankSyncService bankSyncService,
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
            return new TrueLayerCallbackOutcome(
                false,
                "callback_state_invalid",
                "The authorization state is invalid or expired. Restart connection from the app.",
                StatusCodes.Status400BadRequest,
                null);
        }

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

        await bankConnectionService.MarkConnectionStateAsync(
            connection,
            BankConnectionStatuses.Connected,
            errorCode: null,
            errorReason: null,
            cancellationToken);

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "token_exchange_success",
            targetEntityType: "open_banking_connection",
            targetEntityId: connection.Id.ToString(),
            actorId: connection.UserId,
            actorType: "user",
            metadata: new { provider = connection.ProviderName },
            cancellationToken);

        var syncResult = await bankSyncService.RunInitialSyncAsync(
            connection,
            configResult.Value,
            tokenResult.Value!,
            cancellationToken);
        if (!syncResult.Succeeded)
        {
            return new TrueLayerCallbackOutcome(
                false,
                syncResult.Error!.Code,
                "Bank linked, but the initial sync did not complete. Use reconnect or manual sync in app.",
                syncResult.Error.StatusCode,
                connection.Id);
        }

        return new TrueLayerCallbackOutcome(
            true,
            "connected",
            "Bank connection complete. Return to the app and refresh accounts.",
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
