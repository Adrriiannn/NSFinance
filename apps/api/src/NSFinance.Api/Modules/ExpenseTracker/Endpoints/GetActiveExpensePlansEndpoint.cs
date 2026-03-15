using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetActiveExpensePlansEndpoint
{
    public static async Task<IResult> HandleAsync(
        ExpensePlanService expensePlanService,
        CancellationToken cancellationToken)
    {
        var plans = await expensePlanService.GetActivePlansAsync(cancellationToken);
        return Results.Ok(plans);
    }
}
