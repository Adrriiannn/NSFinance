using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class CreateExpenseTrackerEntryEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateExpenseTrackerEntryRequest request,
        ExpenseTrackerService expenseTrackerService,
        CancellationToken cancellationToken)
    {
        var errors = ExpenseTrackerEntryRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var entry = await expenseTrackerService.CreateEntryAsync(request, cancellationToken);
        return Results.Created($"/api/expense-tracker/entries/{entry.Id}", entry);
    }
}
