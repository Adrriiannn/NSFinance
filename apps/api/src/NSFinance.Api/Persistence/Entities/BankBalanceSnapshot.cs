namespace NSFinance.Api.Persistence.Entities;

public class BankBalanceSnapshot
{
    public Guid Id { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public decimal? Available { get; set; }
    public decimal? Current { get; set; }
    public decimal? Overdraft { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime CapturedUtc { get; set; }
    public string RawPayloadJson { get; set; } = "{}";

    public LinkedBankAccount? LinkedBankAccount { get; set; }
}
