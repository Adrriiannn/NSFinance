using System.Text.Json;
using System.Text.RegularExpressions;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.Services.Models;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class TrueLayerTokenService(
    TrueLayerHttpClient httpClient,
    ILogger<TrueLayerTokenService> logger)
{
    public async Task<ServiceResult<TrueLayerTokenExchangeResult>> ExchangeAuthorizationCodeAsync(
        TrueLayerResolvedConfiguration configuration,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildTokenEndpoint(configuration.AuthBaseUrl);
        var grantType = "authorization_code";
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = grantType,
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = configuration.ClientSecret,
            ["redirect_uri"] = configuration.RedirectUri,
            ["code"] = authorizationCode
        };

        var preflightValidation = ValidateAuthorizationCodeExchangeRequest(configuration, endpoint, payload, authorizationCode);
        if (preflightValidation is not null)
        {
            logger.LogWarning(
                "TrueLayer token exchange request validation failed code={ErrorCode} reason={Reason} endpoint={Endpoint} environment={Environment}",
                preflightValidation.Code,
                preflightValidation.Message,
                endpoint,
                configuration.Environment);

            return ServiceResult<TrueLayerTokenExchangeResult>.Fail(
                preflightValidation.Message,
                preflightValidation.Code,
                StatusCodes.Status500InternalServerError);
        }

        logger.LogInformation(
            "TrueLayer token exchange request endpoint={Endpoint} environment={Environment} redirectUri={RedirectUri} clientId={ClientId} grantType={GrantType}",
            endpoint,
            configuration.Environment,
            configuration.RedirectUri,
            configuration.ClientId,
            grantType);

        var response = await httpClient.PostFormAsync(endpoint, payload, cancellationToken);
        return MapTokenExchangeResponse(response, grantType);
    }

    public async Task<ServiceResult<TrueLayerTokenExchangeResult>> RefreshAccessTokenAsync(
        TrueLayerResolvedConfiguration configuration,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildTokenEndpoint(configuration.AuthBaseUrl);
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = configuration.ClientSecret,
            ["refresh_token"] = refreshToken
        };

        var response = await httpClient.PostFormAsync(endpoint, payload, cancellationToken);
        return MapTokenExchangeResponse(response, "refresh_token");
    }

    private ServiceResult<TrueLayerTokenExchangeResult> MapTokenExchangeResponse(
        ServiceResult<string> response,
        string grantType)
    {
        if (!response.Succeeded)
        {
            var providerError = TryReadProviderError(response.Error?.Message);
            var statusCode = response.Error?.StatusCode ?? StatusCodes.Status502BadGateway;
            var safeBody = SanitizeProviderPayload(response.Error?.Message);
            if (providerError is not null)
            {
                var mappedCode = MapProviderErrorToInternalCode(providerError);
                logger.LogWarning(
                    "TrueLayer token exchange failed grantType={GrantType} status={StatusCode} providerError={ProviderError} mappedCode={MappedCode} providerResponse={ProviderResponse}",
                    grantType,
                    statusCode,
                    providerError.ErrorCode,
                    mappedCode,
                    safeBody);

                return ServiceResult<TrueLayerTokenExchangeResult>.Fail(
                    providerError.ErrorDescription,
                    mappedCode,
                    statusCode);
            }

            logger.LogWarning(
                "TrueLayer token exchange failed grantType={GrantType} status={StatusCode} providerResponse={ProviderResponse}",
                grantType,
                statusCode,
                safeBody);

            return ServiceResult<TrueLayerTokenExchangeResult>.Fail(
                "Token exchange with TrueLayer failed.",
                "truelayer_token_exchange_failed",
                statusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            var root = document.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var accessNode) ? accessNode.GetString() : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var refreshNode) ? refreshNode.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var expiresNode) ? expiresNode.GetInt32() : 0;
            var scope = root.TryGetProperty("scope", out var scopeNode) ? scopeNode.GetString() : null;

            if (string.IsNullOrWhiteSpace(accessToken)
                || string.IsNullOrWhiteSpace(refreshToken)
                || expiresIn <= 0)
            {
                return ServiceResult<TrueLayerTokenExchangeResult>.Fail(
                    "TrueLayer token response was incomplete.",
                    "truelayer_token_payload_invalid",
                    StatusCodes.Status502BadGateway);
            }

            return ServiceResult<TrueLayerTokenExchangeResult>.Ok(
                new TrueLayerTokenExchangeResult(
                    accessToken,
                    refreshToken,
                    DateTime.UtcNow.AddSeconds(expiresIn),
                    scope));
        }
        catch (JsonException)
        {
            return ServiceResult<TrueLayerTokenExchangeResult>.Fail(
                "Unable to parse TrueLayer token response.",
                "truelayer_token_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

    private static TrueLayerApiError? TryReadProviderError(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var errorCode = root.TryGetProperty("error", out var codeNode) ? codeNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(errorCode))
            {
                return null;
            }

            var description = root.TryGetProperty("error_description", out var descriptionNode)
                ? descriptionNode.GetString()
                : "Provider rejected the token exchange request.";

            return new TrueLayerApiError(errorCode, description ?? "Provider returned an error.", StatusCodes.Status400BadRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildTokenEndpoint(string authBaseUrl)
    {
        var normalizedBase = authBaseUrl.TrimEnd('/');
        return $"{normalizedBase}/connect/token";
    }

    private static ServiceError? ValidateAuthorizationCodeExchangeRequest(
        TrueLayerResolvedConfiguration configuration,
        string endpoint,
        IReadOnlyDictionary<string, string> payload,
        string authorizationCode)
    {
        var expectedEndpoint = BuildTokenEndpoint(configuration.AuthBaseUrl);
        if (!string.Equals(endpoint, expectedEndpoint, StringComparison.Ordinal))
        {
            return new ServiceError(
                "Token endpoint mismatch for configured auth base URL.",
                "truelayer_token_endpoint_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (!payload.TryGetValue("grant_type", out var grantType)
            || !string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
        {
            return new ServiceError(
                "grant_type must be authorization_code for initial token exchange.",
                "truelayer_grant_type_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (!payload.TryGetValue("redirect_uri", out var redirectUri)
            || !string.Equals(redirectUri, configuration.RedirectUri, StringComparison.Ordinal))
        {
            return new ServiceError(
                "Redirect URI mismatch in token exchange request.",
                "truelayer_redirect_uri_mismatch_internal",
                StatusCodes.Status500InternalServerError);
        }

        if (string.IsNullOrWhiteSpace(configuration.ClientId)
            || string.IsNullOrWhiteSpace(configuration.ClientSecret))
        {
            return new ServiceError(
                "TrueLayer client credentials are missing.",
                "truelayer_client_credentials_missing",
                StatusCodes.Status500InternalServerError);
        }

        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return new ServiceError(
                "Authorization code is missing.",
                "truelayer_authorization_code_missing",
                StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static string MapProviderErrorToInternalCode(TrueLayerApiError providerError)
    {
        var description = providerError.ErrorDescription.ToLowerInvariant();
        return providerError.ErrorCode switch
        {
            "invalid_grant" => "truelayer_authorization_code_invalid",
            "invalid_client" => "truelayer_client_credentials_invalid",
            "invalid_request" when description.Contains("redirect_uri", StringComparison.Ordinal) => "truelayer_redirect_uri_mismatch",
            "invalid_request" => "truelayer_token_invalid_request",
            "unauthorized_client" => "truelayer_client_not_authorized",
            "unsupported_grant_type" => "truelayer_grant_type_invalid",
            _ => "truelayer_token_exchange_failed"
        };
    }

    private static string SanitizeProviderPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "<empty>";
        }

        var sanitized = payload;
        sanitized = Regex.Replace(
            sanitized,
            "\"(access_token|refresh_token|client_secret|code)\"\\s*:\\s*\"[^\"]*\"",
            "\"$1\":\"[REDACTED]\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(
            sanitized,
            "(code|client_secret|refresh_token|access_token)=([^&\\s]+)",
            "$1=[REDACTED]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        const int maxLength = 1200;
        if (sanitized.Length > maxLength)
        {
            sanitized = $"{sanitized[..maxLength]}...(truncated)";
        }

        return sanitized;
    }
}
