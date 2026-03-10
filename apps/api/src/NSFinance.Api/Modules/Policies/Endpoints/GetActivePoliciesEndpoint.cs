using NSFinance.Api.Modules.Policies.Services;

namespace NSFinance.Api.Modules.Policies.Endpoints;

public static class GetActivePoliciesEndpoint
{
    public static async Task<IResult> HandleAsync(
        PolicyService policyService,
        CancellationToken cancellationToken)
    {
        var policies = await policyService.GetActivePoliciesAsync(cancellationToken);
        return Results.Ok(policies);
    }
}
