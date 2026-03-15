using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetExpensePlansEndpoint
{
    public static async Task<IResult> HandleAsync(
        ExpensePlanService expensePlanService,
        string? status,
        bool templatesOnly,
        int? take,
        CancellationToken cancellationToken)
    {
        var plans = await expensePlanService.GetPlansAsync(status, templatesOnly, take, cancellationToken);
        return Results.Ok(plans);
    }
}
