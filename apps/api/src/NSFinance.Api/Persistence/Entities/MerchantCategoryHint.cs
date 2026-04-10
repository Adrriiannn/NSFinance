namespace NSFinance.Api.Persistence.Entities;

public class MerchantCategoryHint
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public int DomainId { get; set; }
    public int CategoryId { get; set; }
    public int? SubcategoryId { get; set; }
    public double Confidence { get; set; }
    public MerchantHintStrength HintStrength { get; set; } = MerchantHintStrength.Weak;
    public string Source { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Merchant? Merchant { get; set; }
}
