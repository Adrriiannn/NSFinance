using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.Accounts.Validators;

namespace NSFinance.Api.Modules.Accounts.Endpoints;

public static class UpdateAccountEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateAccountRequest request,
        AccountService accountService,
        CancellationToken cancellationToken)
    {
        var errors = AccountRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var account = await accountService.UpdateAccountAsync(id, request, cancellationToken);
        return account is null
            ? Results.NotFound(new ApiErrorResponse("Account not found.", "account_not_found"))
            : Results.Ok(account);
    }
}

