namespace NSFinance.Api.Modules.Banking.Services;

internal sealed class ProviderDirectDebitCommitmentRow
{
    public Guid Id { get; init; }
    public Guid LinkedBankAccountId { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public string AccountDisplayName { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? MandateType { get; init; }
    public string? Reference { get; init; }
    public string? MerchantName { get; init; }
    public DateTime? PreviousPaymentDateUtc { get; init; }
    public decimal? PreviousPaymentAmount { get; init; }
    public string? PreviousPaymentCurrency { get; init; }
    public DateTime? NextPaymentDateUtc { get; init; }
    public decimal? NextPaymentAmount { get; init; }
    public string? NextPaymentCurrency { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

internal sealed class ProviderStandingOrderCommitmentRow
{
    public Guid Id { get; init; }
    public Guid LinkedBankAccountId { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public string AccountDisplayName { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? Frequency { get; init; }
    public string? Reference { get; init; }
    public string? PayeeName { get; init; }
    public DateTime? FirstPaymentDateUtc { get; init; }
    public DateTime? NextPaymentDateUtc { get; init; }
    public DateTime? FinalPaymentDateUtc { get; init; }
    public decimal? NextPaymentAmount { get; init; }
    public string? NextPaymentCurrency { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

internal sealed class InferredCommitmentTransactionRow
{
    public Guid Id { get; init; }
    public Guid FinancialAccountId { get; init; }
    public Guid? LinkedBankAccountId { get; init; }
    public string AccountDisplayName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EUR";
    public string Description { get; init; } = string.Empty;
    public DateTime BookedAtUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? MetadataUpdatedUtc { get; init; }
}
