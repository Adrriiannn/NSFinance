using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Policies.Services;

namespace NSFinTech.Api.Modules.Policies.Endpoints;

public static class GetConsentsEndpoint
{
    public static async Task<IResult> HandleAsync(
        PolicyService policyService,
        CancellationToken cancellationToken)
    {
        var result = await policyService.GetConsentsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
