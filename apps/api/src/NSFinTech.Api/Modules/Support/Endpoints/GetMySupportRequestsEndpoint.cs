using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Support.Services;

namespace NSFinTech.Api.Modules.Support.Endpoints;

public static class GetMySupportRequestsEndpoint
{
    public static async Task<IResult> HandleAsync(
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var result = await supportService.GetMySupportRequestsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
