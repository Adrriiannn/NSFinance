namespace NSFinTech.Api.Persistence.Entities;

public class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string DeviceFingerprint { get; set; } = string.Empty;
    public string DeviceLabel { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool IsTrusted { get; set; }

    public User? User { get; set; }
    public ICollection<Session> Sessions { get; set; } = [];
}
