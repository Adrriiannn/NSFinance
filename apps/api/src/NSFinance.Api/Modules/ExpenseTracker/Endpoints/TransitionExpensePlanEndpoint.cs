using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class TransitionExpensePlanEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        TransitionExpensePlanRequest request,
        ExpensePlanService expensePlanService,
        CancellationToken cancellationToken)
    {
        var errors = ExpensePlanRequestValidator.ValidateTransition(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var plan = await expensePlanService.TransitionPlanAsync(id, request, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }
}
