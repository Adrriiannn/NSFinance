using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Auth.Validators;

namespace NSFinTech.Api.Modules.Auth.Endpoints;

public static class ConfirmEmailVerificationEndpoint
{
    public static async Task<IResult> HandleAsync(
        ConfirmEmailVerificationRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = ConfirmEmailVerificationRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.ConfirmEmailVerificationAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
