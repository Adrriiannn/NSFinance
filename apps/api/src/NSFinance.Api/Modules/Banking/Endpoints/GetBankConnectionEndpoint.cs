using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetBankConnectionEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid connectionId,
        ICurrentUserProvider currentUserProvider,
        BankConnectionService bankConnectionService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var connection = await bankConnectionService.GetConnectionSummaryAsync(userId, connectionId, cancellationToken);
        return connection is null ? Results.NotFound() : Results.Ok(connection);
    }
}
