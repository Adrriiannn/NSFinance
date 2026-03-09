using System.Text.Json;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Audit.Services;

public sealed class AuditService(
    AppDbContext dbContext,
    IRequestContextAccessor requestContext,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteEventAsync(
        string category,
        string eventName,
        string targetEntityType,
        string? targetEntityId,
        Guid? actorId,
        string actorType,
        object? metadata,
        CancellationToken cancellationToken)
    {
        var auditEvent = new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorType = actorType,
            ActorId = actorId,
            TargetEntityType = targetEntityType,
            TargetEntityId = targetEntityId,
            EventCategory = category,
            EventName = eventName,
            EventTimestampUtc = DateTime.UtcNow,
            SourceChannel = requestContext.SourceChannel,
            CorrelationId = requestContext.CorrelationId,
            MetadataJson = metadata is null
                ? null
                : JsonSerializer.Serialize(metadata, MetadataSerializerOptions)
        };

        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Audit event {Category}:{EventName} actor={ActorType}/{ActorId} target={TargetType}/{TargetId} correlationId={CorrelationId}",
            category,
            eventName,
            actorType,
            actorId,
            targetEntityType,
            targetEntityId,
            requestContext.CorrelationId);
    }
}
