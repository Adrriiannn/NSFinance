using NSFinTech.Api.Modules.Policies.Services;

namespace NSFinTech.Api.Modules.Policies.Endpoints;

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
