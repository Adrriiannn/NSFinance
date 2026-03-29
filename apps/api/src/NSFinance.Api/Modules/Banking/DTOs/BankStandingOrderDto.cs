namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankStandingOrderDto(
    Guid Id,
    Guid LinkedBankAccountId,
    Guid ConnectionId,
    string AccountDisplayName,
    string ProviderStandingOrderId,
    string? Status,
    string? Frequency,
    string? Reference,
    string? PayeeName,
    DateTime? FirstPaymentDateUtc,
    DateTime? NextPaymentDateUtc,
    DateTime? FinalPaymentDateUtc,
    decimal? NextPaymentAmount,
    string? NextPaymentCurrency,
    DateTime UpdatedUtc);
