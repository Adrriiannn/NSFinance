namespace NSFinance.Api.Persistence.Entities;

public class ExpenseTrackerEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Category { get; set; } = "Other";
    public string PaymentSource { get; set; } = "Other";
    public DateTime OccurredAtUtc { get; set; }
    public string? Notes { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string Status { get; set; } = "completed";
    public bool IsRecurring { get; set; }
    public string? Merchant { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public User? User { get; set; }
}
