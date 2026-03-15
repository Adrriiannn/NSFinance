using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class GetCommunityExpensePlansEndpoint
{
    public static async Task<IResult> HandleAsync(
        ExpensePlanCommunityService communityService,
        string? search,
        string? sort,
        string? planType,
        string? creator,
        bool templatesOnly,
        int? take,
        CancellationToken cancellationToken)
    {
        var plans = await communityService.GetCommunityPlansAsync(
            new BrowseExpensePlanPublicationsRequest(search, sort, planType, creator, templatesOnly, take),
            cancellationToken);
        return Results.Ok(plans);
    }
}
