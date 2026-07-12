namespace NSFinance.Api.Persistence.Entities;

public sealed class TransactionalMessage
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? IdentityChallengeId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public int TemplateVersion { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string EncryptedPayload { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public string? LeaseId { get; set; }
    public DateTime? ProviderAcceptedUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }
    public DateTime? FailedUtc { get; set; }
    public string? LastFailureCode { get; set; }

    public User? User { get; set; }
    public IdentityChallenge? IdentityChallenge { get; set; }
}
