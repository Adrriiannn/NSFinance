using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.Accounts.Validators;

namespace NSFinance.Api.Modules.Accounts.Endpoints;

public static class CreateAccountEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateAccountRequest request,
        AccountService accountService,
        CancellationToken cancellationToken)
    {
        var errors = AccountRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var account = await accountService.CreateAccountAsync(request, cancellationToken);
        return Results.Created($"/api/accounts/{account.Id}", account);
    }
}
