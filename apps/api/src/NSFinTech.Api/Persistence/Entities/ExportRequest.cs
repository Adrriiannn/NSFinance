namespace NSFinTech.Api.Persistence.Entities;

public class ExportRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "requested";
    public DateTime RequestedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? Notes { get; set; }
    public string? ArtifactReference { get; set; }

    public User? User { get; set; }
}
