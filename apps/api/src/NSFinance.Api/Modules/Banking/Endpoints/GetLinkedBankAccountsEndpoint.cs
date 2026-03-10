using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetLinkedBankAccountsEndpoint
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

        var accounts = await bankConnectionService.ListLinkedAccountsAsync(userId, cancellationToken);
        return Results.Ok(accounts);
    }
}
