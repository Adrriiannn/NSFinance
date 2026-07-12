using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class MicrosoftLoginEndpoint
{
    public static async Task<IResult> HandleAsync(
        MicrosoftLoginRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = MicrosoftLoginRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.LoginWithMicrosoftAsync(request, cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}

public static class MicrosoftAuthOptionsEndpoint
{
    public static IResult Handle(MicrosoftAuthService microsoftAuthService)
    {
        return Results.Ok(new MicrosoftAuthOptionsResponse(
            microsoftAuthService.IsConfigured,
            microsoftAuthService.ClientId,
            microsoftAuthService.Authority,
            microsoftAuthService.Scope));
    }
}
