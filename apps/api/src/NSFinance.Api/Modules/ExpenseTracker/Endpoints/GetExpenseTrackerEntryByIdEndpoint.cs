using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetExpenseTrackerEntryByIdEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExpenseTrackerService expenseTrackerService,
        CancellationToken cancellationToken)
    {
        var entry = await expenseTrackerService.GetEntryByIdAsync(id, cancellationToken);
        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }
}
