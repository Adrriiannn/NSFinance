namespace NSFinance.Api.Persistence.Entities;

public class OperationalFailureRecord
{
    public Guid Id { get; set; }
    public OperationalFailureArea Area { get; set; } = OperationalFailureArea.AIProvider;
    public OperationalFailureSeverity Severity { get; set; } = OperationalFailureSeverity.Warning;
    public string FailureType { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? SubjectKey { get; set; }
    public string? FailureMessage { get; set; }
    public string? DetailsJson { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTime FirstOccurredUtc { get; set; }
    public DateTime LastOccurredUtc { get; set; }
}
