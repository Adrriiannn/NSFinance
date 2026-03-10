using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class SessionService(
    AppDbContext dbContext,
    JwtTokenService jwtTokenService,
    TokenSecretService tokenSecretService,
    IOptions<JwtOptions> options,
    IRequestContextAccessor requestContext,
    ILogger<SessionService> logger)
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokenResponse> CreateSessionAsync(
        User user,
        DeviceContextDto? deviceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var device = await ResolveDeviceAsync(user.Id, deviceContext, now, cancellationToken);
        var familyId = Guid.NewGuid();

        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = device?.Id,
            CreatedUtc = now,
            LastSeenUtc = now,
            ExpiresUtc = now.AddDays(_options.RefreshTokenDays),
            DeviceLabel = BuildBestEffortDeviceLabel(device?.DeviceLabel, deviceContext, requestContext.Platform),
            Platform = deviceContext?.Platform ?? requestContext.Platform,
            OsVersion = deviceContext?.OsVersion,
            AppVersion = deviceContext?.AppVersion ?? requestContext.AppVersion,
            IpAddress = requestContext.IpAddress,
            RefreshTokenFamilyId = familyId
        };

        var refreshToken = tokenSecretService.CreateToken();
        var refreshEntity = new SessionRefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            FamilyId = familyId,
            TokenHash = tokenSecretService.HashToken(refreshToken),
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(_options.RefreshTokenDays)
        };

        session.RefreshTokens.Add(refreshEntity);

        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        var (accessToken, accessTokenExpiresAtUtc) = jwtTokenService.CreateAccessToken(user, session.Id);

        return new AuthTokenResponse(
            accessToken,
            accessTokenExpiresAtUtc,
            refreshToken,
            refreshEntity.ExpiresUtc,
            session.Id,
            MapUserProfile(user));
    }

    public async Task<ServiceResult<AuthTokenResponse>> RefreshAsync(
        string refreshToken,
        DeviceContextDto? deviceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var tokenHash = tokenSecretService.HashToken(refreshToken);

        var storedToken = await dbContext.SessionRefreshTokens
            .Include(x => x.Session)
            .ThenInclude(x => x!.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null || storedToken.Session?.User is null)
        {
            return ServiceResult<AuthTokenResponse>.Fail("Invalid session token.", "invalid_refresh_token", StatusCodes.Status401Unauthorized);
        }

        if (storedToken.UsedUtc is not null || storedToken.RevokedUtc is not null)
        {
            await RevokeFamilyAsync(storedToken.FamilyId, "refresh_token_reuse", cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail("Session token is no longer valid.", "refresh_token_reused", StatusCodes.Status401Unauthorized);
        }

        if (storedToken.ExpiresUtc <= now)
        {
            storedToken.RevokedUtc = now;
            storedToken.RevocationReason = "expired";
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail("Session token expired.", "refresh_token_expired", StatusCodes.Status401Unauthorized);
        }

        var session = storedToken.Session;
        var user = session.User!;
        if (session.RevokedUtc is not null || session.ExpiresUtc <= now || user.IsDisabled || user.IsSuspended)
        {
            return ServiceResult<AuthTokenResponse>.Fail("Session is no longer valid.", "session_revoked", StatusCodes.Status401Unauthorized);
        }

        var device = await ResolveDeviceAsync(user.Id, deviceContext, now, cancellationToken);
        session.LastSeenUtc = now;
        session.ExpiresUtc = now.AddDays(_options.RefreshTokenDays);
        session.DeviceId = device?.Id ?? session.DeviceId;
        session.DeviceLabel = BuildBestEffortDeviceLabel(device?.DeviceLabel, deviceContext, session.Platform);
        session.Platform = deviceContext?.Platform ?? session.Platform;
        session.OsVersion = deviceContext?.OsVersion ?? session.OsVersion;
        session.AppVersion = deviceContext?.AppVersion ?? session.AppVersion;
        session.IpAddress = requestContext.IpAddress;

        storedToken.UsedUtc = now;

        var newRefreshToken = tokenSecretService.CreateToken();
        var replacementToken = new SessionRefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            FamilyId = storedToken.FamilyId,
            ParentTokenId = storedToken.Id,
            TokenHash = tokenSecretService.HashToken(newRefreshToken),
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(_options.RefreshTokenDays)
        };

        storedToken.ReplacedByTokenId = replacementToken.Id;
        dbContext.SessionRefreshTokens.Add(replacementToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var (accessToken, accessTokenExpiresAtUtc) = jwtTokenService.CreateAccessToken(user, session.Id);
        logger.LogInformation("Rotated refresh token for session {SessionId} and user {UserId}", session.Id, user.Id);

        return ServiceResult<AuthTokenResponse>.Ok(new AuthTokenResponse(
            accessToken,
            accessTokenExpiresAtUtc,
            newRefreshToken,
            replacementToken.ExpiresUtc,
            session.Id,
            MapUserProfile(user)));
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return await dbContext.Sessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedUtc == null && x.ExpiresUtc > now)
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => new SessionDto(
                x.Id,
                x.CreatedUtc,
                x.ExpiresUtc,
                x.LastSeenUtc,
                x.RevokedUtc,
                x.DeviceLabel,
                x.Platform,
                x.OsVersion,
                x.AppVersion,
                currentSessionId.HasValue && x.Id == currentSessionId.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedUtc == null, cancellationToken);

        if (session is null)
        {
            return 0;
        }

        await RevokeSessionEntityAsync(session, reason, cancellationToken);
        return 1;
    }

    public async Task<int> RevokeAllSessionsAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.Sessions
            .Where(x => x.UserId == userId && x.RevokedUtc == null && (!exceptSessionId.HasValue || x.Id != exceptSessionId.Value))
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            await RevokeSessionEntityAsync(session, reason, cancellationToken, saveImmediately: false);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    public async Task RevokeAllSessionsForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        await RevokeAllSessionsAsync(userId, reason, exceptSessionId: null, cancellationToken);
    }

    private async Task RevokeSessionEntityAsync(
        Session session,
        string reason,
        CancellationToken cancellationToken,
        bool saveImmediately = true)
    {
        var now = DateTime.UtcNow;
        session.RevokedUtc = now;
        session.RevocationReason = reason;

        var refreshTokens = await dbContext.SessionRefreshTokens
            .Where(x => x.SessionId == session.Id && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in refreshTokens)
        {
            token.RevokedUtc = now;
            token.RevocationReason = reason;
        }

        if (saveImmediately)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var familyTokens = await dbContext.SessionRefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in familyTokens)
        {
            token.RevokedUtc = now;
            token.RevocationReason = reason;
        }

        var familySessions = await dbContext.Sessions
            .Where(x => x.RefreshTokenFamilyId == familyId && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in familySessions)
        {
            session.RevokedUtc = now;
            session.RevocationReason = reason;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Device?> ResolveDeviceAsync(
        Guid userId,
        DeviceContextDto? deviceContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var fingerprint = NormalizeFingerprint(deviceContext?.DeviceFingerprint);
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var device = await dbContext.Devices
            .SingleOrDefaultAsync(x => x.UserId == userId && x.DeviceFingerprint == fingerprint, cancellationToken);

        if (device is null)
        {
            device = new Device
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceFingerprint = fingerprint,
            DeviceLabel = NormalizeDeviceLabel(deviceContext?.DeviceLabel),
                Platform = NormalizeNullable(deviceContext?.Platform),
                OsVersion = NormalizeNullable(deviceContext?.OsVersion),
                AppVersion = NormalizeNullable(deviceContext?.AppVersion),
                FirstSeenUtc = now,
                LastSeenUtc = now,
                IsTrusted = false
            };

            dbContext.Devices.Add(device);
            await dbContext.SaveChangesAsync(cancellationToken);
            return device;
        }

        device.LastSeenUtc = now;
        device.DeviceLabel = NormalizeDeviceLabel(deviceContext?.DeviceLabel);
        device.Platform = NormalizeNullable(deviceContext?.Platform) ?? device.Platform;
        device.OsVersion = NormalizeNullable(deviceContext?.OsVersion) ?? device.OsVersion;
        device.AppVersion = NormalizeNullable(deviceContext?.AppVersion) ?? device.AppVersion;
        await dbContext.SaveChangesAsync(cancellationToken);
        return device;
    }

    private static UserProfileDto MapUserProfile(User user)
    {
        return new UserProfileDto(
            user.Id,
            user.PrimaryEmail,
            user.FullName,
            user.DisplayName,
            user.Handle,
            user.ProfileImageUrl,
            user.ProfileSubtitle,
            user.Timezone,
            user.Locale,
            user.PreferredCurrency,
            user.Role,
            user.EmailVerified,
            user.OnboardingStatus,
            user.BiometricUnlockEnabled,
            user.TwoFactorEnabled,
            user.PlanTier,
            user.CreatedUtc,
            user.LastLoginUtc);
    }

    private static string NormalizeDeviceLabel(string? input)
    {
        var value = NormalizeNullable(input);
        return value ?? "Unknown Device";
    }

    private static string BuildBestEffortDeviceLabel(
        string? persistedLabel,
        DeviceContextDto? deviceContext,
        string? platformFallback)
    {
        var normalizedPersisted = NormalizeNullable(persistedLabel);
        if (!string.IsNullOrWhiteSpace(normalizedPersisted) && normalizedPersisted != "Unknown Device")
        {
            return normalizedPersisted;
        }

        var contextLabel = NormalizeNullable(deviceContext?.DeviceLabel);
        if (!string.IsNullOrWhiteSpace(contextLabel))
        {
            return contextLabel;
        }

        var platform = NormalizeNullable(deviceContext?.Platform) ?? NormalizeNullable(platformFallback);
        if (string.IsNullOrWhiteSpace(platform))
        {
            return "Unknown Device";
        }

        var osVersion = NormalizeNullable(deviceContext?.OsVersion);
        return osVersion is null
            ? $"{platform} device"
            : $"{platform} {osVersion}";
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }
}
