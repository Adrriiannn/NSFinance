using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetCommunityExpensePlanByIdEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var publication = await communityService.GetPublicationByIdAsync(id, cancellationToken);
        return publication is null ? Results.NotFound() : Results.Ok(publication);
    }
}
