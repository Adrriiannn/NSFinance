namespace NSFinance.Api.Persistence.Entities;

public class LinkedBankCard
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string ProviderCardId { get; set; } = string.Empty;
    public string? ProviderAccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public string? CardType { get; set; }
    public string? CardNetwork { get; set; }
    public string? CardNumberLastFour { get; set; }
    public string? NameOnCard { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public string CurrentConnectionHealth { get; set; } = "healthy";
    public string RawPayloadJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public OpenBankingConnection? Connection { get; set; }
    public ICollection<BankCardBalanceSnapshot> BalanceSnapshots { get; set; } = [];
    public ICollection<RawBankCardTransaction> Transactions { get; set; } = [];
}
