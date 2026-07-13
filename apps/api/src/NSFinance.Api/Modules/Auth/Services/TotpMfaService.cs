using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using OtpNet;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class TotpMfaService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    MfaSecretProtector secretProtector,
    IIdentityCodeService identityCodeService,
    TokenSecretService tokenSecretService,
    MfaTrustedDeviceService trustedDeviceService,
    IAuditService auditService,
    IOptions<IdentitySecurityOptions> options)
{
    private const string TotpMethod = "totp";
    private const string RecoveryCodeMethod = "recovery_code";
    private const string RecoveryAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private readonly IdentitySecurityOptions _options = options.Value;

    public Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.TotpAuthenticators.AnyAsync(
            x => x.UserId == userId && x.VerifiedUtc != null && x.DisabledUtc == null,
            cancellationToken);
    }

    public async Task<ServiceResult<MfaStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<MfaStatusResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var authenticator = await dbContext.TotpAuthenticators
            .AsNoTracking()
            .Include(x => x.RecoveryCodes)
            .Where(x => x.UserId == userId && x.VerifiedUtc != null && x.DisabledUtc == null)
            .OrderByDescending(x => x.VerifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return ServiceResult<MfaStatusResponse>.Ok(new MfaStatusResponse(
            authenticator is not null,
            authenticator is null ? null : TotpMethod,
            authenticator?.RecoveryCodes.Count(x => x.UsedUtc == null) ?? 0));
    }

    public async Task<ServiceResult<BeginTotpEnrollmentResponse>> BeginEnrollmentAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<BeginTotpEnrollmentResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<BeginTotpEnrollmentResponse>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        var alreadyEnabled = await dbContext.TotpAuthenticators
            .AnyAsync(x => x.UserId == userId && x.VerifiedUtc != null && x.DisabledUtc == null, cancellationToken);
        if (alreadyEnabled)
        {
            return ServiceResult<BeginTotpEnrollmentResponse>.Fail(
                "Authenticator MFA is already enabled.",
                "mfa_already_enabled",
                StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var pending = await dbContext.TotpAuthenticators
            .Where(x => x.UserId == userId && x.VerifiedUtc == null && x.DisabledUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var stale in pending)
        {
            stale.DisabledUtc = now;
        }

        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(secretBytes);
        var expiresUtc = now.AddMinutes(10);
        var authenticator = new TotpAuthenticator
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EncryptedSecret = secretProtector.Protect(secret),
            CreatedUtc = now,
            EnrollmentExpiresUtc = expiresUtc
        };
        dbContext.TotpAuthenticators.Add(authenticator);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "totp_enrollment_started",
            targetEntityType: "totp_authenticator",
            targetEntityId: authenticator.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        var issuer = string.IsNullOrWhiteSpace(_options.TotpIssuer) ? "NSFinance" : _options.TotpIssuer.Trim();
        var label = Uri.EscapeDataString($"{issuer}:{user.PrimaryEmail}");
        var otpAuthUri =
            $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secret)}" +
            $"&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

        return ServiceResult<BeginTotpEnrollmentResponse>.Ok(new BeginTotpEnrollmentResponse(
            authenticator.Id,
            secret,
            otpAuthUri,
            expiresUtc));
    }

    public async Task<ServiceResult<ConfirmTotpEnrollmentResponse>> ConfirmEnrollmentAsync(
        ConfirmTotpEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<ConfirmTotpEnrollmentResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var authenticator = await dbContext.TotpAuthenticators
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Id == request.AuthenticatorId && x.UserId == userId && x.DisabledUtc == null,
                cancellationToken);
        var now = DateTime.UtcNow;
        if (authenticator is null
            || authenticator.VerifiedUtc is not null
            || authenticator.EnrollmentExpiresUtc <= now)
        {
            return ServiceResult<ConfirmTotpEnrollmentResponse>.Fail(
                "Authenticator enrollment is invalid or expired.",
                "mfa_enrollment_invalid",
                StatusCodes.Status400BadRequest);
        }

        if (!TryVerifyTotp(authenticator, request.Code, out var timeStep))
        {
            return ServiceResult<ConfirmTotpEnrollmentResponse>.Fail(
                "The authenticator code is invalid.",
                "mfa_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        var rawRecoveryCodes = Enumerable.Range(0, Math.Max(1, _options.RecoveryCodeCount))
            .Select(_ => CreateRecoveryCode())
            .ToArray();
        foreach (var rawCode in rawRecoveryCodes)
        {
            dbContext.MfaRecoveryCodes.Add(new MfaRecoveryCode
            {
                Id = Guid.NewGuid(),
                TotpAuthenticatorId = authenticator.Id,
                CodeHash = identityCodeService.HashRecoveryCode(authenticator.Id, rawCode),
                CreatedUtc = now
            });
        }

        authenticator.VerifiedUtc = now;
        authenticator.LastAcceptedTimeStep = timeStep;
        authenticator.User!.TwoFactorEnabled = true;
        authenticator.User.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "totp_enabled",
            targetEntityType: "totp_authenticator",
            targetEntityId: authenticator.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new { recoveryCodeCount = rawRecoveryCodes.Length },
            cancellationToken);

        return ServiceResult<ConfirmTotpEnrollmentResponse>.Ok(new ConfirmTotpEnrollmentResponse(
            true,
            rawRecoveryCodes));
    }

    public async Task<ServiceResult<MfaLoginChallengeResponse>> CreateLoginChallengeAsync(
        User user,
        CancellationToken cancellationToken)
    {
        return await CreateChallengeAsync(user, IdentityChallengePurposes.MfaLogin, cancellationToken);
    }

    public async Task<ServiceResult<MfaLoginChallengeResponse>> CreateSessionResumeChallengeAsync(
        User user,
        CancellationToken cancellationToken)
    {
        return await CreateChallengeAsync(user, IdentityChallengePurposes.MfaSessionResume, cancellationToken);
    }

    private async Task<ServiceResult<MfaLoginChallengeResponse>> CreateChallengeAsync(
        User user,
        string purpose,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var active = await dbContext.IdentityChallenges
            .Where(x => x.UserId == user.Id
                && x.Purpose == purpose
                && x.ConsumedUtc == null
                && x.SupersededUtc == null
                && x.ExpiresUtc > now)
            .ToListAsync(cancellationToken);
        foreach (var challenge in active)
        {
            challenge.SupersededUtc = now;
            challenge.ConcurrencyToken = Guid.NewGuid();
        }

        var challengeToken = tokenSecretService.CreateToken();
        var challengeId = Guid.NewGuid();
        var expiresUtc = now.AddMinutes(Math.Max(1, _options.MfaChallengeLifetimeMinutes));
        dbContext.IdentityChallenges.Add(new IdentityChallenge
        {
            Id = challengeId,
            UserId = user.Id,
            Purpose = purpose,
            Channel = IdentityChannels.Authenticator,
            DestinationHash = identityCodeService.HashDestination(IdentityChannels.Authenticator, user.Id.ToString("N")),
            SecretHash = tokenSecretService.HashToken(challengeToken),
            FailedAttempts = 0,
            MaxAttempts = Math.Max(1, _options.MaxCodeAttempts),
            CreatedUtc = now,
            ExpiresUtc = expiresUtc,
            ConcurrencyToken = Guid.NewGuid()
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<MfaLoginChallengeResponse>.Ok(new MfaLoginChallengeResponse(
            challengeId,
            challengeToken,
            expiresUtc,
            [TotpMethod, RecoveryCodeMethod],
            MaskAccountEmail(user.PrimaryEmail)));
    }

    private static string MaskAccountEmail(string email)
    {
        var normalized = email.Trim();
        var separatorIndex = normalized.LastIndexOf('@');
        if (separatorIndex <= 0 || separatorIndex == normalized.Length - 1)
        {
            return "****";
        }

        var localPart = normalized[..separatorIndex];
        var domain = normalized[(separatorIndex + 1)..];
        var visibleLocalLength = Math.Min(3, localPart.Length);
        return $"{localPart[..visibleLocalLength]}****@{domain}";
    }

    public async Task<ServiceResult<User>> VerifyLoginChallengeAsync(
        VerifyMfaLoginRequest request,
        CancellationToken cancellationToken)
    {
        return await VerifyChallengeAsync(
            request,
            IdentityChallengePurposes.MfaLogin,
            cancellationToken);
    }

    public async Task<ServiceResult<User>> VerifySessionResumeChallengeAsync(
        VerifyMfaLoginRequest request,
        CancellationToken cancellationToken)
    {
        return await VerifyChallengeAsync(
            request,
            IdentityChallengePurposes.MfaSessionResume,
            cancellationToken);
    }

    private async Task<ServiceResult<User>> VerifyChallengeAsync(
        VerifyMfaLoginRequest request,
        string purpose,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.IdentityChallenges
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId && x.Purpose == purpose,
                cancellationToken);
        var now = DateTime.UtcNow;
        if (challenge is null
            || challenge.User is null
            || !FixedTimeTokenHashEquals(tokenSecretService.HashToken(request.ChallengeToken), challenge.SecretHash))
        {
            return InvalidMfaResult();
        }

        if (challenge.ExpiresUtc <= now)
        {
            return ExpiredMfaResult();
        }

        if (challenge.ConsumedUtc is not null
            || challenge.SupersededUtc is not null
            || challenge.FailedAttempts >= challenge.MaxAttempts)
        {
            return InvalidMfaResult();
        }

        var authenticator = await dbContext.TotpAuthenticators
            .Include(x => x.RecoveryCodes)
            .Where(x => x.UserId == challenge.UserId && x.VerifiedUtc != null && x.DisabledUtc == null)
            .OrderByDescending(x => x.VerifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (authenticator is null)
        {
            return InvalidMfaResult();
        }

        var accepted = false;
        if (string.Equals(request.Method, TotpMethod, StringComparison.Ordinal))
        {
            accepted = TryVerifyTotp(authenticator, request.Code, out var timeStep)
                && (!authenticator.LastAcceptedTimeStep.HasValue || timeStep > authenticator.LastAcceptedTimeStep.Value);
            if (accepted)
            {
                authenticator.LastAcceptedTimeStep = timeStep;
            }
        }
        else if (string.Equals(request.Method, RecoveryCodeMethod, StringComparison.Ordinal))
        {
            var recoveryCode = authenticator.RecoveryCodes.FirstOrDefault(x =>
                x.UsedUtc == null
                && identityCodeService.VerifyRecoveryCode(authenticator.Id, request.Code, x.CodeHash));
            if (recoveryCode is not null)
            {
                recoveryCode.UsedUtc = now;
                accepted = true;
            }
        }

        if (!accepted)
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= challenge.MaxAttempts)
            {
                challenge.ConsumedUtc = now;
            }
            challenge.ConcurrencyToken = Guid.NewGuid();
            await dbContext.SaveChangesAsync(cancellationToken);
            return InvalidMfaCodeResult();
        }

        challenge.ConsumedUtc = now;
        challenge.ConcurrencyToken = Guid.NewGuid();
        if (purpose == IdentityChallengePurposes.MfaLogin)
        {
            challenge.User.LastLoginUtc = now;
            challenge.User.UpdatedUtc = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<User>.Ok(challenge.User);
    }

    public async Task<ServiceResult> DisableAsync(DisableMfaRequest request, CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var authenticator = await dbContext.TotpAuthenticators
            .Include(x => x.User)
            .Include(x => x.RecoveryCodes)
            .Where(x => x.UserId == userId && x.VerifiedUtc != null && x.DisabledUtc == null)
            .OrderByDescending(x => x.VerifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (authenticator is null)
        {
            return ServiceResult.Fail("Authenticator MFA is not enabled.", "mfa_not_enabled", StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var accepted = false;
        if (string.Equals(request.Method, TotpMethod, StringComparison.Ordinal))
        {
            accepted = TryVerifyTotp(authenticator, request.Code, out var timeStep)
                && (!authenticator.LastAcceptedTimeStep.HasValue || timeStep > authenticator.LastAcceptedTimeStep.Value);
            if (accepted)
            {
                authenticator.LastAcceptedTimeStep = timeStep;
            }
        }
        else if (string.Equals(request.Method, RecoveryCodeMethod, StringComparison.Ordinal))
        {
            var recoveryCode = authenticator.RecoveryCodes.FirstOrDefault(x =>
                x.UsedUtc == null
                && identityCodeService.VerifyRecoveryCode(authenticator.Id, request.Code, x.CodeHash));
            if (recoveryCode is not null)
            {
                recoveryCode.UsedUtc = now;
                accepted = true;
            }
        }

        if (!accepted)
        {
            return ServiceResult.Fail("The verification code is invalid.", "mfa_code_invalid", StatusCodes.Status400BadRequest);
        }

        authenticator.DisabledUtc = now;
        authenticator.User!.TwoFactorEnabled = false;
        authenticator.User.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await trustedDeviceService.RevokeAllForUserAsync(
            userId,
            "mfa_disabled",
            cancellationToken);
        await auditService.WriteEventAsync(
            category: "security",
            eventName: "totp_disabled",
            targetEntityType: "totp_authenticator",
            targetEntityId: authenticator.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult.Ok();
    }

    private bool TryVerifyTotp(TotpAuthenticator authenticator, string code, out long timeStep)
    {
        timeStep = 0;
        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            return false;
        }

        var secret = Base32Encoding.ToBytes(secretProtector.Unprotect(authenticator.EncryptedSecret));
        var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.VerifyTotp(code, out timeStep, VerificationWindow.RfcSpecifiedNetworkDelay);
    }

    private static string CreateRecoveryCode()
    {
        Span<char> characters = stackalloc char[8];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        }

        return $"{new string(characters[..4])}-{new string(characters[4..])}";
    }

    private static bool FixedTimeTokenHashEquals(string actual, string expected)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ServiceResult<User> InvalidMfaResult()
    {
        return ServiceResult<User>.Fail(
            "The verification code is invalid or expired.",
            "mfa_challenge_invalid",
            StatusCodes.Status400BadRequest);
    }

    private static ServiceResult<User> ExpiredMfaResult()
    {
        return ServiceResult<User>.Fail(
            "The security check has expired. Sign in again to continue.",
            "mfa_challenge_expired",
            StatusCodes.Status400BadRequest);
    }

    private static ServiceResult<User> InvalidMfaCodeResult()
    {
        return ServiceResult<User>.Fail(
            "The verification code is invalid.",
            "mfa_code_invalid",
            StatusCodes.Status400BadRequest);
    }
}
