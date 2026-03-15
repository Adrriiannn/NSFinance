using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class ReportExpensePlanPublicationEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ReportExpensePlanPublicationRequest request,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var errors = ExpensePlanCommunityRequestValidator.ValidateReport(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var publication = await communityService.ReportPublicationAsync(id, request, cancellationToken);
            return publication is null ? Results.NotFound() : Results.Ok(publication);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }
}
