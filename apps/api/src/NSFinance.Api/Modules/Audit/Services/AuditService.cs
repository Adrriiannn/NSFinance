using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Audit.Services;

public sealed class AuditService(
    AppDbContext dbContext,
    IRequestContextAccessor requestContext,
    ILogger<AuditService> logger) : IAuditService
{
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
        var auditEvent = AuditEventFactory.Create(
            requestContext,
            category,
            eventName,
            targetEntityType,
            targetEntityId,
            actorId,
            actorType,
            metadata);

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
