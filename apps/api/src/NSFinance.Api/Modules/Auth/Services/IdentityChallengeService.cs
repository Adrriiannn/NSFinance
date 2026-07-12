using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class IdentityChallengeService(
    AppDbContext dbContext,
    IIdentityCodeService codeService,
    TokenSecretService tokenSecretService,
    TransactionalMessageService messageService,
    IRequestContextAccessor requestContext,
    IOptions<IdentitySecurityOptions> options)
{
    private readonly IdentitySecurityOptions _options = options.Value;

    public async Task<ServiceResult<IdentityChallengeCreated>> CreateEmailCodeAsync(
        User? user,
        string destination,
        string purpose,
        string templateKey,
        CancellationToken cancellationToken)
    {
        if (!messageService.IsEmailConfigured)
        {
            return ServiceResult<IdentityChallengeCreated>.Fail(
                "Security email delivery is temporarily unavailable.",
                "identity_delivery_unavailable",
                StatusCodes.Status503ServiceUnavailable);
        }

        var normalizedDestination = destination.Trim().ToLowerInvariant();
        var destinationHash = codeService.HashDestination(IdentityChannels.Email, normalizedDestination);
        var now = DateTime.UtcNow;
        var cooldownSeconds = Math.Max(1, _options.ResendCooldownSeconds);
        var latest = await dbContext.IdentityChallenges
            .Where(x => x.DestinationHash == destinationHash && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null)
        {
            var retryAfterUtc = latest.CreatedUtc.AddSeconds(cooldownSeconds);
            if (retryAfterUtc > now)
            {
                var remainingSeconds = Math.Max(1, (int)Math.Ceiling((retryAfterUtc - now).TotalSeconds));
                return ServiceResult<IdentityChallengeCreated>.Fail(
                    $"Please wait {remainingSeconds} seconds before requesting another code.",
                    "identity_code_resend_wait",
                    StatusCodes.Status429TooManyRequests);
            }
        }

        var activeChallenges = await dbContext.IdentityChallenges
            .Where(x =>
                x.DestinationHash == destinationHash
                && x.Purpose == purpose
                && x.ConsumedUtc == null
                && x.SupersededUtc == null
                && x.ExpiresUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var activeChallenge in activeChallenges)
        {
            activeChallenge.SupersededUtc = now;
            activeChallenge.ConcurrencyToken = Guid.NewGuid();
        }

        var challengeId = Guid.NewGuid();
        var code = codeService.CreateSixDigitCode();
        var lifetimeMinutes = Math.Max(1, _options.ChallengeLifetimeMinutes);
        var challenge = new IdentityChallenge
        {
            Id = challengeId,
            UserId = user?.Id,
            Purpose = purpose,
            Channel = IdentityChannels.Email,
            DestinationHash = destinationHash,
            SecretHash = codeService.HashChallengeSecret(challengeId, code),
            FailedAttempts = 0,
            MaxAttempts = Math.Max(1, _options.MaxCodeAttempts),
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(lifetimeMinutes),
            RequestedByIp = requestContext.IpAddress,
            ConcurrencyToken = Guid.NewGuid()
        };
        dbContext.IdentityChallenges.Add(challenge);

        if (user is not null)
        {
            messageService.QueueEmail(
                user.Id,
                challenge.Id,
                normalizedDestination,
                templateKey,
                new IdentityEmailPayload(
                    user.FullName,
                    code,
                    lifetimeMinutes,
                    now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<IdentityChallengeCreated>.Ok(new IdentityChallengeCreated(
            challenge.Id,
            challenge.ExpiresUtc,
            cooldownSeconds,
            TransactionalMessageStatuses.Pending));
    }

    public async Task<ServiceResult<IdentityChallengeVerified>> VerifyCodeAsync(
        Guid challengeId,
        string purpose,
        string code,
        bool issueGrant,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.IdentityChallenges
            .SingleOrDefaultAsync(x => x.Id == challengeId && x.Purpose == purpose, cancellationToken);
        var now = DateTime.UtcNow;

        if (!CanAttempt(challenge, now) || challenge!.UserId is null)
        {
            return InvalidCodeResult();
        }

        if (!IsSixDigitCode(code) || !codeService.VerifyChallengeSecret(challenge.Id, code, challenge.SecretHash))
        {
            challenge.FailedAttempts++;
            challenge.ConcurrencyToken = Guid.NewGuid();
            if (challenge.FailedAttempts >= challenge.MaxAttempts)
            {
                challenge.ConsumedUtc = now;
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return InvalidCodeResult();
            }

            return InvalidCodeResult();
        }

        string? grantToken = null;
        challenge.VerifiedUtc = now;
        challenge.ConcurrencyToken = Guid.NewGuid();
        if (issueGrant)
        {
            grantToken = tokenSecretService.CreateToken();
            challenge.GrantHash = tokenSecretService.HashToken(grantToken);
            challenge.GrantExpiresUtc = now.AddMinutes(Math.Max(1, _options.RecoveryGrantLifetimeMinutes));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return InvalidCodeResult();
        }

        return ServiceResult<IdentityChallengeVerified>.Ok(new IdentityChallengeVerified(challenge, grantToken));
    }

    public async Task<ServiceResult<IdentityChallenge>> ConsumeGrantAsync(
        Guid challengeId,
        string purpose,
        string grantToken,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var grantHash = string.IsNullOrWhiteSpace(grantToken)
            ? string.Empty
            : tokenSecretService.HashToken(grantToken.Trim());
        var challenge = await dbContext.IdentityChallenges
            .SingleOrDefaultAsync(x => x.Id == challengeId && x.Purpose == purpose, cancellationToken);

        if (challenge is null
            || challenge.UserId is null
            || challenge.VerifiedUtc is null
            || challenge.ConsumedUtc is not null
            || challenge.SupersededUtc is not null
            || challenge.GrantExpiresUtc is null
            || challenge.GrantExpiresUtc <= now
            || string.IsNullOrWhiteSpace(challenge.GrantHash)
            || !FixedTimeTokenHashEquals(grantHash, challenge.GrantHash))
        {
            return ServiceResult<IdentityChallenge>.Fail(
                "Recovery authorization is invalid or expired.",
                "recovery_grant_invalid",
                StatusCodes.Status400BadRequest);
        }

        challenge.ConsumedUtc = now;
        challenge.ConcurrencyToken = Guid.NewGuid();
        return ServiceResult<IdentityChallenge>.Ok(challenge);
    }

    public async Task<ServiceResult<IdentityChallenge>> VerifyCodeForCompletionAsync(
        Guid challengeId,
        Guid userId,
        string purpose,
        string code,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.IdentityChallenges
            .SingleOrDefaultAsync(
                x => x.Id == challengeId && x.Purpose == purpose && x.UserId == userId,
                cancellationToken);
        var now = DateTime.UtcNow;
        if (!CanAttempt(challenge, now))
        {
            return ServiceResult<IdentityChallenge>.Fail(
                "The code is invalid or expired.",
                "identity_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        var activeChallenge = challenge!;
        if (!IsSixDigitCode(code) || !codeService.VerifyChallengeSecret(activeChallenge.Id, code, activeChallenge.SecretHash))
        {
            activeChallenge.FailedAttempts++;
            activeChallenge.ConcurrencyToken = Guid.NewGuid();
            if (activeChallenge.FailedAttempts >= activeChallenge.MaxAttempts)
            {
                activeChallenge.ConsumedUtc = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<IdentityChallenge>.Fail(
                "The code is invalid or expired.",
                "identity_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        activeChallenge.VerifiedUtc = now;
        activeChallenge.ConsumedUtc = now;
        activeChallenge.ConcurrencyToken = Guid.NewGuid();
        return ServiceResult<IdentityChallenge>.Ok(activeChallenge);
    }

    private static bool CanAttempt(IdentityChallenge? challenge, DateTime now)
    {
        return challenge is not null
            && challenge.VerifiedUtc is null
            && challenge.ConsumedUtc is null
            && challenge.SupersededUtc is null
            && challenge.ExpiresUtc > now
            && challenge.FailedAttempts < challenge.MaxAttempts;
    }

    private static bool IsSixDigitCode(string value)
    {
        return value.Length == 6 && value.All(char.IsDigit);
    }

    private static bool FixedTimeTokenHashEquals(string actual, string expected)
    {
        try
        {
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ServiceResult<IdentityChallengeVerified> InvalidCodeResult()
    {
        return ServiceResult<IdentityChallengeVerified>.Fail(
            "The code is invalid or expired.",
            "identity_code_invalid",
            StatusCodes.Status400BadRequest);
    }
}
