using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Tests.Unit;

public sealed class BankRecurringPaymentQueriesTests
{
    [Fact]
    public void DirectDebits_TranslateWithProductionProvider()
    {
        using var dbContext = CreateNpgsqlContext();

        var sql = BankRecurringPaymentQueries
            .BuildDirectDebits(dbContext, Guid.NewGuid())
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandingOrders_TranslateWithProductionProvider()
    {
        using var dbContext = CreateNpgsqlContext();

        var sql = BankRecurringPaymentQueries
            .BuildStandingOrders(dbContext, Guid.NewGuid())
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static AppDbContext CreateNpgsqlContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation;Username=unused;Password=unused")
            .Options;

        return new AppDbContext(options);
    }
}
