namespace NSFinTech.Api.Modules.Transactions.DTOs;

public sealed record CreateTransactionRequest(
    Guid AccountId,
    string Description,
    decimal Amount,
    string Direction,
    string? Currency,
    Guid? CategoryId,
    DateTime? BookedAtUtc);
