using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Transactions.Endpoints;

public static class GetTransactionByIdEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        TransactionService transactionService,
        CancellationToken cancellationToken)
    {
        var transaction = await transactionService.GetTransactionByIdAsync(id, cancellationToken);
        return transaction is null
            ? Results.NotFound(new ApiErrorResponse("Transaction not found.", "transaction_not_found"))
            : Results.Ok(transaction);
    }
}
