using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class GoogleAuthService(
    IOptions<GoogleAuthOptions> options,
    IGoogleIdTokenVerifier idTokenVerifier,
    ILogger<GoogleAuthService> logger)
{
    private readonly IReadOnlyCollection<string> _allowedClientIds = options.Value.GetConfiguredClientIds();

    public bool IsConfigured => _allowedClientIds.Count > 0;

    public string ProviderType => "google_oidc";

    public async Task<ServiceResult<GoogleIdentityPayload>> VerifyIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        var tokenSummary = SummarizeToken(idToken);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Google token verification started hasIdToken={HasIdToken} idTokenLength={IdTokenLength}",
                tokenSummary.HasToken,
                tokenSummary.TokenLength);
        }

        if (!IsConfigured)
        {
            logger.LogWarning(
                "Google sign-in attempted but no Google client IDs are configured. " +
                "Set GoogleAuth:WebClientId and GoogleAuth:AndroidClientIdProd.");
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google sign-in is not configured.",
                "google_sign_in_not_configured",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(idToken))
        {
            logger.LogWarning("Google token verification failed reason=missing_token");
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google ID token is required.",
                "google_id_token_required",
                StatusCodes.Status400BadRequest);
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await idTokenVerifier.ValidateAsync(
                idToken.Trim(),
                _allowedClientIds,
                cancellationToken);
        }
        catch (InvalidJwtException ex)
        {
            var reason = ClassifyInvalidJwtFailureReason(ex);
            logger.LogInformation(
                ex,
                "Google ID token validation failed reason={Reason} idTokenLength={IdTokenLength}",
                reason,
                tokenSummary.TokenLength);
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google authentication failed.",
                "google_id_token_invalid",
                StatusCodes.Status401Unauthorized);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google ID token verification encountered an unexpected error.");
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google authentication is currently unavailable.",
                "google_auth_unavailable",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(payload.Subject))
        {
            logger.LogWarning("Google token verification failed reason=subject_missing");
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google identity is missing subject identifier.",
                "google_subject_missing",
                StatusCodes.Status401Unauthorized);
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Google token verification passed subjectPrefix={SubjectPrefix} email={Email}",
                payload.Subject[..Math.Min(8, payload.Subject.Length)],
                payload.Email);
        }

        return ServiceResult<GoogleIdentityPayload>.Ok(
            new GoogleIdentityPayload(
                payload.Subject.Trim(),
                payload.Email?.Trim(),
                payload.EmailVerified,
                payload.Name?.Trim(),
                payload.GivenName?.Trim(),
                payload.FamilyName?.Trim(),
                payload.Picture?.Trim()));
    }

    private static (bool HasToken, int TokenLength) SummarizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, 0);
        }

        var trimmed = token.Trim();
        return (true, trimmed.Length);
    }

    private static string ClassifyInvalidJwtFailureReason(InvalidJwtException exception)
    {
        var message = exception.Message?.ToLowerInvariant() ?? string.Empty;

        if (message.Contains("wrong recipient", StringComparison.Ordinal)
            || message.Contains("audience", StringComparison.Ordinal))
        {
            return "invalid_audience";
        }

        if (message.Contains("issuer", StringComparison.Ordinal))
        {
            return "invalid_issuer";
        }

        if (message.Contains("expired", StringComparison.Ordinal))
        {
            return "expired_token";
        }

        if (message.Contains("malformed", StringComparison.Ordinal)
            || message.Contains("consist of 3", StringComparison.Ordinal)
            || message.Contains("unable to decode", StringComparison.Ordinal))
        {
            return "malformed_token";
        }

        return "invalid_token";
    }
}
