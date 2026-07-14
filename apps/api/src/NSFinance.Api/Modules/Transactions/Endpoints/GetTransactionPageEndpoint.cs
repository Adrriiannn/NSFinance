using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Services;

namespace NSFinance.Api.Modules.Transactions.Endpoints;

public static class GetTransactionPageEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid? accountId,
        int? pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? direction,
        string? cursor,
        TransactionService transactionService,
        CancellationToken cancellationToken)
    {
        var result = await transactionService.GetTransactionsPageAsync(
            new TransactionPageRequest(accountId, pageSize, fromUtc, toUtc, direction, cursor),
            cancellationToken);

        return result.Succeeded
            ? Results.Ok(result.Value)
            : result.Error!.ToApiError();
    }
}
