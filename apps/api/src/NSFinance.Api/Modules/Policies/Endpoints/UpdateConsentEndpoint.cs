using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Policies.DTOs;
using NSFinance.Api.Modules.Policies.Services;
using NSFinance.Api.Modules.Policies.Validators;

namespace NSFinance.Api.Modules.Policies.Endpoints;

public static class UpdateConsentEndpoint
{
    public static async Task<IResult> HandleAsync(
        UpdateConsentRequest request,
        PolicyService policyService,
        CancellationToken cancellationToken)
    {
        var errors = UpdateConsentRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await policyService.UpdateConsentAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
