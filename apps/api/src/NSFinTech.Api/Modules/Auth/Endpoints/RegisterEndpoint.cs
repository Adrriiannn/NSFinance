using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Auth.Validators;

namespace NSFinTech.Api.Modules.Auth.Endpoints;

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
        if (result.Error is not null)
        {
            return result.Conflict
                ? Results.Conflict(new ApiErrorResponse(result.Error, "email_exists"))
                : Results.BadRequest(new ApiErrorResponse(result.Error, "register_failed"));
        }

        return Results.Ok(result.Response);
    }
}
