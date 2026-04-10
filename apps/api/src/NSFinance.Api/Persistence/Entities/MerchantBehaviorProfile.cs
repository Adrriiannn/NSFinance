namespace NSFinance.Api.Persistence.Entities;

public class MerchantBehaviorProfile
{
    public Guid MerchantId { get; set; }
    public bool SupportsSubscriptions { get; set; }
    public bool SupportsRecurringPayments { get; set; }
    public bool SupportsOneTimePurchases { get; set; }
    public bool SupportsMarketplacePayments { get; set; }
    public bool SupportsInAppPurchases { get; set; }
    public bool AnnualRenewalsCommon { get; set; }
    public bool RefundsCommon { get; set; }
    public bool MixedUseRisk { get; set; }
    public double PaymentBehaviorConfidence { get; set; }
    public string BehaviorSummary { get; set; } = string.Empty;

    public Merchant? Merchant { get; set; }
}
