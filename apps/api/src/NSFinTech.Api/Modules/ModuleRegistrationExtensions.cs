using NSFinTech.Api.Modules.Accounts;
using NSFinTech.Api.Modules.Auth;
using NSFinTech.Api.Modules.Categories;
using NSFinTech.Api.Modules.Insights;
using NSFinTech.Api.Modules.Transactions;
using NSFinTech.Api.Modules.Users;

namespace NSFinTech.Api.Modules;

public static class ModuleRegistrationExtensions
{
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        app.MapAuthModule();
        app.MapUsersModule();
        app.MapAccountsModule();
        app.MapTransactionsModule();
        app.MapCategoriesModule();
        app.MapInsightsModule();
        return app;
    }
}
