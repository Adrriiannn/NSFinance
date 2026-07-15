using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportReviewServiceTests
{
    private static readonly DateTime UtcNow =
        new(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReviewRowsAsync_UpdatesEligibleRowsCountsRevisionAndAuditAtomically()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var duplicateCandidateId = await SeedTransactionAsync(dbContext, accountId);
        var batch = await StageAsync(
            dbContext,
            userId,
            accountId,
            [
                ReadyRow(1, "a"),
                LikelyDuplicateRow(2, "b", duplicateCandidateId),
                InvalidRow(3, "c")
            ]);
        var rows = await dbContext.StatementImportRows
            .OrderBy(row => row.RowNumber)
            .ToListAsync();

        var result = await CreateReviewService(dbContext, userId).ReviewRowsAsync(
            batch.Id,
            new ReviewStatementImportRowsCommand(
                batch.Revision,
                [
                    new StatementImportRowReviewDecision(
                        rows[0].Id,
                        StatementImportReviewDispositions.Excluded),
                    new StatementImportRowReviewDecision(
                        rows[1].Id,
                        StatementImportReviewDispositions.Included)
                ]),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Revision);
        Assert.Equal(1, result.Value.IncludedRowCount);
        Assert.Equal(0, result.Value.PendingRowCount);
        Assert.Equal(2, result.Value.ExcludedRowCount);
        Assert.Equal([1, 2], result.Value.ReviewedRows.Select(row => row.RowNumber));
        Assert.Equal(
            [StatementImportReviewDispositions.Excluded, StatementImportReviewDispositions.Included],
            result.Value.ReviewedRows.Select(row => row.ReviewDisposition));

        var storedBatch = await dbContext.ImportJobs.SingleAsync(item => item.Id == batch.Id);
        Assert.Equal(2, storedBatch.Revision);
        Assert.Equal(1, storedBatch.IncludedRowCount);
        var audit = Assert.Single(dbContext.AuditEvents);
        Assert.Equal("statement_import_rows_reviewed", audit.EventName);
        Assert.DoesNotContain("Coffee", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4.50", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewRowsAsync_EnforcesOwnershipRevisionAndLockedRowsWithoutPartialMutation()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext, "owner@local");
        var otherId = await SeedUserAsync(dbContext, "other@local");
        var accountId = await SeedAccountAsync(dbContext, ownerId);
        var batch = await StageAsync(
            dbContext,
            ownerId,
            accountId,
            [ReadyRow(1, "a"), InvalidRow(2, "b")]);
        var rows = await dbContext.StatementImportRows
            .OrderBy(row => row.RowNumber)
            .ToListAsync();
        var command = new ReviewStatementImportRowsCommand(
            batch.Revision,
            [new StatementImportRowReviewDecision(
                rows[0].Id,
                StatementImportReviewDispositions.Excluded)]);

        var foreign = await CreateReviewService(dbContext, otherId).ReviewRowsAsync(
            batch.Id,
            command,
            CancellationToken.None);
        var stale = await CreateReviewService(dbContext, ownerId).ReviewRowsAsync(
            batch.Id,
            command with { ExpectedRevision = batch.Revision + 1 },
            CancellationToken.None);
        var locked = await CreateReviewService(dbContext, ownerId).ReviewRowsAsync(
            batch.Id,
            command with
            {
                Decisions =
                [
                    command.Decisions![0],
                    new StatementImportRowReviewDecision(
                        rows[1].Id,
                        StatementImportReviewDispositions.Excluded)
                ]
            },
            CancellationToken.None);

        Assert.Equal("statement_import_batch_not_found", foreign.Error!.Code);
        Assert.Equal("statement_import_revision_conflict", stale.Error!.Code);
        Assert.Equal("statement_import_row_not_reviewable", locked.Error!.Code);
        Assert.Equal(1, (await dbContext.ImportJobs.SingleAsync(item => item.Id == batch.Id)).Revision);
        Assert.Equal(
            StatementImportReviewDispositions.Included,
            (await dbContext.StatementImportRows.SingleAsync(row => row.Id == rows[0].Id)).ReviewDisposition);
        Assert.Empty(dbContext.AuditEvents);
    }

    [Fact]
    public async Task ReviewRowsAsync_RejectsMalformedDecisionsPendingOrdinaryRowsAndCrossBatchRows()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var first = await StageAsync(dbContext, userId, accountId, [ReadyRow(1, "a")]);
        var second = await StageAsync(
            dbContext,
            userId,
            accountId,
            [ReadyRow(1, "b")],
            sourceSeed: "d");
        var firstRow = await dbContext.StatementImportRows.SingleAsync(row => row.ImportJobId == first.Id);
        var secondRow = await dbContext.StatementImportRows.SingleAsync(row => row.ImportJobId == second.Id);
        var service = CreateReviewService(dbContext, userId);

        var empty = await service.ReviewRowsAsync(
            first.Id,
            new ReviewStatementImportRowsCommand(first.Revision, []),
            CancellationToken.None);
        var repeated = await service.ReviewRowsAsync(
            first.Id,
            new ReviewStatementImportRowsCommand(
                first.Revision,
                [
                    new StatementImportRowReviewDecision(
                        firstRow.Id,
                        StatementImportReviewDispositions.Excluded),
                    new StatementImportRowReviewDecision(
                        firstRow.Id,
                        StatementImportReviewDispositions.Included)
                ]),
            CancellationToken.None);
        var pending = await service.ReviewRowsAsync(
            first.Id,
            new ReviewStatementImportRowsCommand(
                first.Revision,
                [new StatementImportRowReviewDecision(
                    firstRow.Id,
                    StatementImportReviewDispositions.Pending)]),
            CancellationToken.None);
        var crossBatch = await service.ReviewRowsAsync(
            first.Id,
            new ReviewStatementImportRowsCommand(
                first.Revision,
                [new StatementImportRowReviewDecision(
                    secondRow.Id,
                    StatementImportReviewDispositions.Excluded)]),
            CancellationToken.None);

        Assert.Equal("statement_import_review_decisions_required", empty.Error!.Code);
        Assert.Equal("statement_import_review_row_repeated", repeated.Error!.Code);
        Assert.Equal("statement_import_pending_requires_likely_duplicate", pending.Error!.Code);
        Assert.Equal("statement_import_row_not_found", crossBatch.Error!.Code);
        Assert.Equal(1, (await dbContext.ImportJobs.SingleAsync(item => item.Id == first.Id)).Revision);
    }

    [Fact]
    public async Task ReviewRowsAsync_NoOpKeepsRevisionAndExpiredBatchTransitionsOnce()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var active = await StageAsync(dbContext, userId, accountId, [ReadyRow(1, "a")]);
        var expired = await StageAsync(
            dbContext,
            userId,
            accountId,
            [ReadyRow(1, "b")],
            sourceSeed: "d");
        expired.ExpiresUtc = UtcNow;
        await dbContext.SaveChangesAsync();
        var activeRow = await dbContext.StatementImportRows.SingleAsync(row => row.ImportJobId == active.Id);
        var expiredRow = await dbContext.StatementImportRows.SingleAsync(row => row.ImportJobId == expired.Id);
        var service = CreateReviewService(dbContext, userId);

        var noOp = await service.ReviewRowsAsync(
            active.Id,
            new ReviewStatementImportRowsCommand(
                active.Revision,
                [new StatementImportRowReviewDecision(
                    activeRow.Id,
                    StatementImportReviewDispositions.Included)]),
            CancellationToken.None);
        var expiration = await service.ReviewRowsAsync(
            expired.Id,
            new ReviewStatementImportRowsCommand(
                expired.Revision,
                [new StatementImportRowReviewDecision(
                    expiredRow.Id,
                    StatementImportReviewDispositions.Excluded)]),
            CancellationToken.None);

        Assert.True(noOp.Succeeded);
        Assert.Equal(1, noOp.Value!.Revision);
        Assert.Equal("statement_import_batch_expired", expiration.Error!.Code);
        var storedExpired = await dbContext.ImportJobs.SingleAsync(item => item.Id == expired.Id);
        Assert.Equal(StatementImportBatchStatuses.Expired, storedExpired.Status);
        Assert.Equal(2, storedExpired.Revision);
        var audit = Assert.Single(dbContext.AuditEvents);
        Assert.Equal("statement_import_expired", audit.EventName);
    }

    [Fact]
    public async Task ReviewRowsAsync_InconsistentCountsFailBeforeRowsBecomeDirty()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "owner@local");
        var accountId = await SeedAccountAsync(dbContext, userId);
        var batch = await StageAsync(dbContext, userId, accountId, [ReadyRow(1, "a")]);
        batch.IncludedRowCount = 3;
        await dbContext.SaveChangesAsync();
        var row = await dbContext.StatementImportRows.SingleAsync(item => item.ImportJobId == batch.Id);

        var result = await CreateReviewService(dbContext, userId).ReviewRowsAsync(
            batch.Id,
            new ReviewStatementImportRowsCommand(
                batch.Revision,
                [new StatementImportRowReviewDecision(
                    row.Id,
                    StatementImportReviewDispositions.Excluded)]),
            CancellationToken.None);

        Assert.Equal("statement_import_review_state_invalid", result.Error!.Code);
        Assert.Equal(StatementImportReviewDispositions.Included, row.ReviewDisposition);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(row).State);
        Assert.Empty(dbContext.AuditEvents);
    }

    private static async Task<ImportJob> StageAsync(
        AppDbContext dbContext,
        Guid userId,
        Guid accountId,
        IReadOnlyList<StageStatementImportRowCommand> rows,
        string sourceSeed = "c")
    {
        var service = new StatementImportBatchService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new FixedTimeProvider(UtcNow));
        var result = await service.StageAsync(
            new StageStatementImportBatchCommand(
                accountId,
                "statement.csv",
                1_024,
                Fingerprint(sourceSeed),
                Fingerprint("e"),
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
            "{\"version\":1,\"date\":\"2026-07-01\",\"description\":\"Coffee\",\"amount\":\"-4.50\",\"currency\":\"EUR\"}",
            new DateOnly(2026, 7, 1),
            null,
            StatementImportTimestampPrecisions.Date,
            "Coffee",
            -4.50m,
            "EUR");

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

    private static StageStatementImportRowCommand InvalidRow(int rowNumber, string seed) =>
        ReadyRow(rowNumber, seed) with
        {
            ValidationStatus = StatementImportValidationStatuses.Invalid,
            ValidationCode = "amount_invalid",
            ReviewDisposition = StatementImportReviewDispositions.Excluded,
            EffectiveDate = null,
            TimestampPrecision = null,
            Description = null,
            Amount = null,
            Currency = null
        };

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
            .UseInMemoryDatabase($"statement-import-review-tests-{Guid.NewGuid():N}")
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
            DisplayName = "Statement Import Reviewer",
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

    private static async Task<Guid> SeedTransactionAsync(AppDbContext dbContext, Guid accountId)
    {
        var id = Guid.NewGuid();
        dbContext.Transactions.Add(new Transaction
        {
            Id = id,
            FinancialAccountId = accountId,
            Amount = -4.50m,
            Currency = "EUR",
            Description = "Coffee",
            BookedAtUtc = UtcNow.AddDays(-1),
            CreatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();
        return id;
    }

    private static string Fingerprint(string seed) => new(seed[0], 64);

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
        public string CorrelationId => "statement-import-review-test";
        public string SourceChannel => "test";
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string? Platform => "test";
        public string? AppVersion => "test";
    }
}
