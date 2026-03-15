using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetExpenseTrackerTaxonomyEndpoint
{
    public static IResult Handle(ExpenseTaxonomyService taxonomyService, bool includeSystem = false)
    {
        var taxonomy = taxonomyService.GetTaxonomy(includeSystem);
        return Results.Ok(taxonomy);
    }
}
