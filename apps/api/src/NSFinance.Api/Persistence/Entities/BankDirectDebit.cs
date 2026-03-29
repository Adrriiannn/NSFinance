namespace NSFinance.Api.Persistence.Entities;

public class BankDirectDebit
{
    public Guid Id { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public string ProviderDirectDebitId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? MandateType { get; set; }
    public string? Reference { get; set; }
    public string? MerchantName { get; set; }
    public DateTime? PreviousPaymentDateUtc { get; set; }
    public decimal? PreviousPaymentAmount { get; set; }
    public string? PreviousPaymentCurrency { get; set; }
    public DateTime? NextPaymentDateUtc { get; set; }
    public decimal? NextPaymentAmount { get; set; }
    public string? NextPaymentCurrency { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public LinkedBankAccount? LinkedBankAccount { get; set; }
}
