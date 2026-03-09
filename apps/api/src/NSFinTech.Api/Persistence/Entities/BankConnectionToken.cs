namespace NSFinTech.Api.Persistence.Entities;

public class BankConnectionToken
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTime? AccessTokenExpiresUtc { get; set; }
    public DateTime TokenObtainedUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedUtc { get; set; }

    public OpenBankingConnection? Connection { get; set; }
}
