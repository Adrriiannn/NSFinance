using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;

namespace NSFinance.Api.Modules.ExpenseTracker.Endpoints;

public static class UpdateExpensePlanPublicationEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateExpensePlanPublicationRequest request,
        ExpensePlanCommunityService communityService,
        CancellationToken cancellationToken)
    {
        var errors = ExpensePlanCommunityRequestValidator.ValidateUpdate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var publication = await communityService.UpdatePublicationAsync(id, request, cancellationToken);
            return publication is null ? Results.NotFound() : Results.Ok(publication);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }
}
