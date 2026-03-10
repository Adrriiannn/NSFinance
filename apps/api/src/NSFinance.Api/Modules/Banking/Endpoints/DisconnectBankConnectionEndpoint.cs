using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class DisconnectBankConnectionEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid connectionId,
        ICurrentUserProvider currentUserProvider,
        IAuditService auditService,
        BankConnectionService bankConnectionService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        await auditService.WriteEventAsync(
            category: "banking",
            eventName: "disconnect_requested",
            targetEntityType: "open_banking_connection",
            targetEntityId: connectionId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        var result = await bankConnectionService.DisconnectAsync(userId, connectionId, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.NoContent();
    }
}
