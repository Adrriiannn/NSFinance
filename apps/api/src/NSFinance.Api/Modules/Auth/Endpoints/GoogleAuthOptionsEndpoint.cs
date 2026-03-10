using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class GoogleAuthOptionsEndpoint
{
    public static IResult HandleAsync(AuthService authService)
    {
        return Results.Ok(authService.GetGoogleAuthOptions());
    }
}
