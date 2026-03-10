using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Transactions.Validators;
using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Transactions.Endpoints;

public static class CreateTransactionEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateTransactionRequest request,
        TransactionService transactionService,
        CancellationToken cancellationToken)
    {
        var errors = TransactionRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var (transaction, error) = await transactionService.CreateTransactionAsync(request, cancellationToken);
        if (error is not null)
        {
            return Results.BadRequest(new ApiErrorResponse(error, "transaction_create_failed"));
        }

        return Results.Created($"/api/transactions/{transaction!.Id}", transaction);
    }
}
