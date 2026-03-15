using NSFinance.Api.Modules.ExpenseTracker.Endpoints;

namespace NSFinance.Api.Modules.ExpenseTracker;

public static class ExpenseTrackerModule
{
    public static IEndpointRouteBuilder MapExpenseTrackerModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expense-tracker")
            .WithTags("Expense Tracker")
            .RequireAuthorization();

        group.MapGet("/taxonomy", GetExpenseTrackerTaxonomyEndpoint.Handle)
            .WithName("GetExpenseTrackerTaxonomy");

        group.MapGet("/entries", GetExpenseTrackerEntriesEndpoint.HandleAsync)
            .WithName("GetExpenseTrackerEntries");

        group.MapGet("/entries/{id:guid}", GetExpenseTrackerEntryByIdEndpoint.HandleAsync)
            .WithName("GetExpenseTrackerEntryById");

        group.MapPost("/entries", CreateExpenseTrackerEntryEndpoint.HandleAsync)
            .WithName("CreateExpenseTrackerEntry");

        group.MapPut("/entries/{id:guid}", UpdateExpenseTrackerEntryEndpoint.HandleAsync)
            .WithName("UpdateExpenseTrackerEntry");

        group.MapDelete("/entries/{id:guid}", DeleteExpenseTrackerEntryEndpoint.HandleAsync)
            .WithName("DeleteExpenseTrackerEntry");

        return app;
    }
}
