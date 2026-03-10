using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class RegisterEndpoint
{
    public static async Task<IResult> HandleAsync(
        RegisterRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = RegisterRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
