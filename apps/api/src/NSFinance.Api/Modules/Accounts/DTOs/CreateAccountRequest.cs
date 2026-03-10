namespace NSFinance.Api.Modules.Accounts.DTOs;

public sealed record CreateAccountRequest(
    string Name,
    string Type,
    string Currency,
    decimal? OpeningBalance);
