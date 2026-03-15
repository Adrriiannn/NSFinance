using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class UpdateExpensePlanEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateExpensePlanRequest request,
        ExpensePlanService expensePlanService,
        ExpenseTaxonomyService expenseTaxonomyService,
        CancellationToken cancellationToken)
    {
        var errors = ExpensePlanRequestValidator.ValidateUpdate(request, expenseTaxonomyService);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var plan = await expensePlanService.UpdatePlanAsync(id, request, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }
}
