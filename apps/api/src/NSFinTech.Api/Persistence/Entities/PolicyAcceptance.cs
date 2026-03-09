namespace NSFinTech.Api.Persistence.Entities;

public class PolicyAcceptance
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PolicyVersionId { get; set; }
    public string PolicyType { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTime AcceptedUtc { get; set; }
    public string AcceptanceContext { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }

    public User? User { get; set; }
    public PolicyVersion? PolicyVersionEntity { get; set; }
}
