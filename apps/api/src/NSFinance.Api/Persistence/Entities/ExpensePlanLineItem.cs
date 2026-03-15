namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlanLineItem
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public int TaxonomyDomainId { get; set; }
    public int TaxonomyCategoryId { get; set; }
    public int? TaxonomySubcategoryId { get; set; }
    public string DisplayNameSnapshot { get; set; } = string.Empty;
    public string HierarchyPathSnapshot { get; set; } = string.Empty;
    public decimal ExpectedAmount { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ExpensePlan? Plan { get; set; }
}
