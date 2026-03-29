namespace NSFinance.Api.Persistence.Entities;

public class LinkedBankAccount
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string ProviderAccountId { get; set; } = string.Empty;
    public string? AccountType { get; set; }
    public string? AccountSubType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public string? AccountNumberMetadataJson { get; set; }
    public string CurrentConnectionHealth { get; set; } = "healthy";
    public string RawPayloadJson { get; set; } = "{}";
    public Guid? FinancialAccountId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public OpenBankingConnection? Connection { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public ICollection<BankBalanceSnapshot> BalanceSnapshots { get; set; } = [];
    public ICollection<RawBankTransaction> Transactions { get; set; } = [];
    public ICollection<BankDirectDebit> DirectDebits { get; set; } = [];
    public ICollection<BankStandingOrder> StandingOrders { get; set; } = [];
}
