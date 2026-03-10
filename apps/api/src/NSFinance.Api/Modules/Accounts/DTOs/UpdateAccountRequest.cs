namespace NSFinance.Api.Modules.Accounts.DTOs;

public sealed record UpdateAccountRequest(
    string Name,
    string Type);

