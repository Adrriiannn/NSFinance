using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class MicrosoftAuthService(
    IOptions<MicrosoftAuthOptions> options,
    IMicrosoftAccessTokenVerifier tokenVerifier,
    ILogger<MicrosoftAuthService> logger)
{
    private readonly MicrosoftAuthOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;
    public string? ClientId => IsConfigured ? _options.ClientId : null;
    public string Authority => _options.Authority;
    public string? Scope => IsConfigured ? _options.ApiScope : null;

    public async Task<ServiceResult<MicrosoftIdentityPayload>> VerifyAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return ServiceResult<MicrosoftIdentityPayload>.Fail(
                "Microsoft sign-in is not configured.",
                "microsoft_sign_in_not_configured",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ServiceResult<MicrosoftIdentityPayload>.Fail(
                "Microsoft sign-in token is required.",
                "microsoft_token_required",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            var principal = await tokenVerifier.ValidateAsync(accessToken, cancellationToken);
            if (!HasDelegatedScope(principal, MicrosoftAuthOptions.DelegatedScopeName)
                || !IsExpectedClient(principal, _options.ClientId))
            {
                return ServiceResult<MicrosoftIdentityPayload>.Fail(
                    "Microsoft sign-in token is not authorized for NSFinance.",
                    "microsoft_token_invalid",
                    StatusCodes.Status401Unauthorized);
            }

            var tenantId = ReadClaim(principal, "tid");
            var objectId = ReadClaim(principal, "oid");
            var subject = ReadClaim(principal, "sub");
            var email = ReadClaim(principal, "email")
                ?? ReadClaim(principal, "preferred_username")
                ?? ReadClaim(principal, "upn");

            if (string.IsNullOrWhiteSpace(tenantId)
                || string.IsNullOrWhiteSpace(objectId)
                || string.IsNullOrWhiteSpace(subject))
            {
                return ServiceResult<MicrosoftIdentityPayload>.Fail(
                    "Microsoft account identity is incomplete.",
                    "microsoft_identity_incomplete",
                    StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(email) || !LooksLikeEmail(email))
            {
                return ServiceResult<MicrosoftIdentityPayload>.Fail(
                    "Microsoft did not provide an addressable email. Add an email to the Microsoft account and try again.",
                    "microsoft_email_missing",
                    StatusCodes.Status400BadRequest);
            }

            return ServiceResult<MicrosoftIdentityPayload>.Ok(new MicrosoftIdentityPayload(
                $"{tenantId}:{objectId}",
                tenantId,
                objectId,
                email.Trim().ToLowerInvariant(),
                ReadClaim(principal, "name"),
                ReadClaim(principal, "given_name"),
                ReadClaim(principal, "family_name")));
        }
        catch (SecurityTokenException exception)
        {
            logger.LogWarning(exception, "Microsoft access token validation failed.");
            return ServiceResult<MicrosoftIdentityPayload>.Fail(
                "Microsoft sign-in could not be verified.",
                "microsoft_token_invalid",
                StatusCodes.Status401Unauthorized);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Microsoft identity verification failed.");
            return ServiceResult<MicrosoftIdentityPayload>.Fail(
                "Microsoft sign-in is temporarily unavailable.",
                "microsoft_sign_in_unavailable",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string? ReadClaim(ClaimsPrincipal principal, string name)
    {
        return principal.FindFirst(name)?.Value;
    }

    private static bool HasDelegatedScope(ClaimsPrincipal principal, string requiredScope)
    {
        return (ReadClaim(principal, "scp") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(requiredScope, StringComparer.Ordinal);
    }

    private static bool IsExpectedClient(ClaimsPrincipal principal, string clientId)
    {
        var authorizedParty = ReadClaim(principal, "azp") ?? ReadClaim(principal, "appid");
        return string.Equals(authorizedParty, clientId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 3 && value.IndexOf('.', at) > at + 1;
    }
}
