using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Api.Persistence.Migrations;

namespace NSFinance.Api.Tests.Unit;

public sealed class ManualAccountProvenanceTests
{
    [Fact]
    public void Migration_UsesAdditiveColumnsAndNarrowLegacyOpeningBalanceBackfill()
    {
        var migration = new ManualAccountProvenance();
        var addedColumns = migration.UpOperations.OfType<AddColumnOperation>().ToList();

        Assert.Contains(addedColumns, operation => operation.Table == "FinancialAccounts" && operation.Name == "Source");
        Assert.Contains(addedColumns, operation => operation.Table == "Transactions" && operation.Name == "EntryKind");
        Assert.Contains(addedColumns, operation => operation.Table == "Transactions" && operation.Name == "AnalyticsTreatment");

        var backfillSql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("linked.\"FinancialAccountId\" = account.\"Id\"", backfillSql, StringComparison.Ordinal);
        Assert.Contains("transaction.\"CreatedUtc\" = account.\"CreatedUtc\"", backfillSql, StringComparison.Ordinal);
        Assert.Contains("transaction.\"BookedAtUtc\" = account.\"CreatedUtc\"", backfillSql, StringComparison.Ordinal);
        Assert.Contains("transaction.\"Description\" = 'Opening balance'", backfillSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAccountAsync_OpeningBalance_IsBalanceOnlyAndCannotBecomeIncomeOrCategory()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var currentUser = new TestCurrentUserProvider(userId);
        var balanceService = new AccountBalanceReadService(dbContext, currentUser, TimeProvider.System);
        var accountService = new AccountService(
            dbContext,
            currentUser,
            balanceService,
            NullLogger<AccountService>.Instance);

        var account = await accountService.CreateAccountAsync(
            new CreateAccountRequest("Cash wallet", "Cash", "EUR", 1_250m),
            CancellationToken.None);

        Assert.Equal(FinancialAccountSources.Manual, account.Source);
        Assert.Equal(1_250m, account.CurrentBalance);
        Assert.Equal(1_250m, account.Balance!.Current);
        Assert.Equal("manual_ledger", account.Balance.Source);

        var openingBalance = await dbContext.Transactions.SingleAsync();
        Assert.Equal(TransactionEntryKinds.OpeningBalanceAdjustment, openingBalance.EntryKind);
        Assert.Equal(TransactionAnalyticsTreatments.BalanceOnly, openingBalance.AnalyticsTreatment);
        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, openingBalance.DeterministicClassificationStatus);
        Assert.True(openingBalance.DeterministicClassificationTerminal);
        Assert.Equal("balance_only_entry", openingBalance.DeterministicReasonCode);

        var transactionService = new TransactionService(dbContext, currentUser, new ExpenseTaxonomyService());
        var detail = await transactionService.GetTransactionByIdAsync(openingBalance.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("Adjustment", detail!.Direction);
        Assert.Equal("balance_adjustment", detail.DisplaySemantic);
        Assert.Equal("balance_only", detail.ReportingBucket);
        Assert.True(detail.IsGloballyNeutralized);
        Assert.Equal(TransactionEntryKinds.OpeningBalanceAdjustment, detail.EntryKind);
        Assert.Equal(TransactionAnalyticsTreatments.BalanceOnly, detail.AnalyticsTreatment);

        var incomePage = await transactionService.GetTransactionsPageAsync(
            new TransactionPageRequest(account.Id, 20, null, null, "income", null),
            CancellationToken.None);
        Assert.True(incomePage.Succeeded);
        Assert.Empty(incomePage.Value!.Items);

        var categoryResult = await transactionService.UpdateTransactionMetadataAsync(
            openingBalance.Id,
            new UpdateTransactionMetadataRequest(null, null, 13010, 130101),
            CancellationToken.None);
        Assert.Null(categoryResult.Transaction);
        Assert.Equal("transaction_not_categorizable", categoryResult.ErrorCode);
    }

    [Fact]
    public async Task FinancialConsumers_ExcludeBalanceOnlyRowsButKeepThemInAccountBalance()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Manual current account",
            Type = "Current",
            Currency = "EUR",
            Source = FinancialAccountSources.Manual,
            CreatedUtc = now.AddDays(-10)
        });
        dbContext.Transactions.AddRange(
            CreateTransaction(accountId, 1_000m, "Opening balance", now.AddDays(-5), TransactionAnalyticsTreatments.BalanceOnly),
            CreateTransaction(accountId, -250m, "Balance correction", now.AddDays(-4), TransactionAnalyticsTreatments.BalanceOnly),
            CreateTransaction(accountId, 200m, "Salary", now.AddDays(-3)),
            CreateTransaction(accountId, -20m, "Gym membership", now.AddDays(-2)),
            CreateTransaction(accountId, -20m, "Gym membership", now.AddDays(-1)));
        await dbContext.SaveChangesAsync();

        var summary = await new UserFinancialSummaryService(dbContext)
            .GetSummaryAsync(userId, CancellationToken.None);
        Assert.Equal(200m, summary.IncomeLast30Days);
        Assert.Equal(40m, summary.SpendLast30Days);
        Assert.Equal(160m, summary.NetLast30Days);

        var spending = await new SpendingAnalysisService(dbContext)
            .AnalyzeAsync(userId, 30, CancellationToken.None);
        Assert.Equal(40m, spending.SpendByDomain[0]);
        Assert.Equal(20m, spending.LargestExpense);

        var recurring = await new RecurringObligationsService(dbContext)
            .GetRecurringAsync(userId, CancellationToken.None);
        var recurringItem = Assert.Single(recurring.Items);
        Assert.Equal("Gym membership", recurringItem.Name);

        var budget = await new BudgetStatusService(dbContext)
            .GetBudgetStatusAsync(userId, CancellationToken.None);
        Assert.Equal(40m, budget.MonthToDateSpend);

        var openingSearch = await new TransactionQueryService(dbContext)
            .QueryAsync(userId, "Opening", 20, CancellationToken.None);
        Assert.Empty(openingSearch.Items);

        var currentUser = new TestCurrentUserProvider(userId);
        var dashboard = await new DashboardService(
                dbContext,
                currentUser,
                new AccountBalanceReadService(dbContext, currentUser, TimeProvider.System),
                new ExpenseTaxonomyService())
            .GetSummaryAsync(CancellationToken.None);
        Assert.Equal(910m, dashboard.TotalBalance);
        Assert.Equal(40m, dashboard.RecentOutflow);
        Assert.Contains(
            dashboard.RecentTransactions,
            transaction => transaction.AnalyticsTreatment == TransactionAnalyticsTreatments.BalanceOnly
                && transaction.DisplaySemantic == "balance_adjustment");
    }

    private static Transaction CreateTransaction(
        Guid accountId,
        decimal amount,
        string description,
        DateTime bookedAtUtc,
        string analyticsTreatment = TransactionAnalyticsTreatments.Ordinary)
    {
        var balanceOnly = analyticsTreatment == TransactionAnalyticsTreatments.BalanceOnly;
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = "EUR",
            Description = description,
            EntryKind = balanceOnly
                ? TransactionEntryKinds.OpeningBalanceAdjustment
                : TransactionEntryKinds.ManualAdjustment,
            AnalyticsTreatment = analyticsTreatment,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc,
            DeterministicClassificationStatus = balanceOnly
                ? DeterministicClassificationStatus.EvaluatedNoMatchingRule
                : DeterministicClassificationStatus.NotEvaluated,
            DeterministicClassificationTerminal = balanceOnly
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"manual-account-provenance-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext)
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "manual-provenance-tests@local",
            NormalizedEmail = "manual-provenance-tests@local",
            DisplayName = "Manual Provenance Tester",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = now,
            UpdatedUtc = now,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-IE",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });
        await dbContext.SaveChangesAsync();
        return userId;
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
