namespace NSFinance.Api.Persistence.Entities;

public class RawBankTransaction
{
    public Guid Id { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime BookedAtUtc { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? TransactionType { get; set; }
    public string? TransactionStatus { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public DateTime ImportedUtc { get; set; }

    public LinkedBankAccount? LinkedBankAccount { get; set; }
}
