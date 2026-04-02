namespace NSFinance.Api.Persistence.Entities;

public class NormalizedBankTransaction
{
    public Guid Id { get; set; }
    public Guid RawBankTransactionId { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid? ProjectedTransactionId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? NormalizedProviderTransactionId { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime BookedAtUtc { get; set; }
    public DateTime? ValueAtUtc { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? TransactionType { get; set; }
    public string? TransactionStatus { get; set; }
    public string SourceEndpoint { get; set; } = "legacy";
    public string? ProviderStatus { get; set; }
    public string? StatusNormalizationReason { get; set; }
    public string? ProviderTimestampRaw { get; set; }
    public string? ValueTimestampRaw { get; set; }
    public string? TimestampSource { get; set; }
    public string TimestampPrecision { get; set; } = "unknown_needs_verification";
    public string? TimestampNormalizedByPolicy { get; set; }
    public string? NormalizationPolicyKey { get; set; }
    public string? NormalizationPolicyFamily { get; set; }
    public int? InterpretationConfidenceScore { get; set; }
    public string? InterpretationConfidenceTier { get; set; }
    public string? InterpretationReasonJson { get; set; }
    public DateTime ImportedUtc { get; set; }
    public DateTime LastNormalizedUtc { get; set; }

    public RawBankTransaction? RawBankTransaction { get; set; }
    public LinkedBankAccount? LinkedBankAccount { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public Transaction? ProjectedTransaction { get; set; }
}
