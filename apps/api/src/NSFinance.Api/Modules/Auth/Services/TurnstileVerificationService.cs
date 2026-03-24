using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class TurnstileVerificationService(
    HttpClient httpClient,
    IOptions<TurnstileOptions> options,
    IRequestContextAccessor requestContextAccessor,
    ILogger<TurnstileVerificationService> logger)
{
    private static readonly Uri SiteVerifyUri = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");
    private readonly TurnstileOptions _options = options.Value;

    public async Task<ServiceResult> VerifyRegisterTokenAsync(string? token, CancellationToken cancellationToken)
    {
        var trimmedToken = token?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedToken))
        {
            return ServiceResult.Fail(
                "Security verification token is required.",
                "captcha_token_required",
                StatusCodes.Status400BadRequest);
        }

        var secretKey = _options.SecretKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            logger.LogError("Turnstile verification failed because Turnstile:SecretKey is missing.");
            return ServiceResult.Fail(
                "Security verification is unavailable.",
                "captcha_verification_unavailable",
                StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            using var content = new FormUrlEncodedContent(BuildRequestBody(secretKey, trimmedToken, requestContextAccessor.IpAddress));
            using var response = await httpClient.PostAsync(SiteVerifyUri, content, cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning(
                    "Turnstile siteverify returned non-success HTTP status {StatusCode}",
                    (int)response.StatusCode);
                return ServiceResult.Fail(
                    "Security verification failed. Please retry.",
                    "captcha_verification_failed",
                    StatusCodes.Status400BadRequest);
            }

            var verification = await response.Content.ReadFromJsonAsync<TurnstileVerificationResponse>(cancellationToken: cancellationToken);
            if (verification is null)
            {
                logger.LogWarning("Turnstile siteverify response body was empty.");
                return ServiceResult.Fail(
                    "Security verification failed. Please retry.",
                    "captcha_verification_failed",
                    StatusCodes.Status400BadRequest);
            }

            if (!verification.Success)
            {
                logger.LogInformation(
                    "Turnstile siteverify rejected token. ErrorCodes={ErrorCodes}",
                    verification.ErrorCodes is { Length: > 0 } ? string.Join(",", verification.ErrorCodes) : "<none>");
                return ServiceResult.Fail(
                    "Security verification failed. Please retry.",
                    "captcha_verification_failed",
                    StatusCodes.Status400BadRequest);
            }

            if (!string.Equals(verification.Action, "register", StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Turnstile token action mismatch. Expected=register Actual={Action}",
                    verification.Action ?? "<null>");
                return ServiceResult.Fail(
                    "Security verification failed. Please retry.",
                    "captcha_verification_failed",
                    StatusCodes.Status400BadRequest);
            }

            return ServiceResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Turnstile verification call failed.");
            return ServiceResult.Fail(
                "Security verification is unavailable.",
                "captcha_verification_unavailable",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static Dictionary<string, string> BuildRequestBody(string secret, string responseToken, string? remoteIp)
    {
        var values = new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = responseToken
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            values["remoteip"] = remoteIp.Trim();
        }

        return values;
    }

    private sealed record TurnstileVerificationResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
