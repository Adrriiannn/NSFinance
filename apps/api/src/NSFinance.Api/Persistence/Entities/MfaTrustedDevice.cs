namespace NSFinance.Api.Persistence.Entities;

public sealed class MfaTrustedDevice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevocationReason { get; set; }

    public User? User { get; set; }
    public Device? Device { get; set; }
}
