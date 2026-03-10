namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record SessionDto(
    Guid Id,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    DateTime LastSeenUtc,
    DateTime? RevokedUtc,
    string DeviceLabel,
    string? Platform,
    string? OsVersion,
    string? AppVersion,
    bool IsCurrentSession);
