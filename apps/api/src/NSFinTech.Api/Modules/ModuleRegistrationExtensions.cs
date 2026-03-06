using NSFinTech.Api.Modules.Users;

namespace NSFinTech.Api.Modules;

public static class ModuleRegistrationExtensions
{
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        app.MapUsersModule();
        return app;
    }
}
