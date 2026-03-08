using NSFinTech.Api.Modules.Transactions.Services;
using NSFinTech.Api.Common.Contracts;

namespace NSFinTech.Api.Modules.Transactions.Endpoints;

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
