using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Support.DTOs;
using NSFinance.Api.Modules.Support.Services;

namespace NSFinance.Api.Modules.Support.Endpoints;

public static class CreateExportRequestEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateExportRequestRequest? request,
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var result = await supportService.CreateExportRequestAsync(
            request ?? new CreateExportRequestRequest(null),
            cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
