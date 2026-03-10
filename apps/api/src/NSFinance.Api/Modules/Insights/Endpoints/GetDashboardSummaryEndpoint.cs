using NSFinance.Api.Modules.Insights.Services;

namespace NSFinance.Api.Modules.Insights.Endpoints;

public static class GetDashboardSummaryEndpoint
{
    public static async Task<IResult> HandleAsync(
        DashboardService dashboardService,
        CancellationToken cancellationToken)
    {
        var summary = await dashboardService.GetSummaryAsync(cancellationToken);
        return Results.Ok(summary);
    }
}
