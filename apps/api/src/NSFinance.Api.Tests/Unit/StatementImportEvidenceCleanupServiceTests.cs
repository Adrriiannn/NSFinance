using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportEvidenceCleanupServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 15, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PurgeExpiredAsync_ClearsOnlyExpiredEvidenceAndPreservesNormalizedFacts()
    {
        await using var dbContext = CreateDbContext();
        var expired = CreateRow(UtcNow.AddMinutes(-1), "expired evidence");
        var future = CreateRow(UtcNow.AddMinutes(1), "future evidence");
        var alreadyCleared = CreateRow(null, null);
        dbContext.StatementImportRows.AddRange(expired, future, alreadyCleared);
        await dbContext.SaveChangesAsync();

        var purgedCount = await new StatementImportEvidenceCleanupService(
                dbContext,
                new FixedTimeProvider(UtcNow))
            .PurgeExpiredAsync(CancellationToken.None);

        Assert.Equal(1, purgedCount);
        var rows = await dbContext.StatementImportRows
            .AsNoTracking()
            .ToDictionaryAsync(row => row.Id);
        Assert.Null(rows[expired.Id].SourceEvidenceJson);
        Assert.Null(rows[expired.Id].EvidenceExpiresUtc);
        Assert.Equal("Coffee", rows[expired.Id].Description);
        Assert.Equal(-4.50m, rows[expired.Id].Amount);
        Assert.Equal(UtcNow, rows[expired.Id].UpdatedUtc);
        Assert.Equal("future evidence", rows[future.Id].SourceEvidenceJson);
        Assert.Equal(UtcNow.AddMinutes(1), rows[future.Id].EvidenceExpiresUtc);
        Assert.Null(rows[alreadyCleared.Id].SourceEvidenceJson);
    }

    [Fact]
    public async Task PurgeExpiredAsync_StopsAtThePerRunPrivacyWorkBudget()
    {
        await using var dbContext = CreateDbContext();
        var rows = Enumerable.Range(0, StatementImportEvidenceCleanupService.MaximumRowsPerRun + 1)
            .Select(index => CreateRow(UtcNow.AddMinutes(-1), $"evidence-{index}"))
            .ToList();
        dbContext.StatementImportRows.AddRange(rows);
        await dbContext.SaveChangesAsync();

        var purgedCount = await new StatementImportEvidenceCleanupService(
                dbContext,
                new FixedTimeProvider(UtcNow))
            .PurgeExpiredAsync(CancellationToken.None);

        Assert.Equal(StatementImportEvidenceCleanupService.MaximumRowsPerRun, purgedCount);
        Assert.Equal(
            1,
            await dbContext.StatementImportRows.CountAsync(row => row.SourceEvidenceJson != null));
    }

    private static StatementImportRow CreateRow(DateTime? expiresUtc, string? evidence) =>
        new()
        {
            Id = Guid.NewGuid(),
            ImportJobId = Guid.NewGuid(),
            RowNumber = 1,
            RowFingerprint = new string('a', 64),
            ValidationStatus = StatementImportValidationStatuses.Valid,
            DuplicateClassification = StatementImportDuplicateClassifications.None,
            ReviewDisposition = StatementImportReviewDispositions.Included,
            SourceEvidenceJson = evidence,
            EvidenceExpiresUtc = expiresUtc,
            EffectiveDate = new DateOnly(2026, 7, 1),
            TimestampPrecision = StatementImportTimestampPrecisions.Date,
            Description = "Coffee",
            Amount = -4.50m,
            Currency = "EUR",
            CreatedUtc = UtcNow.AddDays(-1),
            UpdatedUtc = UtcNow.AddDays(-1)
        };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"statement-import-cleanup-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
