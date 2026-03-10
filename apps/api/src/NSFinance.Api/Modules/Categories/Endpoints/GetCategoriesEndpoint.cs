using NSFinance.Api.Modules.Categories.Services;

namespace NSFinance.Api.Modules.Categories.Endpoints;

public static class GetCategoriesEndpoint
{
    public static async Task<IResult> HandleAsync(
        CategoryService categoryService,
        CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetCategoriesAsync(cancellationToken);
        return Results.Ok(categories);
    }
}
