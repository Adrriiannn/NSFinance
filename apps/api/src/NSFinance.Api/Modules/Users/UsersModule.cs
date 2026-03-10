using NSFinance.Api.Modules.Users.Endpoints;

namespace NSFinance.Api.Modules.Users;

public static class UsersModule
{
    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/profile", GetUserProfileEndpoint.HandleAsync)
            .WithName("GetUserProfile");

        group.MapPatch("/profile", UpdateUserProfileEndpoint.HandleAsync)
            .WithName("UpdateUserProfile");

        group.MapGet("/preferences", GetUserPreferencesEndpoint.HandleAsync)
            .WithName("GetUserPreferences");

        group.MapPatch("/preferences", UpdateUserPreferencesEndpoint.HandleAsync)
            .WithName("UpdateUserPreferences");

        return app;
    }
}
