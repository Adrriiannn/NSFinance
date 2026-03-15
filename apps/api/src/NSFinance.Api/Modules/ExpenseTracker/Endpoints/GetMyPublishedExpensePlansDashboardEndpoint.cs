using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetMyPublishedExpensePlansDashboardEndpoint
{
    public static async Task<IResult> HandleAsync(
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var dashboard = await communityService.GetMyDashboardAsync(cancellationToken);
        return Results.Ok(dashboard);
    }
}
