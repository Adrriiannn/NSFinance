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
        if (!IsConfigured)
        {
            logger.LogWarning(
                "Google sign-in attempted but no Google client IDs are configured. " +
                "Set GoogleAuth:WebClientId and/or GoogleAuth:AndroidClientIdDebug/GoogleAuth:AndroidClientIdProd.");
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google sign-in is not configured in this environment.",
                "google_sign_in_not_configured",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(idToken))
        {
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
            logger.LogInformation(ex, "Google ID token validation failed.");
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
            return ServiceResult<GoogleIdentityPayload>.Fail(
                "Google identity is missing subject identifier.",
                "google_subject_missing",
                StatusCodes.Status401Unauthorized);
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
}
