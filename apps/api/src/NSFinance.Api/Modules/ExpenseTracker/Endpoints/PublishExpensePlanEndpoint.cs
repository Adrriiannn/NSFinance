using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class PublishExpensePlanEndpoint
{
    public static async Task<IResult> HandleAsync(
        PublishExpensePlanRequest request,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var errors = ExpensePlanCommunityRequestValidator.ValidatePublish(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var publication = await communityService.PublishPlanAsync(request, cancellationToken);
            return Results.Created($"/api/expense-tracker/community/{publication.Id}", publication);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }
}
