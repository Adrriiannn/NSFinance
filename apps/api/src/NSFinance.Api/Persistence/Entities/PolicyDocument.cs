namespace NSFinance.Api.Persistence.Entities;

public class PolicyDocument
{
    public Guid Id { get; set; }
    public string PolicyType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public ICollection<PolicyVersion> Versions { get; set; } = [];
}
