using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Accounts.DTOs;
using NSFinTech.Api.Modules.Accounts.Services;
using NSFinTech.Api.Modules.Accounts.Validators;

namespace NSFinTech.Api.Modules.Accounts.Endpoints;

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

