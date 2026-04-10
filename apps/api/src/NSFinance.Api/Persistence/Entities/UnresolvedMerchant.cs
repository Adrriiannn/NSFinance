namespace NSFinance.Api.Persistence.Entities;

public class UnresolvedMerchant
{
    public Guid Id { get; set; }
    public string RawDescriptor { get; set; } = string.Empty;
    public string NormalizedDescriptor { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime? LastInvestigationUtc { get; set; }
    public UnresolvedMerchantStatus Status { get; set; } = UnresolvedMerchantStatus.New;
    public string? Notes { get; set; }
}
