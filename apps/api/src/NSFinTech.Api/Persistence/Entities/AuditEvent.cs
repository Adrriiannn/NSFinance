namespace NSFinTech.Api.Persistence.Entities;

public class AuditEvent
{
    public Guid Id { get; set; }
    public string ActorType { get; set; } = "system";
    public Guid? ActorId { get; set; }
    public string TargetEntityType { get; set; } = string.Empty;
    public string? TargetEntityId { get; set; }
    public string EventCategory { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTime EventTimestampUtc { get; set; }
    public string SourceChannel { get; set; } = "api";
    public string? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
}
