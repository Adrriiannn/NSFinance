using NSFinance.Api.Modules.Categories.Endpoints;

namespace NSFinance.Api.Modules.Categories;

public static class CategoriesModule
{
    public static IEndpointRouteBuilder MapCategoriesModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories")
            .WithTags("Categories")
            .RequireAuthorization();

        group.MapGet("/", GetCategoriesEndpoint.HandleAsync)
            .WithName("GetCategories");

        group.MapGet("/characteristics", GetCategoryCharacteristicsEndpoint.Handle)
            .WithName("GetCategoryCharacteristics");

        return app;
    }
}
