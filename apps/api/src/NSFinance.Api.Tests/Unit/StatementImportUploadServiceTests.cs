using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Imports.Mapping;
using NSFinance.Api.Modules.Imports.Parsing;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportUploadServiceTests
{
    private static readonly DateTime UtcNow =
        new(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task InspectAsync_ReturnsBoundedShapeWithoutServerFingerprints()
    {
        await using var harness = await TestHarness.CreateAsync();
        var file = CsvFile(
            @"C:\private\statement.csv",
            "Date,Description,Amount\n2026-07-01,Coffee,-4.50\n");

        var result = await harness.UploadService.InspectAsync(
            file,
            "comma",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("statement.csv", result.Value!.FileName);
        Assert.Equal(1, result.Value.DataRowCount);
        Assert.Equal(["Date", "Description", "Amount"],
            result.Value.Columns.Select(column => column.Name));
        Assert.Single(result.Value.SampleRows);
        Assert.DoesNotContain(
            result.Value.GetType().GetProperties(),
            property => property.Name.Contains("Fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewAsync_ClassifiesExactExistingTransactionAndStagesOwnerScopedRows()
    {
        await using var harness = await TestHarness.CreateAsync(useActualMappingEngine: true);
        var candidateId = Guid.NewGuid();
        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = candidateId,
            FinancialAccountId = harness.AccountId,
            Amount = -4.50m,
            Currency = "EUR",
            Description = "Coffee",
            BookedAtUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            CreatedUtc = UtcNow
        });
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.UploadService.PreviewAsync(
            CsvFile("statement.csv", "Date,Description,Amount\n2026-07-01,Coffee,-4.50\n"),
            Request(harness.AccountId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Batch.ExactDuplicateRowCount);
        Assert.Equal(0, result.Value.Batch.IncludedRowCount);
        var row = Assert.Single(result.Value.Rows.Items);
        Assert.Equal(StatementImportDuplicateClassifications.Exact, row.DuplicateClassification);
        Assert.Equal(StatementImportReviewDispositions.Excluded, row.ReviewDisposition);
        Assert.Equal(candidateId, row.DuplicateCandidateTransactionId);
        Assert.Single(harness.DbContext.ImportJobs);
        Assert.Single(harness.DbContext.StatementImportRows);
    }

    [Fact]
    public async Task PreviewAsync_LeavesSameValueDifferentDescriptionAsLikelyAndPending()
    {
        await using var harness = await TestHarness.CreateAsync(useActualMappingEngine: true);
        var candidateId = Guid.NewGuid();
        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = candidateId,
            FinancialAccountId = harness.AccountId,
            Amount = -4.50m,
            Currency = "EUR",
            Description = "Coffee Shop A",
            BookedAtUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            CreatedUtc = UtcNow
        });
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.UploadService.PreviewAsync(
            CsvFile("statement.csv", "Date,Description,Amount\n2026-07-01,Coffee Shop B,-4.50\n"),
            Request(harness.AccountId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Batch.LikelyDuplicateRowCount);
        var row = Assert.Single(result.Value.Rows.Items);
        Assert.Equal(StatementImportDuplicateClassifications.Likely, row.DuplicateClassification);
        Assert.Equal(StatementImportReviewDispositions.Pending, row.ReviewDisposition);
        Assert.Equal(candidateId, row.DuplicateCandidateTransactionId);
    }

    [Fact]
    public async Task PreviewAsync_MarksRepeatedSourceRowInvalidAndReplaysIdempotently()
    {
        await using var harness = await TestHarness.CreateAsync();
        const string csv =
            "Date,Description,Amount\n2026-07-01,Coffee,-4.50\n2026-07-01,Coffee,-4.50\n";

        var first = await harness.UploadService.PreviewAsync(
            CsvFile("statement.csv", csv),
            Request(harness.AccountId),
            CancellationToken.None);
        var replay = await harness.UploadService.PreviewAsync(
            CsvFile("statement.csv", csv),
            Request(harness.AccountId),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(1, first.Value!.Batch.ValidRowCount);
        Assert.Equal(1, first.Value.Batch.InvalidRowCount);
        Assert.Equal(
            "duplicate_within_source",
            first.Value.Rows.Items.Single(row => row.RowNumber == 2).ValidationCode);
        Assert.True(replay.Succeeded);
        Assert.True(replay.Value!.Batch.WasReplay);
        Assert.Equal(first.Value.Batch.Id, replay.Value.Batch.Id);
        Assert.Equal(1, await harness.DbContext.ImportJobs.CountAsync());
        Assert.Equal(2, await harness.DbContext.StatementImportRows.CountAsync());
    }

    [Fact]
    public async Task PreviewAsync_DoesNotRevealAnotherUsersAccount()
    {
        await using var harness = await TestHarness.CreateAsync();
        var otherUserId = await harness.SeedUserAsync("other@local");
        var otherAccountId = await harness.SeedAccountAsync(otherUserId);

        var result = await harness.UploadService.PreviewAsync(
            CsvFile("statement.csv", "Date,Description,Amount\n2026-07-01,Coffee,-4.50\n"),
            Request(otherAccountId),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("statement_import_account_not_found", result.Error!.Code);
        Assert.Empty(harness.DbContext.ImportJobs);
    }

    [Fact]
    public async Task PreviewAsync_ActualMapperEvidencePassesTheStagingAllowlist()
    {
        await using var harness = await TestHarness.CreateAsync(useActualMappingEngine: true);

        var result = await harness.UploadService.PreviewAsync(
            CsvFile(
                "statement.csv",
                "Date,Description,Amount,Reference\n2026-07-01,Coffee,-4.50,CARD-123\n"),
            Request(harness.AccountId) with { ReferenceColumn = 3 },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var stored = await harness.DbContext.StatementImportRows.SingleAsync();
        using var evidence = JsonDocument.Parse(stored.SourceEvidenceJson!);
        Assert.Equal(1, evidence.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("CARD-123", evidence.RootElement.GetProperty("reference").GetString());
    }

    private static StatementImportPreviewRequest Request(Guid accountId) =>
        new(
            accountId,
            "comma",
            DateColumn: 0,
            DescriptionColumn: 1,
            AmountColumn: 2,
            DebitColumn: null,
            CreditColumn: null,
            CurrencyColumn: null,
            ReferenceColumn: null,
            DateFormat: "yyyy-MM-dd",
            DateValueKind: StatementImportDateValueKinds.Date,
            AmountMode: StatementImportAmountModes.Signed,
            AmountSign: StatementImportAmountSigns.AsIs,
            Locale: "en-IE",
            TimeZoneId: "Europe/Dublin");

    private static FormFile CsvFile(string fileName, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private TestHarness(
            AppDbContext dbContext,
            TestCurrentUserProvider currentUserProvider,
            Guid accountId,
            IStatementImportMappingEngine mappingEngine)
        {
            DbContext = dbContext;
            CurrentUserProvider = currentUserProvider;
            AccountId = accountId;
            var batchService = new StatementImportBatchService(
                DbContext,
                CurrentUserProvider,
                new FixedTimeProvider(UtcNow));
            UploadService = new StatementImportUploadService(
                DbContext,
                CurrentUserProvider,
                new StatementCsvParser(),
                mappingEngine,
                batchService);
        }

        public AppDbContext DbContext { get; }
        public TestCurrentUserProvider CurrentUserProvider { get; }
        public Guid AccountId { get; }
        public StatementImportUploadService UploadService { get; }

        public static async Task<TestHarness> CreateAsync(bool useActualMappingEngine = false)
        {
            var dbContext = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"statement-upload-tests-{Guid.NewGuid():N}")
                    .Options);
            var userId = Guid.NewGuid();
            var currentUserProvider = new TestCurrentUserProvider(userId);
            var harness = new TestHarness(
                dbContext,
                currentUserProvider,
                Guid.NewGuid(),
                useActualMappingEngine
                    ? new StatementImportMappingEngine()
                    : new TestMappingEngine());
            await harness.SeedUserAsync("owner@local", userId);
            await harness.SeedAccountAsync(userId, harness.AccountId);
            return harness;
        }

        public async Task<Guid> SeedUserAsync(string email, Guid? id = null)
        {
            var userId = id ?? Guid.NewGuid();
            DbContext.Users.Add(new User
            {
                Id = userId,
                PrimaryEmail = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = "Import Tester",
                Status = "active",
                OnboardingStatus = "complete",
                Role = "user",
                CreatedUtc = UtcNow,
                UpdatedUtc = UtcNow,
                EmailVerified = true,
                Timezone = "Europe/Dublin",
                Locale = "en-IE",
                PreferredCurrency = "EUR",
                PlanTier = "standard"
            });
            await DbContext.SaveChangesAsync();
            return userId;
        }

        public async Task<Guid> SeedAccountAsync(Guid userId, Guid? id = null)
        {
            var accountId = id ?? Guid.NewGuid();
            DbContext.FinancialAccounts.Add(new FinancialAccount
            {
                Id = accountId,
                UserId = userId,
                Name = "Manual account",
                Type = "Current",
                Currency = "EUR",
                Source = FinancialAccountSources.Manual,
                CreatedUtc = UtcNow
            });
            await DbContext.SaveChangesAsync();
            return accountId;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class TestMappingEngine : IStatementImportMappingEngine
    {
        public ServiceError? ValidateDefinition(
            StatementImportMappingDefinition definition,
            IReadOnlyList<StatementCsvColumn> columns) => null;

        public StatementImportMappedRow MapRow(
            StatementCsvDataRow row,
            StatementImportMappingDefinition definition,
            string accountCurrency)
        {
            var date = row.Fields[definition.DateColumn];
            var description = row.Fields[definition.DescriptionColumn].Trim();
            var amount = decimal.Parse(
                row.Fields[definition.AmountColumn!.Value],
                NumberStyles.Number,
                CultureInfo.InvariantCulture);
            var fingerprint = Sha256($"{date}|{description}|{amount}|{accountCurrency}");
            return new StatementImportMappedRow(
                row.RowNumber,
                fingerprint,
                null,
                StatementImportValidationStatuses.Valid,
                null,
                JsonSerializer.Serialize(new
                {
                    date,
                    description,
                    amount = amount.ToString(CultureInfo.InvariantCulture),
                    currency = accountCurrency
                }),
                DateOnly.ParseExact(date, definition.DateFormat, CultureInfo.InvariantCulture),
                null,
                StatementImportTimestampPrecisions.Date,
                description,
                amount,
                accountCurrency);
        }

        public string CreateCanonicalMappingJson(StatementImportMappingDefinition definition) =>
            JsonSerializer.Serialize(new { version = "test-v1", definition.DateColumn });

        private static string Sha256(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
