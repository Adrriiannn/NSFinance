namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlanPublicationReport
{
    public Guid Id { get; set; }
    public Guid PublicationId { get; set; }
    public Guid ReporterUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Status { get; set; } = "open";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ExpensePlanPublication? Publication { get; set; }
    public User? ReporterUser { get; set; }
}
