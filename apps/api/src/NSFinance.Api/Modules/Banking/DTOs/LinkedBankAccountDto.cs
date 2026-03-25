namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record LinkedBankAccountDto(
    Guid Id,
    Guid ConnectionId,
    string ProviderAccountId,
    string? ProviderId,
    string? ProviderDisplayName,
    string? ProviderIconUrl,
    string? ProviderLogoUrl,
    string? ProviderBrandBgColor,
    string DisplayName,
    string? AccountType,
    string? AccountSubType,
    string Currency,
    string CurrentConnectionHealth,
    decimal? LatestAvailable,
    decimal? LatestCurrent,
    decimal? LatestOverdraft,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
