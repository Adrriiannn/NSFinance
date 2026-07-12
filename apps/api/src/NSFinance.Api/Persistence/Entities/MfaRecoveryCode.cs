namespace NSFinance.Api.Persistence.Entities;

public sealed class MfaRecoveryCode
{
    public Guid Id { get; set; }
    public Guid TotpAuthenticatorId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? UsedUtc { get; set; }

    public TotpAuthenticator? TotpAuthenticator { get; set; }
}
