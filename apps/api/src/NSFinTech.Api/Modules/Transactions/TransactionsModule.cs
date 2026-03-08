using NSFinTech.Api.Modules.Transactions.Endpoints;

namespace NSFinTech.Api.Modules.Transactions;

public static class TransactionsModule
{
    public static IEndpointRouteBuilder MapTransactionsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions")
            .WithTags("Transactions")
            .RequireAuthorization();

        group.MapGet("/", GetTransactionsEndpoint.HandleAsync)
            .WithName("GetTransactions");

        group.MapGet("/{id:guid}", GetTransactionByIdEndpoint.HandleAsync)
            .WithName("GetTransactionById");

        group.MapPost("/", CreateTransactionEndpoint.HandleAsync)
            .WithName("CreateTransaction");

        return app;
    }
}
