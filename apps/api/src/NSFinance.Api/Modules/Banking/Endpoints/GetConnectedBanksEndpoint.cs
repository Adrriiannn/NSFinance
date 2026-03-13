using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetConnectedBanksEndpoint
{
    public static async Task<IResult> HandleAsync(
        ICurrentUserProvider currentUserProvider,
        BankConnectionService bankConnectionService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var overview = await bankConnectionService.ListUserVisibleConnectionsAsync(userId, cancellationToken);
        return Results.Ok(overview);
    }
}
