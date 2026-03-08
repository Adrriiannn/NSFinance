namespace NSFinTech.Api.Modules.Transactions.DTOs;

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string Description,
    decimal Amount,
    string Currency,
    Guid? CategoryId,
    string? CategoryName,
    DateTime BookedAtUtc,
    DateTime CreatedUtc,
    string Direction);
