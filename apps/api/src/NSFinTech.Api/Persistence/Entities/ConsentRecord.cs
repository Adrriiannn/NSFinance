namespace NSFinTech.Api.Persistence.Entities;

public class ConsentRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ConsentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public DateTime? GrantedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string Source { get; set; } = "app";
    public string? MetadataJson { get; set; }

    public User? User { get; set; }
}
