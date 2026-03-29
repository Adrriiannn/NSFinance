namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record LinkedBankCardDto(
    Guid Id,
    Guid ConnectionId,
    string ProviderCardId,
    string? ProviderAccountId,
    string DisplayName,
    string Currency,
    string? CardType,
    string? CardNetwork,
    string? CardNumberLastFour,
    string? NameOnCard,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    string CurrentConnectionHealth,
    decimal? LatestAvailable,
    decimal? LatestCurrent,
    decimal? LatestLimit,
    decimal? LatestOutstanding,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
