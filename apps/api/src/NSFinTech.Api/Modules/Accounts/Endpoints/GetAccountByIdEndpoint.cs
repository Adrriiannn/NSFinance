using NSFinTech.Api.Modules.Accounts.Services;
using NSFinTech.Api.Common.Contracts;

namespace NSFinTech.Api.Modules.Accounts.Endpoints;

public static class GetAccountByIdEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AccountService accountService,
        CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccountByIdAsync(id, cancellationToken);
        return account is null
            ? Results.NotFound(new ApiErrorResponse("Account not found.", "account_not_found"))
            : Results.Ok(account);
    }
}
