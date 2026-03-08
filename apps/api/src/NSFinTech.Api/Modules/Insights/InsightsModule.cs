using NSFinTech.Api.Modules.Insights.Endpoints;

namespace NSFinTech.Api.Modules.Insights;

public static class InsightsModule
{
    public static IEndpointRouteBuilder MapInsightsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();

        group.MapGet("/summary", GetDashboardSummaryEndpoint.HandleAsync)
            .WithName("GetDashboardSummary");

        return app;
    }
}
