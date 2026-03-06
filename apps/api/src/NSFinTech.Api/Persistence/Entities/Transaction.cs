namespace NSFinTech.Api.Persistence.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid FinancialAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Description { get; set; } = string.Empty;
    public DateTime BookedAtUtc { get; set; }
    public Guid? CategoryId { get; set; }
    public DateTime CreatedUtc { get; set; }

    public FinancialAccount? FinancialAccount { get; set; }
    public TransactionCategory? Category { get; set; }
}
