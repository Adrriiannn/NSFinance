namespace NSFinance.Api.Persistence.Entities;

public class BankCardBalanceSnapshot
{
    public Guid Id { get; set; }
    public Guid LinkedBankCardId { get; set; }
    public decimal? Available { get; set; }
    public decimal? Current { get; set; }
    public decimal? Limit { get; set; }
    public decimal? Outstanding { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime CapturedUtc { get; set; }
    public string RawPayloadJson { get; set; } = "{}";

    public LinkedBankCard? LinkedBankCard { get; set; }
}
