using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Policies.Services;

namespace NSFinance.Api.Modules.Policies.Endpoints;

public static class GetPolicyByTypeEndpoint
{
    public static async Task<IResult> HandleAsync(
        string policyType,
        PolicyService policyService,
        CancellationToken cancellationToken)
    {
        var result = await policyService.GetActivePolicyByTypeAsync(policyType, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
