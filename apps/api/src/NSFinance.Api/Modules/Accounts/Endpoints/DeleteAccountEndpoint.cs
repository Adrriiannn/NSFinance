using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Accounts.Services;

namespace NSFinance.Api.Modules.Accounts.Endpoints;

public static class DeleteAccountEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AccountService accountService,
        CancellationToken cancellationToken)
    {
        var result = await accountService.DeleteAccountAsync(id, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : result.Error!.ToApiError();
    }
}
