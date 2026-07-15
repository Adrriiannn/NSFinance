using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Api.Persistence.Migrations;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportBatchServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StageAsync_PersistsBoundedMappedRowsAndReturnsOrderedCounts()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId, FinancialAccountSources.Manual);
        var duplicateCandidateId = await SeedTransactionAsync(dbContext, accountId);
        var service = CreateService(dbContext, userId);
        var command = BuildCommand(
            accountId,
            rows:
            [
                ReadyRow(3, "3"),
                LikelyDuplicateRow(2, "2", duplicateCandidateId),
                RejectedRow(1, "1")
            ]);

        var result = await service.StageAsync(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.WasReplay);
        Assert.Equal("statement.csv", result.Value.FileName);
        Assert.Equal("EUR", result.Value.AccountCurrency);
        Assert.Equal("en-IE", result.Value.Locale);
        Assert.Equal("Europe/Dublin", result.Value.TimeZoneId);
        Assert.Equal(3, result.Value.TotalRowCount);
        Assert.Equal(2, result.Value.ValidRowCount);
        Assert.Equal(1, result.Value.InvalidRowCount);
        Assert.Equal(0, result.Value.ExactDuplicateRowCount);
        Assert.Equal(1, result.Value.LikelyDuplicateRowCount);
        Assert.Equal(1, result.Value.IncludedRowCount);
        Assert.Equal([1, 2, 3], result.Value.Rows.Select(row => row.RowNumber));
        Assert.Equal(StatementImportBatchStatuses.ReadyForReview, result.Value.Status);

        var stored = await dbContext.ImportJobs.Include(item => item.Rows).SingleAsync();
        Assert.Equal(ImportJobKinds.StatementCsv, stored.Kind);
        Assert.Equal(accountId, stored.FinancialAccountId);
        Assert.Equal(3, stored.Rows.Count);
        Assert.All(stored.Rows, row =>
        {
            Assert.NotNull(row.SourceEvidenceJson);
            Assert.DoesNotContain("rawPayload", row.SourceEvidenceJson);
            Assert.Equal(UtcNow.AddHours(24), row.EvidenceExpiresUtc);
        });
    }

    [Fact]
    public async Task StageAsync_ReplayIsIdempotentButDifferentMappingCreatesAnotherBatch()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId, FinancialAccountSources.Manual);
        var service = CreateService(dbContext, userId);
        var command = BuildCommand(accountId);

        var first = await service.StageAsync(command, CancellationToken.None);
        var replay = await service.StageAsync(command, CancellationToken.None);
        var remapped = await service.StageAsync(
            command with { MappingFingerprint = Fingerprint("c") },
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.True(remapped.Succeeded);
        Assert.Equal(first.Value!.Id, replay.Value!.Id);
        Assert.True(replay.Value.WasReplay);
        Assert.NotEqual(first.Value.Id, remapped.Value!.Id);
        Assert.Equal(2, await dbContext.ImportJobs.CountAsync());
        Assert.Equal(2, await dbContext.StatementImportRows.CountAsync());
    }

    [Fact]
    public async Task StageAsync_RejectsForeignAndProviderProjectedAccountsWithoutCreatingBatches()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext, "owner@local");
        var otherId = await SeedUserAsync(dbContext, "other@local");
        var foreignAccountId = await SeedAccountAsync(dbContext, otherId, FinancialAccountSources.Manual);
        var providerAccountId = await SeedAccountAsync(dbContext, ownerId, FinancialAccountSources.ProviderProjected);
        var service = CreateService(dbContext, ownerId);

        var foreign = await service.StageAsync(BuildCommand(foreignAccountId), CancellationToken.None);
        var provider = await service.StageAsync(BuildCommand(providerAccountId), CancellationToken.None);

        Assert.False(foreign.Succeeded);
        Assert.Equal("statement_import_account_not_found", foreign.Error!.Code);
        Assert.False(provider.Succeeded);
        Assert.Equal("statement_import_account_not_manual", provider.Error!.Code);
        Assert.Empty(dbContext.ImportJobs);
    }

    [Fact]
    public async Task GetAsync_DoesNotRevealAnotherUsersBatch()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext, "owner@local");
        var otherId = await SeedUserAsync(dbContext, "other@local");
        var accountId = await SeedAccountAsync(dbContext, ownerId, FinancialAccountSources.Manual);
        var staged = await CreateService(dbContext, ownerId)
            .StageAsync(BuildCommand(accountId), CancellationToken.None);

        var foreignRead = await CreateService(dbContext, otherId)
            .GetAsync(staged.Value!.Id, CancellationToken.None);

        Assert.False(foreignRead.Succeeded);
        Assert.Equal("statement_import_batch_not_found", foreignRead.Error!.Code);
    }

    [Fact]
    public async Task StageAsync_InvalidRowSetFailsBeforeAnyPersistence()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId, FinancialAccountSources.Manual);
        var duplicateRows = BuildCommand(
            accountId,
            rows: [ReadyRow(1, "1"), ReadyRow(1, "2")]);
        var broadEvidence = BuildCommand(accountId) with
        {
            Rows = [ReadyRow(1, "1") with { SourceEvidenceJson = "{\"rawPayload\":\"not allowed\"}" }]
        };

        var duplicateResult = await CreateService(dbContext, userId)
            .StageAsync(duplicateRows, CancellationToken.None);
        var evidenceResult = await CreateService(dbContext, userId)
            .StageAsync(broadEvidence, CancellationToken.None);

        Assert.Equal("statement_import_row_number_invalid", duplicateResult.Error!.Code);
        Assert.Equal("statement_import_row_evidence_invalid", evidenceResult.Error!.Code);
        Assert.Empty(dbContext.ImportJobs);
        Assert.Empty(dbContext.StatementImportRows);
    }

    [Fact]
    public async Task StageAsync_PreservesDateOnlyPrecisionWithoutInventingMidnightUtc()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId, FinancialAccountSources.Manual);

        var result = await CreateService(dbContext, userId)
            .StageAsync(BuildCommand(accountId), CancellationToken.None);

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Rows);
        Assert.Equal(new DateOnly(2026, 7, 1), row.EffectiveDate);
        Assert.Null(row.EffectiveAtUtc);
        Assert.Equal(StatementImportTimestampPrecisions.Date, row.TimestampPrecision);
    }

    [Fact]
    public async Task StageAsync_RejectsCurrencyMismatchAndForeignDuplicateCandidate()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId, FinancialAccountSources.Manual);
        var otherAccountId = await SeedAccountAsync(dbContext, userId, FinancialAccountSources.Manual);
        var foreignCandidateId = await SeedTransactionAsync(dbContext, otherAccountId);
        var service = CreateService(dbContext, userId);

        var currencyMismatch = await service.StageAsync(
            BuildCommand(accountId) with
            {
                Rows = [ReadyRow(1, "1") with { Currency = "USD" }]
            },
            CancellationToken.None);
        var foreignCandidate = await service.StageAsync(
            BuildCommand(accountId) with
            {
                Rows = [LikelyDuplicateRow(1, "2", foreignCandidateId)]
            },
            CancellationToken.None);

        Assert.Equal("statement_import_currency_mismatch", currencyMismatch.Error!.Code);
        Assert.Equal("statement_import_duplicate_candidate_invalid", foreignCandidate.Error!.Code);
        Assert.Empty(dbContext.ImportJobs);
    }

    [Fact]
    public void Model_EnforcesBatchIdempotencyRowIdentityAndSafeDeleteBoundaries()
    {
        using var dbContext = CreateDbContext();
        var importJob = dbContext.Model.FindEntityType(typeof(ImportJob))!;
        var importRow = dbContext.Model.FindEntityType(typeof(StatementImportRow))!;

        Assert.True(importJob.GetIndexes().Single(index =>
            index.GetDatabaseName() == StatementImportIndexNames.ImportJobIdempotency).IsUnique);
        Assert.True(importJob.GetIndexes().Single(index =>
            index.GetDatabaseName() == StatementImportIndexNames.ImportJobCommittedSource).IsUnique);
        Assert.True(importRow.GetIndexes().Single(index =>
            index.GetDatabaseName() == StatementImportIndexNames.BatchRowNumber).IsUnique);
        Assert.True(importRow.GetIndexes().Single(index =>
            index.GetDatabaseName() == StatementImportIndexNames.CommittedTransaction).IsUnique);

        var accountForeignKey = importJob.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(FinancialAccount));
        Assert.Equal(DeleteBehavior.Restrict, accountForeignKey.DeleteBehavior);
        Assert.Equal(["FinancialAccountId", "UserId"], accountForeignKey.Properties.Select(property => property.Name));
        Assert.Equal(["Id", "UserId"], accountForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(
            DeleteBehavior.Cascade,
            importRow.GetForeignKeys().Single(key => key.PrincipalEntityType.ClrType == typeof(ImportJob)).DeleteBehavior);
        Assert.All(
            importRow.GetForeignKeys().Where(key => key.PrincipalEntityType.ClrType == typeof(Transaction)),
            key => Assert.Equal(DeleteBehavior.SetNull, key.DeleteBehavior));
    }

    [Fact]
    public void Migration_IsAdditiveAndPreservesLegacyImportTimestamps()
    {
        var operations = new StatementImportStaging().UpOperations;

        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation);
        Assert.DoesNotContain(operations, operation => operation is DeleteDataOperation);
        var droppedIndex = Assert.Single(operations.OfType<DropIndexOperation>());
        Assert.Equal("IX_ImportJobs_UserId", droppedIndex.Name);
        Assert.Contains(
            operations.OfType<SqlOperation>(),
            operation => operation.Sql.Contains(
                "SET \"UpdatedUtc\" = \"CreatedUtc\"",
                StringComparison.Ordinal));
    }

    private static StageStatementImportBatchCommand BuildCommand(
        Guid accountId,
        IReadOnlyList<StageStatementImportRowCommand>? rows = null) =>
        new(
            accountId,
            @"C:\private\statement.csv",
            1_024,
            Fingerprint("a"),
            Fingerprint("b"),
            "csv-v1",
            "mapping-v1",
            "{\"dateColumn\":0,\"descriptionColumn\":1,\"amountColumn\":2}",
            "en-IE",
            "Europe/Dublin",
            rows ?? [ReadyRow(1, "1")]);

    private static StageStatementImportRowCommand ReadyRow(int rowNumber, string seed) =>
        new(
            rowNumber,
            Fingerprint(seed),
            null,
            StatementImportValidationStatuses.Valid,
            null,
            StatementImportDuplicateClassifications.None,
            StatementImportReviewDispositions.Included,
            null,
            "{\"date\":\"2026-07-01\",\"description\":\"Coffee\",\"amount\":\"-4.50\",\"currency\":\"EUR\"}",
            new DateOnly(2026, 7, 1),
            null,
            StatementImportTimestampPrecisions.Date,
            "Coffee",
            -4.50m,
            "eur");

    private static StageStatementImportRowCommand LikelyDuplicateRow(
        int rowNumber,
        string seed,
        Guid candidateTransactionId) =>
        ReadyRow(rowNumber, seed) with
        {
            DuplicateClassification = StatementImportDuplicateClassifications.Likely,
            ReviewDisposition = StatementImportReviewDispositions.Pending,
            DuplicateCandidateTransactionId = candidateTransactionId
        };

    private static StageStatementImportRowCommand RejectedRow(int rowNumber, string seed) =>
        new(
            rowNumber,
            Fingerprint(seed),
            null,
            StatementImportValidationStatuses.Invalid,
            "amount_invalid",
            StatementImportDuplicateClassifications.None,
            StatementImportReviewDispositions.Excluded,
            null,
            "{\"date\":\"2026-07-01\",\"description\":\"Coffee\",\"amount\":\"invalid\"}",
            null,
            null,
            null,
            null,
            null,
            null);

    private static string Fingerprint(string seed) => new(seed[0], 64);

    private static StatementImportBatchService CreateService(AppDbContext dbContext, Guid userId) =>
        new(dbContext, new TestCurrentUserProvider(userId), new FixedTimeProvider(UtcNow));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"statement-import-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, string email)
    {
        var userId = Guid.NewGuid();
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Statement Import Tester",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = UtcNow,
            UpdatedUtc = UtcNow,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-IE",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });
        await dbContext.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> SeedAccountAsync(AppDbContext dbContext, Guid userId, string source)
    {
        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Statement account",
            Type = "Current",
            Currency = "EUR",
            Source = source,
            CreatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();
        return accountId;
    }

    private static async Task<Guid> SeedTransactionAsync(AppDbContext dbContext, Guid accountId)
    {
        var transactionId = Guid.NewGuid();
        dbContext.Transactions.Add(new Transaction
        {
            Id = transactionId,
            FinancialAccountId = accountId,
            Amount = -4.50m,
            Currency = "EUR",
            Description = "Coffee",
            BookedAtUtc = UtcNow.AddDays(-1),
            CreatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();
        return transactionId;
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
