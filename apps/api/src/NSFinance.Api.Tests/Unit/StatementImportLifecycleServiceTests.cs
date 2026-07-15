using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportLifecycleServiceTests
{
    private static readonly DateTime UtcNow =
        new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CommitAsync_CreatesIncludedTransactionsAndReplaysWithoutDuplicates()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var instant = new DateTime(2026, 7, 2, 10, 30, 0, DateTimeKind.Utc);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [
                DateRow(1, "a", "Coffee", -4.50m),
                InstantRow(2, "b", "Salary", 1_000m, instant),
                DateRow(
                    3,
                    "c",
                    "Excluded item",
                    -20m,
                    StatementImportReviewDispositions.Excluded)
            ]);
        var service = CreateLifecycleService(dbContext, userId);

        var result = await service.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.WasReplay);
        Assert.Equal(StatementImportBatchStatuses.Committed, result.Value.Status);
        Assert.Equal(2, result.Value.Revision);
        Assert.Equal(2, result.Value.CommittedRowCount);
        var transactions = await dbContext.Transactions
            .Where(transaction => transaction.EntryKind == TransactionEntryKinds.StatementImport)
            .OrderBy(transaction => transaction.Description)
            .ToListAsync();
        Assert.Equal(2, transactions.Count);
        var coffee = Assert.Single(transactions, transaction => transaction.Description == "Coffee");
        Assert.Equal(-4.50m, coffee.Amount);
        Assert.Equal("EUR", coffee.Currency);
        Assert.Equal(accountId, coffee.FinancialAccountId);
        Assert.Equal(TransactionAnalyticsTreatments.Ordinary, coffee.AnalyticsTreatment);
        Assert.Equal(new DateTime(2026, 6, 30, 23, 0, 0, DateTimeKind.Utc), coffee.BookedAtUtc);
        Assert.Null(coffee.CategoryId);
        Assert.Equal(DeterministicClassificationStatus.NotEvaluated, coffee.DeterministicClassificationStatus);
        var salary = Assert.Single(transactions, transaction => transaction.Description == "Salary");
        Assert.Equal(instant, salary.BookedAtUtc);
        var rows = await dbContext.StatementImportRows
            .Where(row => row.ImportJobId == batch.Id)
            .OrderBy(row => row.RowNumber)
            .ToListAsync();
        Assert.True(rows[0].CommittedTransactionId.HasValue);
        Assert.True(rows[1].CommittedTransactionId.HasValue);
        Assert.Null(rows[2].CommittedTransactionId);
        var audit = Assert.Single(dbContext.AuditEvents);
        Assert.Equal("statement_import_committed", audit.EventName);
        Assert.DoesNotContain("Coffee", audit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Salary", audit.MetadataJson, StringComparison.Ordinal);

        var replay = await service.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);

        Assert.True(replay.Succeeded);
        Assert.True(replay.Value!.WasReplay);
        Assert.Equal(2, replay.Value.Revision);
        Assert.Equal(2, await dbContext.Transactions.CountAsync(
            transaction => transaction.EntryKind == TransactionEntryKinds.StatementImport));
        Assert.Single(dbContext.AuditEvents);
    }

    [Fact]
    public async Task CommitAsync_EnforcesOwnershipRevisionAndZeroValueGuardWithoutMutation()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext, "owner@local");
        var otherId = await SeedUserAsync(dbContext, "other@local");
        var accountId = await SeedAccountAsync(dbContext, ownerId);
        var batch = await StageAsync(
            dbContext,
            ownerId,
            accountId,
            [DateRow(1, "a", "Invalid zero", -4.50m)]);
        var row = await dbContext.StatementImportRows.SingleAsync(
            item => item.ImportJobId == batch.Id);
        row.Amount = 0m;
        await dbContext.SaveChangesAsync();

        var foreign = await CreateLifecycleService(dbContext, otherId).CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var missingRevision = await CreateLifecycleService(dbContext, ownerId).CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(null),
            CancellationToken.None);
        var stale = await CreateLifecycleService(dbContext, ownerId).CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision + 1),
            CancellationToken.None);
        var invalid = await CreateLifecycleService(dbContext, ownerId).CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);

        Assert.Equal("statement_import_batch_not_found", foreign.Error!.Code);
        Assert.Equal("statement_import_revision_required", missingRevision.Error!.Code);
        Assert.Equal("statement_import_revision_conflict", stale.Error!.Code);
        Assert.Equal("statement_import_zero_amount", invalid.Error!.Code);
        Assert.Empty(dbContext.Transactions);
        Assert.Empty(dbContext.AuditEvents);
        var stored = await dbContext.ImportJobs.SingleAsync(item => item.Id == batch.Id);
        Assert.Equal(StatementImportBatchStatuses.ReadyForReview, stored.Status);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task CommitAsync_RejectsPendingReviewAndUnsafeInstantWithoutPartialWrites()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var candidateId = await SeedTransactionAsync(dbContext, accountId, "Existing", -12m);
        var pending = await StageAsync(
            dbContext,
            userId,
            accountId,
            [LikelyDuplicateRow(1, "a", candidateId)],
            sourceSeed: "p");
        var unsafeInstant = await StageAsync(
            dbContext,
            userId,
            accountId,
            [InstantRow(
                1,
                "b",
                "Unspecified instant",
                -8m,
                new DateTime(2026, 7, 2, 10, 30, 0, DateTimeKind.Utc))],
            sourceSeed: "u");
        var excluded = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(
                1,
                "c",
                "Excluded",
                -3m,
                StatementImportReviewDispositions.Excluded)],
            sourceSeed: "e");
        var unsafeRow = await dbContext.StatementImportRows.SingleAsync(
            row => row.ImportJobId == unsafeInstant.Id);
        unsafeRow.EffectiveAtUtc = DateTime.SpecifyKind(
            unsafeRow.EffectiveAtUtc!.Value,
            DateTimeKind.Unspecified);
        await dbContext.SaveChangesAsync();
        var service = CreateLifecycleService(dbContext, userId);

        var pendingResult = await service.CommitAsync(
            pending.Id,
            new StatementImportRevisionCommand(pending.Revision),
            CancellationToken.None);
        var unsafeResult = await service.CommitAsync(
            unsafeInstant.Id,
            new StatementImportRevisionCommand(unsafeInstant.Revision),
            CancellationToken.None);
        var excludedResult = await service.CommitAsync(
            excluded.Id,
            new StatementImportRevisionCommand(excluded.Revision),
            CancellationToken.None);

        Assert.Equal("statement_import_review_incomplete", pendingResult.Error!.Code);
        Assert.Equal("statement_import_row_date_unrepresentable", unsafeResult.Error!.Code);
        Assert.Equal("statement_import_no_rows_included", excludedResult.Error!.Code);
        Assert.Equal(1, await dbContext.Transactions.CountAsync());
        Assert.Empty(dbContext.AuditEvents);
    }

    [Fact]
    public async Task UndoAsync_RemovesOnlyImportedTransactionsAndSupportsReviewThenRedo()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var otherId = await SeedUserAsync(dbContext, "other@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var unrelatedId = await SeedTransactionAsync(dbContext, accountId, "Unrelated", -2m);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [
                DateRow(1, "a", "First import", -4m),
                DateRow(2, "b", "Second import", -6m)
            ]);
        var lifecycle = CreateLifecycleService(dbContext, userId);
        var committed = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var firstImportedIds = await dbContext.Transactions
            .Where(transaction => transaction.EntryKind == TransactionEntryKinds.StatementImport)
            .Select(transaction => transaction.Id)
            .ToListAsync();
        var foreignUndo = await CreateLifecycleService(dbContext, otherId).UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value!.Revision),
            CancellationToken.None);

        var undone = await lifecycle.UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value!.Revision),
            CancellationToken.None);
        var undoReplay = await lifecycle.UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value.Revision),
            CancellationToken.None);

        Assert.Equal("statement_import_batch_not_found", foreignUndo.Error!.Code);
        Assert.True(undone.Succeeded);
        Assert.Equal(StatementImportBatchStatuses.Undone, undone.Value!.Status);
        Assert.Equal(3, undone.Value.Revision);
        Assert.True(undoReplay.Succeeded);
        Assert.True(undoReplay.Value!.WasReplay);
        Assert.Equal([unrelatedId], await dbContext.Transactions.Select(item => item.Id).ToListAsync());
        Assert.All(
            await dbContext.StatementImportRows.Where(row => row.ImportJobId == batch.Id).ToListAsync(),
            row => Assert.Null(row.CommittedTransactionId));

        var firstRow = await dbContext.StatementImportRows.SingleAsync(
            row => row.ImportJobId == batch.Id && row.RowNumber == 1);
        var reviewed = await CreateReviewService(dbContext, userId).ReviewRowsAsync(
            batch.Id,
            new ReviewStatementImportRowsCommand(
                undone.Value.Revision,
                [new StatementImportRowReviewDecision(
                    firstRow.Id,
                    StatementImportReviewDispositions.Excluded)]),
            CancellationToken.None);
        var redone = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(reviewed.Value!.Revision),
            CancellationToken.None);

        Assert.True(reviewed.Succeeded);
        Assert.True(redone.Succeeded);
        Assert.Equal(StatementImportBatchStatuses.Committed, redone.Value!.Status);
        Assert.Equal(1, redone.Value.CommittedRowCount);
        var redoneTransaction = Assert.Single(await dbContext.Transactions
            .Where(transaction => transaction.EntryKind == TransactionEntryKinds.StatementImport)
            .ToListAsync());
        Assert.Equal("Second import", redoneTransaction.Description);
        Assert.DoesNotContain(redoneTransaction.Id, firstImportedIds);
        Assert.True(await dbContext.Transactions.AnyAsync(item => item.Id == unrelatedId));
        var auditNames = await dbContext.AuditEvents
            .Select(item => item.EventName)
            .ToListAsync();
        Assert.Equal(2, auditNames.Count(name => name == "statement_import_committed"));
        Assert.Single(auditNames, name => name == "statement_import_undone");
        Assert.Single(auditNames, name => name == "statement_import_rows_reviewed");
    }

    [Fact]
    public async Task CommitAsync_ExpiresReviewWindowOnceAndExpiredBatchCanBeDiscarded()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "a", "Expired", -5m)]);
        batch.ExpiresUtc = UtcNow;
        await dbContext.SaveChangesAsync();
        var lifecycle = CreateLifecycleService(dbContext, userId);

        var expired = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);

        Assert.Equal("statement_import_batch_expired", expired.Error!.Code);
        Assert.Equal(StatementImportBatchStatuses.Expired, batch.Status);
        Assert.Equal(2, batch.Revision);
        Assert.Empty(dbContext.Transactions);
        Assert.Equal("statement_import_expired", Assert.Single(dbContext.AuditEvents).EventName);

        var discarded = await lifecycle.DiscardAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);

        Assert.True(discarded.Succeeded);
        Assert.Equal(StatementImportBatchStatuses.Discarded, discarded.Value!.Status);
        Assert.Equal(3, discarded.Value.Revision);
        Assert.Null((await dbContext.StatementImportRows.SingleAsync()).SourceEvidenceJson);
        Assert.Equal(2, await dbContext.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task DiscardAsync_ClearsEvidenceAndReplaysButCannotBeCommitted()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "a", "Discard me", -9m)]);
        var lifecycle = CreateLifecycleService(dbContext, userId);

        var discarded = await lifecycle.DiscardAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var replay = await lifecycle.DiscardAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var commit = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(discarded.Value!.Revision),
            CancellationToken.None);

        Assert.True(discarded.Succeeded);
        Assert.Equal(StatementImportBatchStatuses.Discarded, discarded.Value.Status);
        Assert.Equal(2, discarded.Value.Revision);
        Assert.True(replay.Succeeded);
        Assert.True(replay.Value!.WasReplay);
        Assert.Equal("statement_import_batch_not_committable", commit.Error!.Code);
        var row = await dbContext.StatementImportRows.SingleAsync(
            item => item.ImportJobId == batch.Id);
        Assert.Null(row.SourceEvidenceJson);
        Assert.Null(row.EvidenceExpiresUtc);
        Assert.Empty(dbContext.Transactions);
        Assert.Equal("statement_import_discarded", Assert.Single(dbContext.AuditEvents).EventName);

        row.SourceEvidenceJson = "{\"version\":1}";
        await dbContext.SaveChangesAsync();
        var tamperedReplay = await lifecycle.DiscardAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        Assert.Equal("statement_import_state_invalid", tamperedReplay.Error!.Code);
        Assert.Single(dbContext.AuditEvents);
    }

    [Fact]
    public async Task CommitAsync_AllowsOnlyOneCommittedBatchForAnAccountAndSource()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var first = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "a", "First mapping", -4m)],
            sourceSeed: "s",
            mappingSeed: "a");
        var second = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "b", "Second mapping", -4m)],
            sourceSeed: "s",
            mappingSeed: "b");
        var lifecycle = CreateLifecycleService(dbContext, userId);

        var firstResult = await lifecycle.CommitAsync(
            first.Id,
            new StatementImportRevisionCommand(first.Revision),
            CancellationToken.None);
        var secondResult = await lifecycle.CommitAsync(
            second.Id,
            new StatementImportRevisionCommand(second.Revision),
            CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.Equal("statement_import_source_already_committed", secondResult.Error!.Code);
        Assert.Single(await dbContext.Transactions
            .Where(transaction => transaction.EntryKind == TransactionEntryKinds.StatementImport)
            .ToListAsync());
        Assert.Equal(StatementImportBatchStatuses.ReadyForReview, second.Status);
    }

    [Fact]
    public async Task UndoAsync_RejectsUserEnrichedImportedTransactionsWithoutDeletingData()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "a", "User enriched", -4m)]);
        var lifecycle = CreateLifecycleService(dbContext, userId);
        var committed = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var transaction = await dbContext.Transactions.SingleAsync();
        transaction.Notes = "Reviewed by the user";
        await dbContext.SaveChangesAsync();

        var replay = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var undo = await lifecycle.UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value!.Revision),
            CancellationToken.None);

        Assert.True(replay.Succeeded);
        Assert.True(replay.Value!.WasReplay);
        Assert.Equal("statement_import_undo_transactions_changed", undo.Error!.Code);
        Assert.Equal("Reviewed by the user", (await dbContext.Transactions.SingleAsync()).Notes);
        Assert.Equal(StatementImportBatchStatuses.Committed, batch.Status);
        Assert.Single(dbContext.AuditEvents);
    }

    [Fact]
    public async Task UndoAsync_BlocksActiveDependentReviewButSucceedsAfterItIsDiscarded()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "a", "Original import", -4m)],
            sourceSeed: "original");
        var lifecycle = CreateLifecycleService(dbContext, userId);
        var committed = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var importedId = (await dbContext.Transactions.SingleAsync()).Id;
        var dependent = await StageAsync(
            dbContext,
            userId,
            accountId,
            [LikelyDuplicateRow(1, "b", importedId)],
            sourceSeed: "dependent");

        var blocked = await lifecycle.UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value!.Revision),
            CancellationToken.None);

        Assert.Equal("statement_import_undo_has_downstream_references", blocked.Error!.Code);
        Assert.True(await dbContext.Transactions.AnyAsync(item => item.Id == importedId));
        Assert.Equal(StatementImportBatchStatuses.Committed, batch.Status);

        var discarded = await lifecycle.DiscardAsync(
            dependent.Id,
            new StatementImportRevisionCommand(dependent.Revision),
            CancellationToken.None);
        var undone = await lifecycle.UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value.Revision),
            CancellationToken.None);

        Assert.True(discarded.Succeeded);
        Assert.True(undone.Succeeded);
        Assert.False(await dbContext.Transactions.AnyAsync(item => item.Id == importedId));
        var dependentRow = await dbContext.StatementImportRows.SingleAsync(
            row => row.ImportJobId == dependent.Id);
        Assert.Null(dependentRow.DuplicateCandidateTransactionId);
        Assert.Equal(3, await dbContext.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task ReplayAndUndo_RejectTamperedImmutableImportedFactsWithoutDeletingData()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [DateRow(1, "a", "Original", -4m)]);
        var lifecycle = CreateLifecycleService(dbContext, userId);
        var committed = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var transaction = await dbContext.Transactions.SingleAsync();
        transaction.BookedAtUtc = transaction.BookedAtUtc.AddMinutes(1);
        await dbContext.SaveChangesAsync();

        var replay = await lifecycle.CommitAsync(
            batch.Id,
            new StatementImportRevisionCommand(batch.Revision),
            CancellationToken.None);
        var undo = await lifecycle.UndoAsync(
            batch.Id,
            new StatementImportRevisionCommand(committed.Value!.Revision),
            CancellationToken.None);

        Assert.Equal("statement_import_state_invalid", replay.Error!.Code);
        Assert.Equal("statement_import_state_invalid", undo.Error!.Code);
        Assert.Equal(
            new DateTime(2026, 6, 30, 23, 1, 0, DateTimeKind.Utc),
            (await dbContext.Transactions.SingleAsync()).BookedAtUtc);
        Assert.Equal(StatementImportBatchStatuses.Committed, batch.Status);
        Assert.Single(dbContext.AuditEvents);
    }

    private static async Task<ImportJob> StageAsync(
        AppDbContext dbContext,
        Guid userId,
        Guid accountId,
        IReadOnlyList<StageStatementImportRowCommand> rows,
        string sourceSeed = "s",
        string mappingSeed = "m")
    {
        var result = await new StatementImportBatchService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new FixedTimeProvider(UtcNow)).StageAsync(
            new StageStatementImportBatchCommand(
                accountId,
                "statement.csv",
                1_024,
                Fingerprint(sourceSeed),
                Fingerprint(mappingSeed),
                "csv-v1",
                "mapping-v1",
                "{\"dateColumn\":0,\"descriptionColumn\":1,\"amountColumn\":2}",
                "en-IE",
                "Europe/Dublin",
                rows),
            CancellationToken.None);
        Assert.True(result.Succeeded);
        return await dbContext.ImportJobs.SingleAsync(item => item.Id == result.Value!.Id);
    }

    private static StageStatementImportRowCommand DateRow(
        int rowNumber,
        string seed,
        string description,
        decimal amount,
        string disposition = StatementImportReviewDispositions.Included) =>
        new(
            rowNumber,
            Fingerprint(seed),
            null,
            StatementImportValidationStatuses.Valid,
            null,
            StatementImportDuplicateClassifications.None,
            disposition,
            null,
            $"{{\"version\":1,\"description\":\"{description}\"}}",
            new DateOnly(2026, 7, 1),
            null,
            StatementImportTimestampPrecisions.Date,
            description,
            amount,
            "EUR");

    private static StageStatementImportRowCommand InstantRow(
        int rowNumber,
        string seed,
        string description,
        decimal amount,
        DateTime instant) =>
        DateRow(rowNumber, seed, description, amount) with
        {
            EffectiveDate = null,
            EffectiveAtUtc = instant,
            TimestampPrecision = StatementImportTimestampPrecisions.Instant
        };

    private static StageStatementImportRowCommand LikelyDuplicateRow(
        int rowNumber,
        string seed,
        Guid candidateTransactionId) =>
        DateRow(rowNumber, seed, "Likely duplicate", -12m) with
        {
            DuplicateClassification = StatementImportDuplicateClassifications.Likely,
            ReviewDisposition = StatementImportReviewDispositions.Pending,
            DuplicateCandidateTransactionId = candidateTransactionId
        };

    private static StatementImportLifecycleService CreateLifecycleService(
        AppDbContext dbContext,
        Guid userId) =>
        new(
            dbContext,
            new TestCurrentUserProvider(userId),
            new FixedTimeProvider(UtcNow),
            new TestRequestContextAccessor());

    private static StatementImportReviewService CreateReviewService(
        AppDbContext dbContext,
        Guid userId) =>
        new(
            dbContext,
            new TestCurrentUserProvider(userId),
            new FixedTimeProvider(UtcNow),
            new TestRequestContextAccessor());

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"statement-import-lifecycle-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, string email)
    {
        var id = Guid.NewGuid();
        dbContext.Users.Add(new User
        {
            Id = id,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Statement import lifecycle tester",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = UtcNow,
            UpdatedUtc = UtcNow,
            EmailVerified = true,
            Timezone = "Europe/Dublin",
            Locale = "en-IE",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });
        await dbContext.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedAccountAsync(AppDbContext dbContext, Guid userId)
    {
        var id = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = id,
            UserId = userId,
            Name = "Statement account",
            Type = "Current",
            Currency = "EUR",
            Source = FinancialAccountSources.Manual,
            CreatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTransactionAsync(
        AppDbContext dbContext,
        Guid accountId,
        string description,
        decimal amount)
    {
        var id = Guid.NewGuid();
        dbContext.Transactions.Add(new Transaction
        {
            Id = id,
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = "EUR",
            Description = description,
            BookedAtUtc = UtcNow.AddDays(-1),
            CreatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();
        return id;
    }

    private static string Fingerprint(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

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

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "statement-import-lifecycle-test";
        public string SourceChannel => "test";
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string? Platform => "test";
        public string? AppVersion => "test";
    }
}
