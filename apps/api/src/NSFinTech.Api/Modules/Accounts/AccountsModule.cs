using NSFinTech.Api.Modules.Accounts.Endpoints;

namespace NSFinTech.Api.Modules.Accounts;

public static class AccountsModule
{
    public static IEndpointRouteBuilder MapAccountsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts")
            .WithTags("Accounts")
            .RequireAuthorization();

        group.MapGet("/", GetAccountsEndpoint.HandleAsync)
            .WithName("GetAccounts");

        group.MapPost("/", CreateAccountEndpoint.HandleAsync)
            .WithName("CreateAccount");

        group.MapGet("/{id:guid}", GetAccountByIdEndpoint.HandleAsync)
            .WithName("GetAccountById");

        group.MapPut("/{id:guid}", UpdateAccountEndpoint.HandleAsync)
            .WithName("UpdateAccount");

        group.MapDelete("/{id:guid}", DeleteAccountEndpoint.HandleAsync)
            .WithName("DeleteAccount");

        group.MapGet("/{id:guid}/transactions", GetAccountTransactionsEndpoint.HandleAsync)
            .WithName("GetAccountTransactions");

        return app;
    }
}
