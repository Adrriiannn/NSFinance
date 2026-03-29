namespace NSFinance.Api.Persistence.Entities;

public class BankStandingOrder
{
    public Guid Id { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public string ProviderStandingOrderId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Frequency { get; set; }
    public string? Reference { get; set; }
    public string? PayeeName { get; set; }
    public DateTime? FirstPaymentDateUtc { get; set; }
    public DateTime? NextPaymentDateUtc { get; set; }
    public DateTime? FinalPaymentDateUtc { get; set; }
    public decimal? NextPaymentAmount { get; set; }
    public string? NextPaymentCurrency { get; set; }
    public string? PayeeAccountMetadataJson { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public LinkedBankAccount? LinkedBankAccount { get; set; }
}
