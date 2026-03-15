using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class UnpublishExpensePlanPublicationEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var publication = await communityService.UnpublishAsync(id, cancellationToken);
        return publication is null ? Results.NotFound() : Results.Ok(publication);
    }
}
