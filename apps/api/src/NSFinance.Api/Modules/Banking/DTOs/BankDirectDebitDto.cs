namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankDirectDebitDto(
    Guid Id,
    Guid LinkedBankAccountId,
    Guid ConnectionId,
    string AccountDisplayName,
    string ProviderDirectDebitId,
    string? Status,
    string? MandateType,
    string? Reference,
    string? MerchantName,
    DateTime? PreviousPaymentDateUtc,
    decimal? PreviousPaymentAmount,
    string? PreviousPaymentCurrency,
    DateTime? NextPaymentDateUtc,
    decimal? NextPaymentAmount,
    string? NextPaymentCurrency,
    DateTime UpdatedUtc);
