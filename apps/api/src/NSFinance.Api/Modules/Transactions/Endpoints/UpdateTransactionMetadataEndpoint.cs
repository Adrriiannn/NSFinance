using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Transactions.Validators;

namespace NSFinance.Api.Modules.Transactions.Endpoints;

public static class UpdateTransactionMetadataEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateTransactionMetadataRequest request,
        TransactionService transactionService,
        ExpenseTaxonomyService expenseTaxonomyService,
        CancellationToken cancellationToken)
    {
        var errors = TransactionMetadataRequestValidator.Validate(request, expenseTaxonomyService);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var (transaction, errorCode, errorMessage) = await transactionService.UpdateTransactionMetadataAsync(
            id,
            request,
            cancellationToken);

        if (errorCode is "transaction_not_found")
        {
            return Results.NotFound(new ApiErrorResponse(errorMessage ?? "Transaction not found.", errorCode));
        }

        if (errorCode is not null)
        {
            return Results.BadRequest(new ApiErrorResponse(errorMessage ?? "Could not update transaction metadata.", errorCode));
        }

        return Results.Ok(transaction);
    }
}
