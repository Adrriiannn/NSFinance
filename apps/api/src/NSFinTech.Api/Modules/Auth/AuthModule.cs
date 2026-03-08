using Microsoft.AspNetCore.Authorization;
using NSFinTech.Api.Modules.Auth.Endpoints;

namespace NSFinTech.Api.Modules.Auth;

public static class AuthModule
{
    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", RegisterEndpoint.HandleAsync)
            .WithName("Register");

        group.MapPost("/login", LoginEndpoint.HandleAsync)
            .WithName("Login");

        group.MapPost("/logout", LogoutEndpoint.HandleAsync)
            .WithName("Logout")
            .RequireAuthorization();

        group.MapGet("/me", MeEndpoint.HandleAsync)
            .WithName("GetCurrentUser")
            .RequireAuthorization();

        return app;
    }
}
