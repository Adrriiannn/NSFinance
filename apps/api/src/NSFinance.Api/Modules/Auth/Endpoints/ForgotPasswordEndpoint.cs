using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class ForgotPasswordEndpoint
{
    public static async Task<IResult> HandleAsync(
        ForgotPasswordRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = ForgotPasswordRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.RequestPasswordResetAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
