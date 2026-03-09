using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Accounts.Services;

namespace NSFinTech.Api.Modules.Accounts.Endpoints;

public static class DeleteAccountEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AccountService accountService,
        CancellationToken cancellationToken)
    {
        var deleted = await accountService.DeleteAccountAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound(new ApiErrorResponse("Account not found.", "account_not_found"));
    }
}

