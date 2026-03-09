using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Banking.Services;
using NSFinTech.Api.Modules.Users.Services;

namespace NSFinTech.Api.Modules.Banking.Endpoints;

public static class GetAccountBalancesEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid accountId,
        ICurrentUserProvider currentUserProvider,
        BankConnectionService bankConnectionService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await bankConnectionService.GetLatestBalancesAsync(userId, accountId, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
