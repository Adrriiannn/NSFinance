namespace NSFinance.Api.Persistence.Entities;

public sealed class IdentityChallenge
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string DestinationHash { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public string? GrantHash { get; set; }
    public int FailedAttempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? VerifiedUtc { get; set; }
    public DateTime? GrantExpiresUtc { get; set; }
    public DateTime? ConsumedUtc { get; set; }
    public DateTime? SupersededUtc { get; set; }
    public string? RequestedByIp { get; set; }
    public string? MetadataJson { get; set; }
    public Guid ConcurrencyToken { get; set; }

    public User? User { get; set; }
    public TransactionalMessage? Message { get; set; }
}
