namespace NSFinance.Api.Persistence.Entities;

public sealed class UserFinancialContextProfile
{
    public Guid UserId { get; set; }
    public string Country { get; set; } = "ZZ";
    public string Currency { get; set; } = "EUR";
    public string? MonthlyIncomeRange { get; set; }
    public string KnownObligationsJson { get; set; } = "[]";
    public string BudgetStructureJson { get; set; } = "{}";
    public string ActivePlansJson { get; set; } = "[]";
    public string SpendingTendenciesJson { get; set; } = "[]";
    public string CategoryFlexibilityMarkersJson { get; set; } = "[]";
    public string AdviceStylePreference { get; set; } = "balanced";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public User? User { get; set; }
}
