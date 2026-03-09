using NSFinTech.Api.Modules.Auth.Services;

namespace NSFinTech.Api.Modules.Auth.Endpoints;

public static class GoogleAuthOptionsEndpoint
{
    public static IResult HandleAsync(AuthService authService)
    {
        return Results.Ok(authService.GetGoogleAuthOptions());
    }
}
