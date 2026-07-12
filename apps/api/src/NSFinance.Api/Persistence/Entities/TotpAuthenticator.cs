namespace NSFinance.Api.Persistence.Entities;

public sealed class TotpAuthenticator
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EncryptedSecret { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime EnrollmentExpiresUtc { get; set; }
    public DateTime? VerifiedUtc { get; set; }
    public DateTime? DisabledUtc { get; set; }
    public long? LastAcceptedTimeStep { get; set; }

    public User? User { get; set; }
    public ICollection<MfaRecoveryCode> RecoveryCodes { get; set; } = [];
}
