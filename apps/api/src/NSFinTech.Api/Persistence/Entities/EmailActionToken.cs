namespace NSFinTech.Api.Persistence.Entities;

public class EmailActionToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
    public string? RequestedByIp { get; set; }
    public string? MetadataJson { get; set; }

    public User? User { get; set; }
}
