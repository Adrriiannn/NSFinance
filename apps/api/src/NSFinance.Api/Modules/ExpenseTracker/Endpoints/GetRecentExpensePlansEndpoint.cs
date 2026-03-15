using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetRecentExpensePlansEndpoint
{
    public static async Task<IResult> HandleAsync(
        ExpensePlanService expensePlanService,
        int take,
        CancellationToken cancellationToken)
    {
        var plans = await expensePlanService.GetRecentPlansAsync(take <= 0 ? 10 : take, cancellationToken);
        return Results.Ok(plans);
    }
}
