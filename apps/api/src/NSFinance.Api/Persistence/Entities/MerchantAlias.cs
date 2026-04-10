namespace NSFinance.Api.Persistence.Entities;

public class MerchantAlias
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public string AliasText { get; set; } = string.Empty;
    public string NormalizedAliasText { get; set; } = string.Empty;
    public MerchantAliasType AliasType { get; set; } = MerchantAliasType.BillingDescriptor;
    public double Confidence { get; set; }
    public bool IsExactMatchPreferred { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Merchant? Merchant { get; set; }
}
