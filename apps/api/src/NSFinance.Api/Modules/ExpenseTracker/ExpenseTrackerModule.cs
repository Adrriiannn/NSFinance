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

        group.MapGet("/plans", GetExpensePlansEndpoint.HandleAsync)
            .WithName("GetExpensePlans");

        group.MapGet("/plans/active", GetActiveExpensePlansEndpoint.HandleAsync)
            .WithName("GetActiveExpensePlans");

        group.MapGet("/plans/recent", GetRecentExpensePlansEndpoint.HandleAsync)
            .WithName("GetRecentExpensePlans");

        group.MapGet("/plans/{id:guid}", GetExpensePlanByIdEndpoint.HandleAsync)
            .WithName("GetExpensePlanById");

        group.MapPost("/plans", CreateExpensePlanEndpoint.HandleAsync)
            .WithName("CreateExpensePlan");

        group.MapPut("/plans/{id:guid}", UpdateExpensePlanEndpoint.HandleAsync)
            .WithName("UpdateExpensePlan");

        group.MapPost("/plans/{id:guid}/duplicate", DuplicateExpensePlanEndpoint.HandleAsync)
            .WithName("DuplicateExpensePlan");

        group.MapPost("/plans/{id:guid}/transition", TransitionExpensePlanEndpoint.HandleAsync)
            .WithName("TransitionExpensePlan");

        group.MapGet("/community", GetCommunityExpensePlansEndpoint.HandleAsync)
            .WithName("GetCommunityExpensePlans");

        group.MapGet("/community/mine", GetMyPublishedExpensePlansDashboardEndpoint.HandleAsync)
            .WithName("GetMyPublishedExpensePlansDashboard");

        group.MapGet("/community/{id:guid}", GetCommunityExpensePlanByIdEndpoint.HandleAsync)
            .WithName("GetCommunityExpensePlanById");

        group.MapPost("/community/publish", PublishExpensePlanEndpoint.HandleAsync)
            .WithName("PublishExpensePlan");

        group.MapPut("/community/{id:guid}", UpdateExpensePlanPublicationEndpoint.HandleAsync)
            .WithName("UpdateExpensePlanPublication");

        group.MapPost("/community/{id:guid}/like", ToggleExpensePlanPublicationLikeEndpoint.HandleAsync)
            .WithName("ToggleExpensePlanPublicationLike");

        group.MapPost("/community/{id:guid}/use", UseExpensePlanPublicationEndpoint.HandleAsync)
            .WithName("UseExpensePlanPublication");

        group.MapPost("/community/{id:guid}/report", ReportExpensePlanPublicationEndpoint.HandleAsync)
            .WithName("ReportExpensePlanPublication");

        group.MapPost("/community/{id:guid}/unpublish", UnpublishExpensePlanPublicationEndpoint.HandleAsync)
            .WithName("UnpublishExpensePlanPublication");

        group.MapPost("/community/{id:guid}/rescan", RescanExpensePlanPublicationEndpoint.HandleAsync)
            .WithName("RescanExpensePlanPublication");

        return app;
    }
}
