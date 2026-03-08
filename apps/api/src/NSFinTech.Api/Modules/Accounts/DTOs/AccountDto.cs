namespace NSFinTech.Api.Modules.Accounts.DTOs;

public sealed record AccountDto(
    Guid Id,
    string Name,
    string Type,
    string Currency,
    decimal CurrentBalance,
    int TransactionCount,
    DateTime CreatedUtc);
