using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetExpensePlanByIdEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpensePlanService expensePlanService,
        CancellationToken cancellationToken)
    {
        var plan = await expensePlanService.GetPlanByIdAsync(id, cancellationToken);
        return plan is null ? Results.NotFound() : Results.Ok(plan);
    }
}
