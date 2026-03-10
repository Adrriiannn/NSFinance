namespace NSFinance.Api.Persistence.Entities;

public class TransactionCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
