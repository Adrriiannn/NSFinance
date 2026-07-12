using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class MfaTrustedDeviceService(
    AppDbContext dbContext,
    TokenSecretService tokenSecretService,
    IOptions<IdentitySecurityOptions> options)
{
    private readonly IdentitySecurityOptions _options = options.Value;

    public async Task<MfaTrustedDeviceCredentialResponse?> IssueAsync(
        Guid userId,
        DeviceContextDto? deviceContext,
        CancellationToken cancellationToken)
    {
        var fingerprint = NormalizeFingerprint(deviceContext?.DeviceFingerprint);
        if (fingerprint is null)
        {
            return null;
        }

        var device = await dbContext.Devices.SingleOrDefaultAsync(
            x => x.UserId == userId && x.DeviceFingerprint == fingerprint,
            cancellationToken);
        if (device is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var activeCredentials = await dbContext.MfaTrustedDevices
            .Where(x => x.UserId == userId && x.DeviceId == device.Id && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var credential in activeCredentials)
        {
            credential.RevokedUtc = now;
            credential.RevocationReason = "replaced";
        }

        var token = tokenSecretService.CreateToken();
        var expiresUtc = now.AddDays(_options.MfaTrustedDeviceLifetimeDays);
        dbContext.MfaTrustedDevices.Add(new MfaTrustedDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = device.Id,
            TokenHash = tokenSecretService.HashToken(token),
            CreatedUtc = now,
            ExpiresUtc = expiresUtc
        });
        device.IsTrusted = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MfaTrustedDeviceCredentialResponse(token, expiresUtc);
    }

    public async Task<bool> ValidateAsync(
        Guid userId,
        string? token,
        DeviceContextDto? deviceContext,
        CancellationToken cancellationToken)
    {
        var normalizedToken = token?.Trim();
        var fingerprint = NormalizeFingerprint(deviceContext?.DeviceFingerprint);
        if (string.IsNullOrWhiteSpace(normalizedToken) || fingerprint is null)
        {
            return false;
        }

        var tokenHash = tokenSecretService.HashToken(normalizedToken);
        var credential = await dbContext.MfaTrustedDevices
            .Include(x => x.Device)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (credential is null
            || credential.UserId != userId
            || credential.Device?.UserId != userId
            || !string.Equals(credential.Device.DeviceFingerprint, fingerprint, StringComparison.Ordinal)
            || credential.RevokedUtc is not null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (credential.ExpiresUtc <= now)
        {
            credential.RevokedUtc = now;
            credential.RevocationReason = "expired";
            credential.Device.IsTrusted = false;
            return false;
        }

        credential.LastUsedUtc = now;
        credential.Device.LastSeenUtc = now;
        return true;
    }

    public async Task RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var credentials = await dbContext.MfaTrustedDevices
            .Include(x => x.Device)
            .Where(x => x.UserId == userId && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var credential in credentials)
        {
            credential.RevokedUtc = now;
            credential.RevocationReason = reason;
            if (credential.Device is not null)
            {
                credential.Device.IsTrusted = false;
            }
        }

        if (credentials.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? NormalizeFingerprint(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized[..Math.Min(normalized.Length, 180)];
    }
}
