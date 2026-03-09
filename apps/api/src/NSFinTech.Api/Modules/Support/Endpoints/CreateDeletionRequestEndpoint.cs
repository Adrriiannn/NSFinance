using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Support.DTOs;
using NSFinTech.Api.Modules.Support.Services;

namespace NSFinTech.Api.Modules.Support.Endpoints;

public static class CreateDeletionRequestEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateDeletionRequestRequest request,
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var result = await supportService.CreateDeletionRequestAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
