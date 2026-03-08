using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Auth.Validators;

namespace NSFinTech.Api.Modules.Auth.Endpoints;

public static class LoginEndpoint
{
    public static async Task<IResult> HandleAsync(
        LoginRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = LoginRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.LoginAsync(request, cancellationToken);
        if (result.Error is not null)
        {
            return Results.Json(new ApiErrorResponse(result.Error, "invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(result.Response);
    }
}
