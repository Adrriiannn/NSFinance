using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class DuplicateExpensePlanEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpensePlanService expensePlanService,
        CancellationToken cancellationToken)
    {
        var plan = await expensePlanService.DuplicatePlanAsync(id, cancellationToken);
        return plan is null ? Results.NotFound() : Results.Created($"/api/expense-tracker/plans/{plan.Id}", plan);
    }
}
