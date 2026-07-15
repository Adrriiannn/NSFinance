using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class AccountStatementImportDeletionTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 15, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DeleteAccountAsync_ReturnsControlledConflictWhenStatementHistoryExists()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var accountId = await SeedAccountAsync(dbContext, userId);
        dbContext.ImportJobs.Add(new ImportJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = accountId,
            FileName = "statement.csv",
            Kind = ImportJobKinds.StatementCsv,
            Status = StatementImportBatchStatuses.ReadyForReview,
            CreatedUtc = UtcNow,
            UpdatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, userId)
            .DeleteAccountAsync(accountId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("account_has_statement_import_history", result.Error!.Code);
        Assert.Equal(StatusCodes.Status409Conflict, result.Error.StatusCode);
        Assert.True(await dbContext.FinancialAccounts.AnyAsync(account => account.Id == accountId));
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesOwnedAccountWithoutStatementHistory()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext);
        var accountId = await SeedAccountAsync(dbContext, userId);

        var result = await CreateService(dbContext, userId)
            .DeleteAccountAsync(accountId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(await dbContext.FinancialAccounts.AnyAsync(account => account.Id == accountId));
    }

    [Fact]
    public async Task DeleteAccountAsync_DoesNotRevealAnotherUsersAccount()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = await SeedUserAsync(dbContext, "owner@local");
        var otherId = await SeedUserAsync(dbContext, "other@local");
        var accountId = await SeedAccountAsync(dbContext, ownerId);

        var result = await CreateService(dbContext, otherId)
            .DeleteAccountAsync(accountId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("account_not_found", result.Error!.Code);
        Assert.Equal(StatusCodes.Status404NotFound, result.Error.StatusCode);
        Assert.True(await dbContext.FinancialAccounts.AnyAsync(account => account.Id == accountId));
    }

    private static AccountService CreateService(AppDbContext dbContext, Guid userId)
    {
        var currentUser = new TestCurrentUserProvider(userId);
        return new AccountService(
            dbContext,
            currentUser,
            new AccountBalanceReadService(dbContext, currentUser, new FixedTimeProvider(UtcNow)),
            NullLogger<AccountService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"account-import-delete-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, string email = "owner@local")
    {
        var userId = Guid.NewGuid();
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Account deletion tester",
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

    private static async Task<Guid> SeedAccountAsync(AppDbContext dbContext, Guid userId)
    {
        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Import account",
            Type = "Current",
            Currency = "EUR",
            Source = FinancialAccountSources.Manual,
            CreatedUtc = UtcNow
        });
        await dbContext.SaveChangesAsync();
        return accountId;
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
