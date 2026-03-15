using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class UseExpensePlanPublicationEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var plan = await communityService.UsePublicationAsync(id, cancellationToken);
        return plan is null ? Results.NotFound() : Results.Ok(plan);
    }
}
