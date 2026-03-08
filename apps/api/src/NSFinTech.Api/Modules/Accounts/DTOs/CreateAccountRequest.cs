namespace NSFinTech.Api.Modules.Accounts.DTOs;

public sealed record CreateAccountRequest(
    string Name,
    string Type,
    string Currency,
    decimal? OpeningBalance);
