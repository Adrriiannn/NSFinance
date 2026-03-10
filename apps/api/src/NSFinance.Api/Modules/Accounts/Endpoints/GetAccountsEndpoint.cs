using NSFinance.Api.Modules.Accounts.Services;

namespace NSFinance.Api.Modules.Accounts.Endpoints;

public static class GetAccountsEndpoint
{
    public static async Task<IResult> HandleAsync(AccountService accountService, CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAccountsAsync(cancellationToken);
        return Results.Ok(accounts);
    }
}
