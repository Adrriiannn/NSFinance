using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSFinance.Api.Modules.Accounts;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions;
using NSFinance.Api.Modules.Transactions.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class ManualCreationRouteRemovalTests
{
    [Fact]
    public async Task AccountAndTransactionModules_DoNotExposeManualCreateRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<ExpenseTaxonomyService>();
        builder.Services.AddScoped<TransactionService>();
        await using var app = builder.Build();
        app.MapAccountsModule();
        app.MapTransactionsModule();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.Contains(endpoints, endpoint => HasMethod(endpoint, "/api/accounts/", "GET"));
        Assert.Contains(endpoints, endpoint => HasMethod(endpoint, "/api/transactions/", "GET"));
        Assert.DoesNotContain(endpoints, endpoint => HasMethod(endpoint, "/api/accounts/", "POST"));
        Assert.DoesNotContain(endpoints, endpoint => HasMethod(endpoint, "/api/transactions/", "POST"));
    }

    private static bool HasMethod(RouteEndpoint endpoint, string pattern, string method) =>
        endpoint.RoutePattern.RawText == pattern
        && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true;
}
