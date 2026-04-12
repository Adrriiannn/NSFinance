namespace NSFinance.Api.Persistence.Entities;

public class MerchantAliasConflict
{
    public Guid Id { get; set; }
    public string NormalizedAliasText { get; set; } = string.Empty;
    public MerchantAliasType AliasType { get; set; } = MerchantAliasType.BillingDescriptor;
    public Guid ExistingMerchantId { get; set; }
    public Guid ProposedMerchantId { get; set; }
    public string ProposedSource { get; set; } = string.Empty;
    public MerchantAliasTrustLevel ProposedTrustLevel { get; set; } = MerchantAliasTrustLevel.Observed;
    public MerchantAliasConflictStatus Status { get; set; } = MerchantAliasConflictStatus.Open;
    public int OccurrenceCount { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string? Notes { get; set; }

    public Merchant ExistingMerchant { get; set; } = null!;
    public Merchant ProposedMerchant { get; set; } = null!;
}
