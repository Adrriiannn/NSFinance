namespace NSFinance.Api.Persistence.Entities;

public class PolicyVersion
{
    public Guid Id { get; set; }
    public Guid PolicyDocumentId { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime EffectiveUtc { get; set; }
    public string ContentReference { get; set; } = string.Empty;
    public string ContentMarkdown { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }

    public PolicyDocument? PolicyDocument { get; set; }
    public ICollection<PolicyAcceptance> Acceptances { get; set; } = [];
}
