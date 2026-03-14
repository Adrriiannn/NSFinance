using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetExpenseTrackerEntriesEndpoint
{
    public static async Task<IResult> HandleAsync(
        ExpenseTrackerService expenseTrackerService,
        CancellationToken cancellationToken)
    {
        var entries = await expenseTrackerService.GetEntriesAsync(cancellationToken);
        return Results.Ok(entries);
    }
}
