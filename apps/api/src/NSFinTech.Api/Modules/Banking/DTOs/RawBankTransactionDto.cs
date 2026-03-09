namespace NSFinTech.Api.Modules.Banking.DTOs;

public sealed record RawBankTransactionDto(
    Guid Id,
    Guid LinkedBankAccountId,
    string? ProviderTransactionId,
    decimal Amount,
    string Currency,
    DateTime BookedAtUtc,
    string Description,
    string? TransactionType,
    string? TransactionStatus,
    DateTime ImportedUtc);
