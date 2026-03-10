using NSFinance.Api.Modules.Accounts;
using NSFinance.Api.Modules.Auth;
using NSFinance.Api.Modules.Banking;
using NSFinance.Api.Modules.Categories;
using NSFinance.Api.Modules.Insights;
using NSFinance.Api.Modules.Policies;
using NSFinance.Api.Modules.Support;
using NSFinance.Api.Modules.Transactions;
using NSFinance.Api.Modules.Users;

namespace NSFinance.Api.Modules;

public static class ModuleRegistrationExtensions
{
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        app.MapAuthModule();
        app.MapUsersModule();
        app.MapPoliciesModule();
        app.MapSupportModule();
        app.MapBankingModule();
        app.MapAccountsModule();
        app.MapTransactionsModule();
        app.MapCategoriesModule();
        app.MapInsightsModule();
        return app;
    }
}
