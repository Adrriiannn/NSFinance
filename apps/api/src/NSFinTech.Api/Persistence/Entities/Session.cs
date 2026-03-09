namespace NSFinTech.Api.Persistence.Entities;

public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string? RevocationReason { get; set; }
    public string DeviceLabel { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
    public string? IpAddress { get; set; }
    public string? RiskFlagsJson { get; set; }
    public Guid RefreshTokenFamilyId { get; set; }

    public User? User { get; set; }
    public Device? Device { get; set; }
    public ICollection<SessionRefreshToken> RefreshTokens { get; set; } = [];
}
