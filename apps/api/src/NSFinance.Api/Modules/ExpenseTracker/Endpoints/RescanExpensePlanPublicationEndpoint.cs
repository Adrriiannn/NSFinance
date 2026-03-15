using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class RescanExpensePlanPublicationEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var publication = await communityService.RescanAsync(id, cancellationToken);
        return publication is null ? Results.NotFound() : Results.Ok(publication);
    }
}
