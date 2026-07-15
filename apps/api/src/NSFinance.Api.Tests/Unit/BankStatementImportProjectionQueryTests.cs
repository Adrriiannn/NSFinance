using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Tests.Unit;

public sealed class BankStatementImportProjectionQueryTests
{
    [Fact]
    public void ProjectionFingerprints_KeepExactInstantMatchingStrict_AndNormalizeOnlyDateFallback()
    {
        var bookedAtUtc = new DateTime(2026, 4, 2, 8, 30, 0, DateTimeKind.Utc);
        var effectiveDate = new DateOnly(2026, 4, 2);

        Assert.NotEqual(
            BankSyncService.CreateProjectionFingerprint(-74.11m, "EUR", bookedAtUtc, "Merchant #001"),
            BankSyncService.CreateProjectionFingerprint(-74.11m, "EUR", bookedAtUtc, "merchant 001"));
        Assert.Equal(
            BankSyncService.CreateProjectionDateFingerprint(-74.11m, "EUR", effectiveDate, "Merchant #001"),
            BankSyncService.CreateProjectionDateFingerprint(-74.11m, "EUR", effectiveDate, "merchant 001"));
    }

    [Fact]
    public void DateOnlyImportedProjectionQuery_TranslatesForNpgsql()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);

        var sql = BankSyncService
            .BuildDateOnlyImportedProjectionQuery(dbContext, Guid.NewGuid())
            .ToQueryString();

        Assert.Contains("StatementImportRows", sql, StringComparison.Ordinal);
        Assert.Contains("ImportJobs", sql, StringComparison.Ordinal);
        Assert.Contains("Transactions", sql, StringComparison.Ordinal);
    }
}
