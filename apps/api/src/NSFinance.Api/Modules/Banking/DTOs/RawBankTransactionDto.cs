namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record RawBankTransactionDto(
    Guid Id,
    Guid LinkedBankAccountId,
    string? ProviderTransactionId,
    string? NormalizedProviderTransactionId,
    decimal Amount,
    string Currency,
    DateTime BookedAtUtc,
    DateTime? ValueAtUtc,
    string Description,
    string? TransactionType,
    string? TransactionStatus,
    string SourceEndpoint,
    string? ProviderStatus,
    string? StatusNormalizationReason,
    string? ProviderTimestampRaw,
    string? ValueTimestampRaw,
    string? TimestampSource,
    string TimestampPrecision,
    DateTime ImportedUtc);
