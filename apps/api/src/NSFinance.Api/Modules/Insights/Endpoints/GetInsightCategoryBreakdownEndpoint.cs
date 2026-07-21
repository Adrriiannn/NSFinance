using NSFinance.Api.Modules.Insights.Services;

namespace NSFinance.Api.Modules.Insights.Endpoints;

public static class GetInsightCategoryBreakdownEndpoint
{
    public static async Task<IResult> HandleAsync(
        InsightCategoryBreakdownService breakdownService,
        int? months,
        CancellationToken cancellationToken)
    {
        var result = await breakdownService.GetMonthlyBreakdownAsync(months, cancellationToken);
        return Results.Ok(result);
    }
}
