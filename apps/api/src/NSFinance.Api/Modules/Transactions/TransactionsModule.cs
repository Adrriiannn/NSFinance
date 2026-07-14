using NSFinance.Api.Modules.Transactions.Endpoints;

namespace NSFinance.Api.Modules.Transactions;

public static class TransactionsModule
{
    public static IEndpointRouteBuilder MapTransactionsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions")
            .WithTags("Transactions")
            .RequireAuthorization();

        group.MapGet("/", GetTransactionsEndpoint.HandleAsync)
            .WithName("GetTransactions");

        group.MapGet("/page", GetTransactionPageEndpoint.HandleAsync)
            .WithName("GetTransactionPage");

        group.MapGet("/{id:guid}", GetTransactionByIdEndpoint.HandleAsync)
            .WithName("GetTransactionById");

        group.MapPost("/", CreateTransactionEndpoint.HandleAsync)
            .WithName("CreateTransaction");

        group.MapPatch("/{id:guid}", UpdateTransactionMetadataEndpoint.HandleAsync)
            .WithName("UpdateTransactionMetadata");

        return app;
    }
}
