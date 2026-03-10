using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Support.Services;

namespace NSFinance.Api.Modules.Support.Endpoints;

public static class GetMyDeletionRequestsEndpoint
{
    public static async Task<IResult> HandleAsync(
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var result = await supportService.GetMyDeletionRequestsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
