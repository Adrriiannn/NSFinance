using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class CreateExpensePlanEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateExpensePlanRequest request,
        ExpensePlanService expensePlanService,
        ExpenseTaxonomyService expenseTaxonomyService,
        CancellationToken cancellationToken)
    {
        var errors = ExpensePlanRequestValidator.ValidateCreate(request, expenseTaxonomyService);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var plan = await expensePlanService.CreatePlanAsync(request, cancellationToken);
            return Results.Created($"/api/expense-tracker/plans/{plan.Id}", plan);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }
}
