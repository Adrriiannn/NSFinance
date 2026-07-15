namespace NSFinance.Api.Modules.Accounts.DTOs;

public sealed record AccountDto(
    Guid Id,
    string Name,
    string Type,
    string Currency,
    decimal CurrentBalance,
    int TransactionCount,
    DateTime CreatedUtc,
    string? ProviderId,
    string? ProviderDisplayName,
    string? ProviderIconUrl,
    string? ProviderLogoUrl,
    string? ProviderBrandBgColor,
    bool HasProviderBranding,
    AccountBalanceDto? Balance = null,
    string Source = "manual");
