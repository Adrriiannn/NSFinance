using System.Text.Json;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Audit.Services;

public static class AuditEventFactory
{
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AuditEvent Create(
        IRequestContextAccessor requestContext,
        string category,
        string eventName,
        string targetEntityType,
        string? targetEntityId,
        Guid? actorId,
        string actorType,
        object? metadata)
    {
        return new AuditEvent
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
    }
}
