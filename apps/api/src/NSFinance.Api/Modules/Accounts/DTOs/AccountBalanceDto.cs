namespace NSFinance.Api.Modules.Accounts.DTOs;

public sealed record AccountBalanceDto(
    decimal? Current,
    decimal? Available,
    decimal? Overdraft,
    string Currency,
    string Source,
    DateTime? AsOfUtc,
    string Freshness,
    IReadOnlyList<string> Exclusions);

public sealed record CurrencyBalanceTotalDto(
    string Currency,
    decimal Amount,
    int AccountCount,
    string Basis);

public sealed record PortfolioBalanceDto(
    IReadOnlyList<CurrencyBalanceTotalDto> ByCurrency,
    int IncludedAccountCount,
    int ExcludedAccountCount,
    bool HasMultipleCurrencies);
