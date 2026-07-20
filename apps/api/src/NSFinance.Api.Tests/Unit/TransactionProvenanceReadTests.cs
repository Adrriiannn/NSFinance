using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class TransactionProvenanceReadTests
{
    [Fact]
    public async Task CanonicalReads_StatementDate_ExposeSafeProvenanceConsistently()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var transactionId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var committedUtc = new DateTime(2026, 7, 15, 8, 30, 0, DateTimeKind.Utc);
        var orderingAtUtc = new DateTime(2026, 7, 10, 23, 0, 0, DateTimeKind.Utc);
        var effectiveDate = new DateOnly(2026, 7, 11);

        dbContext.Transactions.Add(new Transaction
        {
            Id = transactionId,
            FinancialAccountId = seeded.AccountId,
            Amount = -24.50m,
            Currency = "EUR",
            Description = "Synthetic grocery purchase",
            EntryKind = TransactionEntryKinds.StatementImport,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = orderingAtUtc,
            CreatedUtc = committedUtc
        });
        dbContext.ImportJobs.Add(new ImportJob
        {
            Id = batchId,
            UserId = seeded.UserId,
            FinancialAccountId = seeded.AccountId,
            FileName = "private-source-name.csv",
            Kind = ImportJobKinds.StatementCsv,
            Status = StatementImportBatchStatuses.Committed,
            SourceFingerprint = "private-source-fingerprint",
            MappingFingerprint = "private-mapping-fingerprint",
            ParserVersion = "private-parser-version",
            MappingVersion = "private-mapping-version",
            MappingJson = "{\"private\":\"mapping\"}",
            AccountCurrency = "EUR",
            CommittedUtc = committedUtc,
            CreatedUtc = committedUtc,
            UpdatedUtc = committedUtc
        });
        dbContext.StatementImportRows.Add(new StatementImportRow
        {
            Id = Guid.NewGuid(),
            ImportJobId = batchId,
            RowNumber = 17,
            RowFingerprint = "private-row-fingerprint",
            SourceReferenceFingerprint = "private-reference-fingerprint",
            ValidationStatus = StatementImportValidationStatuses.Valid,
            DuplicateClassification = StatementImportDuplicateClassifications.None,
            ReviewDisposition = StatementImportReviewDispositions.Included,
            SourceEvidenceJson = "{\"private\":\"evidence\"}",
            EffectiveDate = effectiveDate,
            TimestampPrecision = StatementImportTimestampPrecisions.Date,
            Description = "Synthetic grocery purchase",
            Amount = -24.50m,
            Currency = "EUR",
            CommittedTransactionId = transactionId,
            CreatedUtc = committedUtc,
            UpdatedUtc = committedUtc
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var listRow = Assert.Single(await service.GetTransactionsAsync(null, CancellationToken.None));
        var page = await service.GetTransactionsPageAsync(
            new TransactionPageRequest(null, 20, null, null, null, null),
            CancellationToken.None);
        var pageRow = Assert.Single(page.Value!.Items);
        var detailRow = await service.GetTransactionByIdAsync(transactionId, CancellationToken.None);

        Assert.NotNull(detailRow);
        Assert.Equal(listRow, pageRow);
        Assert.Equal(listRow, detailRow);
        Assert.Equal(FinancialAccountSources.Manual, listRow.AccountSource);
        Assert.Equal("EUR", listRow.AccountCurrency);
        Assert.Equal(orderingAtUtc, listRow.BookedAtUtc);
        Assert.Equal(StatementImportTimestampPrecisions.Date, listRow.EffectiveTime.Precision);
        Assert.Equal(effectiveDate, listRow.EffectiveTime.Date);
        Assert.Null(listRow.EffectiveTime.InstantUtc);
        Assert.Equal(batchId, listRow.StatementImport!.BatchId);
        Assert.Equal(17, listRow.StatementImport.RowNumber);
        Assert.Equal(committedUtc, listRow.StatementImport.CommittedUtc);

        var json = JsonSerializer.Serialize(listRow);
        Assert.DoesNotContain("private-source-name.csv", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-source-fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-mapping-fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-parser-version", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-mapping-version", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-row-fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-reference-fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"private\":\"evidence\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanonicalReads_StatementInstant_ExposeOriginalUtcInstant()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var transactionId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var instantUtc = new DateTime(2026, 7, 15, 9, 12, 34, DateTimeKind.Utc);
        var committedUtc = instantUtc.AddMinutes(2);

        await AddStatementImportAsync(
            dbContext,
            seeded,
            transactionId,
            batchId,
            committedUtc,
            effectiveDate: null,
            effectiveAtUtc: instantUtc,
            StatementImportTimestampPrecisions.Instant);

        var row = await CreateService(dbContext, seeded.UserId)
            .GetTransactionByIdAsync(transactionId, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal(StatementImportTimestampPrecisions.Instant, row.EffectiveTime.Precision);
        Assert.Null(row.EffectiveTime.Date);
        Assert.Equal(instantUtc, row.EffectiveTime.InstantUtc);
        Assert.Equal(instantUtc, row.BookedAtUtc);
        Assert.Equal(batchId, row.StatementImport!.BatchId);
    }

    [Fact]
    public async Task DashboardRecentTransactions_UsesCanonicalStatementProvenance()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var transactionId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var instantUtc = new DateTime(2026, 7, 15, 9, 45, 0, DateTimeKind.Utc);
        await AddStatementImportAsync(
            dbContext,
            seeded,
            transactionId,
            batchId,
            instantUtc.AddMinutes(1),
            effectiveDate: null,
            effectiveAtUtc: instantUtc,
            StatementImportTimestampPrecisions.Instant);

        var currentUser = new TestCurrentUserProvider(seeded.UserId);
        var summary = await new DashboardService(
                dbContext,
                currentUser,
                new AccountBalanceReadService(dbContext, currentUser, TimeProvider.System),
                new ExpenseTaxonomyService())
            .GetSummaryAsync(CancellationToken.None);

        var row = Assert.Single(summary.RecentTransactions);
        Assert.Equal(transactionId, row.Id);
        Assert.Equal(batchId, row.StatementImport!.BatchId);
        Assert.Equal(instantUtc, row.EffectiveTime.InstantUtc);
        Assert.Equal(FinancialAccountSources.Manual, row.AccountSource);
        Assert.Equal("EUR", row.AccountCurrency);
    }

    [Fact]
    public async Task CanonicalReads_StatementCurrencyDrift_FailsClosed()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var transactionId = Guid.NewGuid();
        var instantUtc = new DateTime(2026, 7, 15, 9, 55, 0, DateTimeKind.Utc);
        await AddStatementImportAsync(
            dbContext,
            seeded,
            transactionId,
            Guid.NewGuid(),
            instantUtc.AddMinutes(1),
            effectiveDate: null,
            effectiveAtUtc: instantUtc,
            StatementImportTimestampPrecisions.Instant);
        var transaction = await dbContext.Transactions.SingleAsync(row => row.Id == transactionId);
        transaction.Currency = "USD";
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(dbContext, seeded.UserId)
                .GetTransactionByIdAsync(transactionId, CancellationToken.None));

        Assert.Contains(transactionId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanonicalReads_ManualAndProviderRows_ExposeAccountSourceWithoutImportBatch()
    {
        await using var dbContext = CreateDbContext();
        var manual = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var provider = await SeedAccountAsync(
            dbContext,
            FinancialAccountSources.ProviderProjected,
            manual.UserId);
        var manualId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);

        dbContext.Transactions.AddRange(
            CreateTransaction(
                manualId,
                manual.AccountId,
                TransactionEntryKinds.ManualAdjustment,
                now),
            CreateTransaction(
                providerId,
                provider.AccountId,
                TransactionEntryKinds.Ordinary,
                now.AddMinutes(-1)));
        await dbContext.SaveChangesAsync();

        var rows = await CreateService(dbContext, manual.UserId)
            .GetTransactionsAsync(null, CancellationToken.None);
        var manualRow = Assert.Single(rows, row => row.Id == manualId);
        var providerRow = Assert.Single(rows, row => row.Id == providerId);

        Assert.Equal(FinancialAccountSources.Manual, manualRow.AccountSource);
        Assert.Equal(FinancialAccountSources.ProviderProjected, providerRow.AccountSource);
        Assert.Equal("EUR", manualRow.AccountCurrency);
        Assert.Equal(StatementImportTimestampPrecisions.Instant, manualRow.EffectiveTime.Precision);
        Assert.Equal(now, manualRow.EffectiveTime.InstantUtc);
        Assert.Null(manualRow.StatementImport);
        Assert.Null(providerRow.StatementImport);
    }

    [Fact]
    public async Task CanonicalReads_ProviderMidnightInstant_ExposesDatePrecision()
    {
        await using var dbContext = CreateDbContext();
        var provider = await SeedAccountAsync(dbContext, FinancialAccountSources.ProviderProjected);
        var utcMidnightId = Guid.NewGuid();
        var irishSummerMidnightId = Guid.NewGuid();
        var afterMidnightId = Guid.NewGuid();
        var utcMidnight = new DateTime(2026, 1, 17, 0, 0, 0, DateTimeKind.Utc);
        // 23:00Z on 16 July is 00:00 Irish Summer Time on 17 July.
        var irishSummerMidnightUtc = new DateTime(2026, 7, 16, 23, 0, 0, DateTimeKind.Utc);
        var afterMidnightUtc = new DateTime(2026, 7, 17, 0, 0, 1, DateTimeKind.Utc);

        dbContext.Transactions.AddRange(
            CreateTransaction(
                utcMidnightId,
                provider.AccountId,
                TransactionEntryKinds.Ordinary,
                utcMidnight),
            CreateTransaction(
                irishSummerMidnightId,
                provider.AccountId,
                TransactionEntryKinds.Ordinary,
                irishSummerMidnightUtc),
            CreateTransaction(
                afterMidnightId,
                provider.AccountId,
                TransactionEntryKinds.Ordinary,
                afterMidnightUtc));
        await dbContext.SaveChangesAsync();

        var rows = await CreateService(dbContext, provider.UserId)
            .GetTransactionsAsync(null, CancellationToken.None);
        var utcMidnightRow = Assert.Single(rows, row => row.Id == utcMidnightId);
        var irishMidnightRow = Assert.Single(rows, row => row.Id == irishSummerMidnightId);
        var afterMidnightRow = Assert.Single(rows, row => row.Id == afterMidnightId);

        // Date-granular provider bookings arrive as exact-midnight instants in UTC or
        // in the Irish local day; the read contract must not invent a "00:00" time.
        Assert.Equal(StatementImportTimestampPrecisions.Date, utcMidnightRow.EffectiveTime.Precision);
        Assert.Equal(new DateOnly(2026, 1, 17), utcMidnightRow.EffectiveTime.Date);
        Assert.Null(utcMidnightRow.EffectiveTime.InstantUtc);

        Assert.Equal(StatementImportTimestampPrecisions.Date, irishMidnightRow.EffectiveTime.Precision);
        Assert.Equal(new DateOnly(2026, 7, 17), irishMidnightRow.EffectiveTime.Date);
        Assert.Null(irishMidnightRow.EffectiveTime.InstantUtc);

        // One second past midnight is a genuine instant and keeps full precision.
        Assert.Equal(StatementImportTimestampPrecisions.Instant, afterMidnightRow.EffectiveTime.Precision);
        Assert.Equal(afterMidnightUtc, afterMidnightRow.EffectiveTime.InstantUtc);
        Assert.Null(afterMidnightRow.EffectiveTime.Date);
    }

    [Fact]
    public async Task CanonicalReads_CrossUserImportMetadata_IsNotExposed()
    {
        await using var dbContext = CreateDbContext();
        var primary = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var other = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var transactionId = Guid.NewGuid();
        var otherBatchId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 15, 11, 0, 0, DateTimeKind.Utc);
        dbContext.Transactions.Add(CreateTransaction(
            transactionId,
            primary.AccountId,
            TransactionEntryKinds.ManualAdjustment,
            now));
        dbContext.ImportJobs.Add(new ImportJob
        {
            Id = otherBatchId,
            UserId = other.UserId,
            FinancialAccountId = other.AccountId,
            FileName = "other-user-private.csv",
            Kind = ImportJobKinds.StatementCsv,
            Status = StatementImportBatchStatuses.Committed,
            AccountCurrency = "EUR",
            CommittedUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now
        });
        dbContext.StatementImportRows.Add(new StatementImportRow
        {
            Id = Guid.NewGuid(),
            ImportJobId = otherBatchId,
            RowNumber = 1,
            RowFingerprint = "other-user-private-fingerprint",
            ValidationStatus = StatementImportValidationStatuses.Valid,
            DuplicateClassification = StatementImportDuplicateClassifications.None,
            ReviewDisposition = StatementImportReviewDispositions.Included,
            EffectiveAtUtc = now,
            TimestampPrecision = StatementImportTimestampPrecisions.Instant,
            CommittedTransactionId = transactionId,
            CreatedUtc = now,
            UpdatedUtc = now
        });
        await dbContext.SaveChangesAsync();

        var row = await CreateService(dbContext, primary.UserId)
            .GetTransactionByIdAsync(transactionId, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Null(row.StatementImport);
        var json = JsonSerializer.Serialize(row);
        Assert.DoesNotContain(otherBatchId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("other-user-private", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityList_EqualTimestamps_UsesStableIdTieBreaker()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext, FinancialAccountSources.Manual);
        var now = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var ids = Enumerable.Range(1, 3)
            .Select(value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
            .ToArray();
        dbContext.Transactions.AddRange(ids.Select(id =>
            CreateTransaction(id, seeded.AccountId, TransactionEntryKinds.ManualAdjustment, now)));
        await dbContext.SaveChangesAsync();

        var rows = await CreateService(dbContext, seeded.UserId)
            .GetTransactionsAsync(null, CancellationToken.None);

        Assert.Equal(ids.OrderByDescending(id => id), rows.Select(row => row.Id));
    }

    [Fact]
    public void StatementImportProvenanceQuery_NpgsqlProvider_TranslatesOwnedCommittedProjection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);

        var sql = TransactionProvenanceResolver.BuildStatementImportQuery(
                dbContext,
                Guid.NewGuid(),
                [Guid.NewGuid()])
            .ToQueryString();

        Assert.Contains("StatementImportRows", sql, StringComparison.Ordinal);
        Assert.Contains("ImportJobs", sql, StringComparison.Ordinal);
        Assert.Contains("CommittedTransactionId", sql, StringComparison.Ordinal);
        Assert.Contains("UserId", sql, StringComparison.Ordinal);
        Assert.Contains("statement_csv", sql, StringComparison.Ordinal);
        Assert.Contains("committed", sql, StringComparison.Ordinal);
    }

    private static async Task AddStatementImportAsync(
        AppDbContext dbContext,
        (Guid UserId, Guid AccountId) seeded,
        Guid transactionId,
        Guid batchId,
        DateTime committedUtc,
        DateOnly? effectiveDate,
        DateTime? effectiveAtUtc,
        string precision)
    {
        var bookedAtUtc = effectiveAtUtc
            ?? new DateTime(
                effectiveDate!.Value.Year,
                effectiveDate.Value.Month,
                effectiveDate.Value.Day,
                0,
                0,
                0,
                DateTimeKind.Utc);
        dbContext.Transactions.Add(new Transaction
        {
            Id = transactionId,
            FinancialAccountId = seeded.AccountId,
            Amount = -10m,
            Currency = "EUR",
            Description = "Synthetic import row",
            EntryKind = TransactionEntryKinds.StatementImport,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = committedUtc
        });
        dbContext.ImportJobs.Add(new ImportJob
        {
            Id = batchId,
            UserId = seeded.UserId,
            FinancialAccountId = seeded.AccountId,
            FileName = "synthetic.csv",
            Kind = ImportJobKinds.StatementCsv,
            Status = StatementImportBatchStatuses.Committed,
            AccountCurrency = "EUR",
            CommittedUtc = committedUtc,
            CreatedUtc = committedUtc,
            UpdatedUtc = committedUtc
        });
        dbContext.StatementImportRows.Add(new StatementImportRow
        {
            Id = Guid.NewGuid(),
            ImportJobId = batchId,
            RowNumber = 1,
            RowFingerprint = Guid.NewGuid().ToString("N"),
            ValidationStatus = StatementImportValidationStatuses.Valid,
            DuplicateClassification = StatementImportDuplicateClassifications.None,
            ReviewDisposition = StatementImportReviewDispositions.Included,
            EffectiveDate = effectiveDate,
            EffectiveAtUtc = effectiveAtUtc,
            TimestampPrecision = precision,
            Description = "Synthetic import row",
            Amount = -10m,
            Currency = "EUR",
            CommittedTransactionId = transactionId,
            CreatedUtc = committedUtc,
            UpdatedUtc = committedUtc
        });
        await dbContext.SaveChangesAsync();
    }

    private static Transaction CreateTransaction(
        Guid id,
        Guid accountId,
        string entryKind,
        DateTime bookedAtUtc)
    {
        return new Transaction
        {
            Id = id,
            FinancialAccountId = accountId,
            Amount = -5m,
            Currency = "EUR",
            Description = "Synthetic transaction",
            EntryKind = entryKind,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
    }

    private static TransactionService CreateService(AppDbContext dbContext, Guid userId)
    {
        return new TransactionService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new ExpenseTaxonomyService(),
            new MerchantCorrectionLearningService(dbContext, NullLogger<MerchantCorrectionLearningService>.Instance));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"transaction-provenance-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedAccountAsync(
        AppDbContext dbContext,
        string source,
        Guid? existingUserId = null)
    {
        var now = DateTime.UtcNow;
        var userId = existingUserId ?? Guid.NewGuid();
        if (!existingUserId.HasValue)
        {
            var email = $"provenance-{userId:N}@local";
            dbContext.Users.Add(new User
            {
                Id = userId,
                PrimaryEmail = email,
                NormalizedEmail = email,
                DisplayName = "Provenance Tester",
                Status = "active",
                OnboardingStatus = "profile_created",
                Role = "user",
                CreatedUtc = now,
                UpdatedUtc = now,
                EmailVerified = true,
                Timezone = "Europe/Dublin",
                Locale = "en-IE",
                PreferredCurrency = "EUR",
                PlanTier = "standard"
            });
        }

        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = source == FinancialAccountSources.Manual ? "Manual account" : "Connected account",
            Type = "Current",
            Currency = "EUR",
            Source = source,
            CreatedUtc = now
        });
        await dbContext.SaveChangesAsync();

        return (userId, accountId);
    }

    private sealed class TestCurrentUserProvider(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid resolvedUserId)
        {
            resolvedUserId = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = Guid.Empty;
            return false;
        }
    }
}
