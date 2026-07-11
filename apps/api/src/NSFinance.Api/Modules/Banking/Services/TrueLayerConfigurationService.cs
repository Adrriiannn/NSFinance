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
                "TrueLayer environment must be 'live'.",
                "truelayer_environment_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.ClientSecret)
            || string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer is not configured. Set TrueLayer:ClientId, TrueLayer:ClientSecret, and TrueLayer:RedirectUri (or TRUELAYER_CLIENT_ID, TRUELAYER_CLIENT_SECRET, TRUELAYER_REDIRECT_URI).",
                "truelayer_not_configured",
                StatusCodes.Status503ServiceUnavailable);
        }

        var authBase = string.IsNullOrWhiteSpace(_options.AuthBaseUrl)
            ? GetDefaultAuthBaseUrl()
            : _options.AuthBaseUrl.Trim();
        var apiBase = string.IsNullOrWhiteSpace(_options.ApiBaseUrl)
            ? GetDefaultApiBaseUrl()
            : _options.ApiBaseUrl.Trim();

        if (!Uri.TryCreate(authBase, UriKind.Absolute, out var authUri)
            || !Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer base URLs are invalid.",
                "truelayer_base_url_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (UsesNonLiveBaseUrls(authUri, apiUri))
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                "TrueLayer auth/API base URLs must target the live TrueLayer hosts.",
                "truelayer_environment_mismatch",
                StatusCodes.Status500InternalServerError);
        }

        var redirectValidation = ValidateRedirectUri(_options.RedirectUri);
        if (!redirectValidation.Succeeded)
        {
            return ServiceResult<TrueLayerResolvedConfiguration>.Fail(
                redirectValidation.Error!.Message,
                redirectValidation.Error.Code,
                redirectValidation.Error.StatusCode);
        }

        return ServiceResult<TrueLayerResolvedConfiguration>.Ok(
            new TrueLayerResolvedConfiguration(
                _options.ClientId.Trim(),
                _options.ClientSecret.Trim(),
                redirectValidation.Value!,
                environment,
                authUri.ToString().TrimEnd('/'),
                apiUri.ToString().TrimEnd('/')));
    }

    private static bool UsesNonLiveBaseUrls(Uri authUri, Uri apiUri)
    {
        return !string.Equals(authUri.Host, "auth.truelayer.com", StringComparison.OrdinalIgnoreCase)
               || !string.Equals(apiUri.Host, "api.truelayer.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeEnvironment(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "live" => "live",
            _ => null
        };
    }

    private static string GetDefaultAuthBaseUrl() => "https://auth.truelayer.com";

    private static string GetDefaultApiBaseUrl() => "https://api.truelayer.com";

    private static ServiceResult<string> ValidateRedirectUri(string redirectUri)
    {
        var candidate = redirectUri.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var redirect))
        {
            return ServiceResult<string>.Fail(
                "TrueLayer redirect URI must be an absolute URI.",
                "truelayer_redirect_uri_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (!string.Equals(redirect.AbsolutePath, "/api/banking/truelayer/callback", StringComparison.Ordinal))
        {
            return ServiceResult<string>.Fail(
                "TrueLayer redirect URI must target /api/banking/truelayer/callback.",
                "truelayer_redirect_uri_path_invalid",
                StatusCodes.Status500InternalServerError);
        }

        if (!string.Equals(redirect.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.Fail(
                "Live TrueLayer redirect URI must use HTTPS.",
                "truelayer_redirect_uri_https_required",
                StatusCodes.Status500InternalServerError);
        }

        if (IsLocalHost(redirect.Host))
        {
            return ServiceResult<string>.Fail(
                "Live TrueLayer redirect URI cannot point to localhost.",
                "truelayer_redirect_uri_localhost_invalid",
                StatusCodes.Status500InternalServerError);
        }

        return ServiceResult<string>.Ok(redirect.ToString().TrimEnd('/'));
    }

    private static bool IsLocalHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}
