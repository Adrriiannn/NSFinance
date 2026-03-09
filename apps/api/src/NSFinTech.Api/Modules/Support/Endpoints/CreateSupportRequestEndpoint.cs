using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Support.DTOs;
using NSFinTech.Api.Modules.Support.Services;
using NSFinTech.Api.Modules.Support.Validators;

namespace NSFinTech.Api.Modules.Support.Endpoints;

public static class CreateSupportRequestEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateSupportRequestRequest request,
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var errors = CreateSupportRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await supportService.CreateSupportRequestAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
