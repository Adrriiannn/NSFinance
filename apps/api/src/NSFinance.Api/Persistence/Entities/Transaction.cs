namespace NSFinance.Api.Persistence.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid FinancialAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Description { get; set; } = string.Empty;
    public DateTime BookedAtUtc { get; set; }
    public Guid? CategoryId { get; set; }
    public int? TaxonomyDomainId { get; set; }
    public int? TaxonomyCategoryId { get; set; }
    public int? TaxonomySubcategoryId { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? MetadataUpdatedUtc { get; set; }

    public FinancialAccount? FinancialAccount { get; set; }
    public TransactionCategory? Category { get; set; }
}
