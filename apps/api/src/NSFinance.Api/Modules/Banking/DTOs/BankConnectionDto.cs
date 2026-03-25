namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankConnectionDto(
    Guid Id,
    string Provider,
    string? ProviderId,
    string ProviderEnvironment,
    string? ProviderDisplayName,
    string? ProviderIconUrl,
    string? ProviderLogoUrl,
    string? ProviderBrandBgColor,
    DateTime? BrandingLastSyncedAtUtc,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? LastSuccessfulSyncUtc,
    DateTime? LastSyncAttemptedUtc,
    string? LastErrorCode);
