namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CreatorDisplayNameSnapshot { get; set; } = string.Empty;
    public string CreatorTagSnapshot { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "drafted";
    public string PlanType { get; set; } = "monthly";
    public string PlanOriginType { get; set; } = "manual";
    public int PlanVersion { get; set; } = 1;
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public decimal ExpectedIncomeTotal { get; set; }
    public decimal ExpectedSpendTotal { get; set; }
    public decimal ExpectedRemainingTotal { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string? StatusReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? LastCalculatedAtUtc { get; set; }
    public Guid? SourcePlanId { get; set; }
    public Guid? ImportedFromPublicPlanId { get; set; }
    public bool IsTemplate { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrenceRuleJson { get; set; }
    public bool IsShared { get; set; }
    public string? SharingMode { get; set; }
    public string? SharedIdentity { get; set; }

    public User? User { get; set; }
    public ExpensePlan? SourcePlan { get; set; }
    public ICollection<ExpensePlan> DerivedPlans { get; set; } = [];
    public ExpensePlanPublication? ImportedFromPublicPlan { get; set; }
    public ICollection<ExpensePlanPublication> Publications { get; set; } = [];
    public ICollection<ExpensePlanLineItem> LineItems { get; set; } = [];
}
