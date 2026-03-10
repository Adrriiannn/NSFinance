using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed record TrueLayerResolvedConfiguration(
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string Environment,
    string AuthBaseUrl,
    string ApiBaseUrl);

public sealed class TrueLayerConfigurationService(IOptions<TrueLayerOptions> options)
{
    private readonly TrueLayerOptions _options = options.Value;

    public ServiceResult<TrueLayerResolvedConfiguration> Resolve()
    {
        var environment = NormalizeEnvironment(_options.Environment);
        if (environment is null)
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer environment must be 'sandbox' or 'live'.",
                "truelayer_environment_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.ClientSecret)
            || string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer is not configured for local development. Set TrueLayer:ClientId, TrueLayer:ClientSecret, and TrueLayer:RedirectUri (or TRUELAYER_CLIENT_ID, TRUELAYER_CLIENT_SECRET, TRUELAYER_REDIRECT_URI).",
                "truelayer_not_configured",
                StatusCodes.Status503ServiceUnavailable);
        }

        var authBase = string.IsNullOrWhiteSpace(_options.AuthBaseUrl)
            ? GetDefaultAuthBaseUrl(environment)
            : _options.AuthBaseUrl.Trim();
        var apiBase = string.IsNullOrWhiteSpace(_options.ApiBaseUrl)
            ? GetDefaultApiBaseUrl(environment)
            : _options.ApiBaseUrl.Trim();

        if (!Uri.TryCreate(authBase, UriKind.Absolute, out var authUri)
            || !Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer base URLs are invalid.",
                "truelayer_base_url_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (IsEnvironmentMismatch(environment, authUri, apiUri))
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer auth/API base URLs do not match TRUELAYER_ENVIRONMENT.",
                "truelayer_environment_mismatch",
                StatusCodes.Status500InternalServerError);
        }

        return ServiceResult<TrueLayerResolvedConfiguration>.Ok(
            new TrueLayerResolvedConfiguration(
                _options.ClientId.Trim(),
                _options.ClientSecret.Trim(),
                _options.RedirectUri.Trim(),
                environment,
                authUri.ToString().TrimEnd('/'),
                apiUri.ToString().TrimEnd('/')));
    }

    private static bool IsEnvironmentMismatch(string environment, Uri authUri, Uri apiUri)
    {
        var authHost = authUri.Host.ToLowerInvariant();
        var apiHost = apiUri.Host.ToLowerInvariant();

        if (environment == "sandbox")
        {
            return !authHost.Contains("sandbox") || !apiHost.Contains("sandbox");
        }

        return authHost.Contains("sandbox") || apiHost.Contains("sandbox");
    }

    private static string? NormalizeEnvironment(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "sandbox" => "sandbox",
            "live" => "live",
            _ => null
        };
    }

    private static string GetDefaultAuthBaseUrl(string environment) =>
        environment == "live"
            ? "https://auth.truelayer.com"
            : "https://auth.truelayer-sandbox.com";

    private static string GetDefaultApiBaseUrl(string environment) =>
        environment == "live"
            ? "https://api.truelayer.com"
            : "https://api.truelayer-sandbox.com";
}
