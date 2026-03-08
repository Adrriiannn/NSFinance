using NSFinTech.Api.Modules.Users.Endpoints;

namespace NSFinTech.Api.Modules.Users;

public static class UsersModule
{
    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/", GetUsersEndpoint.HandleAsync)
            .WithName("GetUsers");

        return app;
    }
}
