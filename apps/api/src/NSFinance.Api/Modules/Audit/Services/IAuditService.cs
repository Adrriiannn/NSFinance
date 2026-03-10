namespace NSFinance.Api.Modules.Audit.Services;

public interface IAuditService
{
    Task WriteEventAsync(
        string category,
        string eventName,
        string targetEntityType,
        string? targetEntityId,
        Guid? actorId,
        string actorType,
        object? metadata,
        CancellationToken cancellationToken);
}
