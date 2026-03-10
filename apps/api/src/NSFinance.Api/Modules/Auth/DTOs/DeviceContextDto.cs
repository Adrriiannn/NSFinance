namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record DeviceContextDto(
    string? DeviceFingerprint,
    string? DeviceLabel,
    string? Platform,
    string? OsVersion,
    string? AppVersion);
