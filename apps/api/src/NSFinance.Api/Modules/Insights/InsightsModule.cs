using NSFinance.Api.Modules.Insights.Endpoints;

namespace NSFinance.Api.Modules.Insights;

public static class InsightsModule
{
    public static IEndpointRouteBuilder MapInsightsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();

        group.MapGet("/summary", GetDashboardSummaryEndpoint.HandleAsync)
            .WithName("GetDashboardSummary");

        var insightsGroup = app.MapGroup("/api/insights")
            .WithTags("Insights")
            .RequireAuthorization();

        insightsGroup.MapGet("/periods", GetInsightPeriodsEndpoint.HandleAsync)
            .WithName("GetInsightPeriods");

        insightsGroup.MapGet("/categories", GetInsightCategoryBreakdownEndpoint.HandleAsync)
            .WithName("GetInsightCategoryBreakdown");

        return app;
    }
}
