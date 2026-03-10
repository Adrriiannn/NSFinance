using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Accounts.Endpoints;

public static class GetAccountTransactionsEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        TransactionService transactionService,
        CancellationToken cancellationToken)
    {
        var exists = await transactionService.AccountExistsForCurrentUserAsync(id, cancellationToken);
        if (!exists)
        {
            return Results.NotFound(new ApiErrorResponse("Account not found.", "account_not_found"));
        }

        var transactions = await transactionService.GetTransactionsAsync(id, cancellationToken);
        return Results.Ok(transactions);
    }
}
