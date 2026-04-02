using NSFinance.Api.Modules.Banking.Endpoints;

namespace NSFinance.Api.Modules.Banking;

public static class BankingModule
{
    public static IEndpointRouteBuilder MapBankingModule(this IEndpointRouteBuilder app)
    {
        var protectedGroup = app.MapGroup("/api/banking")
            .WithTags("Banking")
            .RequireAuthorization();

        protectedGroup.MapPost("/truelayer/link", StartTrueLayerLinkEndpoint.HandleAsync)
            .WithName("StartTrueLayerLink");

        protectedGroup.MapGet("/connections", GetBankConnectionsEndpoint.HandleAsync)
            .WithName("GetBankConnections");

        protectedGroup.MapGet("/connected-banks", GetConnectedBanksEndpoint.HandleAsync)
            .WithName("GetConnectedBanks");

        protectedGroup.MapGet("/enrichment-progress", GetBankEnrichmentProgressEndpoint.HandleAsync)
            .WithName("GetBankEnrichmentProgress");

        protectedGroup.MapGet("/connections/{connectionId:guid}", GetBankConnectionEndpoint.HandleAsync)
            .WithName("GetBankConnection");

        protectedGroup.MapGet("/accounts", GetLinkedBankAccountsEndpoint.HandleAsync)
            .WithName("GetLinkedBankAccounts");

        protectedGroup.MapGet("/cards", GetLinkedBankCardsEndpoint.HandleAsync)
            .WithName("GetLinkedBankCards");

        protectedGroup.MapGet("/accounts/{accountId:guid}/balances", GetAccountBalancesEndpoint.HandleAsync)
            .WithName("GetBankAccountBalances");

        protectedGroup.MapGet("/accounts/{accountId:guid}/transactions", GetAccountRawTransactionsEndpoint.HandleAsync)
            .WithName("GetBankAccountRawTransactions");

        protectedGroup.MapGet("/accounts/{accountId:guid}/recurring-payments", GetAccountRecurringPaymentsEndpoint.HandleAsync)
            .WithName("GetBankAccountRecurringPayments");

        protectedGroup.MapGet("/recurring-payments", GetRecurringPaymentsEndpoint.HandleAsync)
            .WithName("GetRecurringPayments");

        protectedGroup.MapPost("/connections/{connectionId:guid}/sync", SyncBankConnectionEndpoint.HandleAsync)
            .WithName("SyncBankConnection");

        protectedGroup.MapPost("/sync", SyncAllBankConnectionsEndpoint.HandleAsync)
            .WithName("SyncAllBankConnections");

        protectedGroup.MapPost("/connections/{connectionId:guid}/disconnect", DisconnectBankConnectionEndpoint.HandleAsync)
            .WithName("DisconnectBankConnection");

        app.MapGet("/api/banking/truelayer/callback", TrueLayerCallbackEndpoint.HandleAsync)
            .WithName("TrueLayerCallback")
            .RequireRateLimiting("provider-callback");

        return app;
    }
}

