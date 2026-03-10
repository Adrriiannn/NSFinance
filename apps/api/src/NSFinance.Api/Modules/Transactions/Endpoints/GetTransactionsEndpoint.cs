using NSFinance.Api.Modules.Transactions.Services;

namespace NSFinance.Api.Modules.Transactions.Endpoints;

public static class GetTransactionsEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid? accountId,
        TransactionService transactionService,
        CancellationToken cancellationToken)
    {
        var transactions = await transactionService.GetTransactionsAsync(accountId, cancellationToken);
        return Results.Ok(transactions);
    }
}
