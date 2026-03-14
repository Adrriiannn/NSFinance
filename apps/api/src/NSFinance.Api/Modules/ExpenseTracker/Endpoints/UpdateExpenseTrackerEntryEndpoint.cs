using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class UpdateExpenseTrackerEntryEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateExpenseTrackerEntryRequest request,
        ExpenseTrackerService expenseTrackerService,
        CancellationToken cancellationToken)
    {
        var errors = ExpenseTrackerEntryRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var entry = await expenseTrackerService.UpdateEntryAsync(id, request, cancellationToken);
        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }
}
