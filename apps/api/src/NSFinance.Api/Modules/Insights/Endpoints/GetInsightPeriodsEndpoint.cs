using NSFinance.Api.Modules.Insights.Services;

namespace NSFinance.Api.Modules.Insights.Endpoints;

public static class GetInsightPeriodsEndpoint
{
    public static async Task<IResult> HandleAsync(
        InsightPeriodsService insightPeriodsService,
        int? months,
        CancellationToken cancellationToken)
    {
        var result = await insightPeriodsService.GetMonthlyPeriodsAsync(months, cancellationToken);
        return Results.Ok(result);
    }
}
