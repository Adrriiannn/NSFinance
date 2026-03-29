namespace NSFinance.Api.Persistence.Entities;

public class BankConnectionIdentityInfo
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? DateOfBirth { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public DateTime FetchedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public OpenBankingConnection? Connection { get; set; }
}
