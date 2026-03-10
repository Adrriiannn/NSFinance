using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Support.DTOs;
using NSFinance.Api.Modules.Support.Services;
using NSFinance.Api.Modules.Support.Validators;

namespace NSFinance.Api.Modules.Support.Endpoints;

public static class CreateDeletionRequestEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateDeletionRequestRequest request,
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var errors = CreateDeletionRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await supportService.CreateDeletionRequestAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
