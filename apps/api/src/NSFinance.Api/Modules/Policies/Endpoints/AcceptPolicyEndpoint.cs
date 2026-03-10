using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Policies.DTOs;
using NSFinance.Api.Modules.Policies.Services;
using NSFinance.Api.Modules.Policies.Validators;

namespace NSFinance.Api.Modules.Policies.Endpoints;

public static class AcceptPolicyEndpoint
{
    public static async Task<IResult> HandleAsync(
        AcceptPolicyRequest request,
        PolicyService policyService,
        CancellationToken cancellationToken)
    {
        var errors = AcceptPolicyRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await policyService.AcceptPolicyAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
