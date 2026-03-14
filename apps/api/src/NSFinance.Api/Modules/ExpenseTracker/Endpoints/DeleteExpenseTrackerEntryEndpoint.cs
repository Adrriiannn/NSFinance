using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class DeleteExpenseTrackerEntryEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpenseTrackerService expenseTrackerService,
        CancellationToken cancellationToken)
    {
        var deleted = await expenseTrackerService.DeleteEntryAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
