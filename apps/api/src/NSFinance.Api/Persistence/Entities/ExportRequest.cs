namespace NSFinance.Api.Persistence.Entities;

public class ExportRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "requested";
    public string Format { get; set; } = "xlsx";
    public Guid? ConnectionId { get; set; }
    public string? ConnectionLabel { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? PeriodPreset { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime RequestedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? Notes { get; set; }
    public string? ArtifactReference { get; set; }

    public User? User { get; set; }
}
