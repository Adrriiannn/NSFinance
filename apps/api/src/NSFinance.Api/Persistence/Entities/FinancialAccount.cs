namespace NSFinance.Api.Persistence.Entities;

public class FinancialAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public DateTime CreatedUtc { get; set; }

    public User? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
}
