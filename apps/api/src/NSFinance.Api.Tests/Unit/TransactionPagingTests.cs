using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Endpoints;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class TransactionPagingTests
{
    [Fact]
    public async Task GetTransactionsPageAsync_EqualTimestamps_PagesWithoutDuplicatesOrGaps()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext);
        var bookedAtUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var ids = Enumerable.Range(1, 5)
            .Select(value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
            .ToArray();

        dbContext.Transactions.AddRange(ids.Select((id, index) => new Transaction
        {
            Id = id,
            FinancialAccountId = seeded.AccountId,
            Amount = -(index + 1),
            Currency = "EUR",
            Description = $"Transaction {index + 1}",
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        }));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var first = await service.GetTransactionsPageAsync(
            new TransactionPageRequest(null, 2, null, null, null, null),
            CancellationToken.None);
        var second = await service.GetTransactionsPageAsync(
            new TransactionPageRequest(null, 2, null, null, null, first.Value!.NextCursor),
            CancellationToken.None);
        var third = await service.GetTransactionsPageAsync(
            new TransactionPageRequest(null, 2, null, null, null, second.Value!.NextCursor),
            CancellationToken.None);

        var actual = first.Value.Items
            .Concat(second.Value.Items)
            .Concat(third.Value!.Items)
            .Select(x => x.Id)
            .ToArray();
        var expected = ids.OrderByDescending(x => x).ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Distinct().Count());
        Assert.True(first.Value.HasMore);
        Assert.True(second.Value.HasMore);
        Assert.False(third.Value.HasMore);
        Assert.Null(third.Value.NextCursor);
    }

    [Fact]
    public async Task GetTransactionsPageAsync_DefaultAndMaximumPageSize_AreEnforced()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext);
        var now = DateTime.UtcNow;
        dbContext.Transactions.AddRange(Enumerable.Range(0, 51).Select(index => new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = seeded.AccountId,
            Amount = -index,
            Currency = "EUR",
            Description = $"Expense {index}",
            BookedAtUtc = now.AddMinutes(-index),
            CreatedUtc = now.AddMinutes(-index)
        }));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, seeded.UserId);
        var defaultPage = await service.GetTransactionsPageAsync(
            new TransactionPageRequest(null, null, null, null, null, null),
            CancellationToken.None);
        var oversized = await service.GetTransactionsPageAsync(
            new TransactionPageRequest(null, 101, null, null, null, null),
            CancellationToken.None);

        Assert.True(defaultPage.Succeeded);
        Assert.Equal(50, defaultPage.Value!.PageSize);
        Assert.Equal(50, defaultPage.Value.Items.Count);
        Assert.True(defaultPage.Value.HasMore);
        Assert.False(oversized.Succeeded);
        Assert.Equal("transaction_page_size_invalid", oversized.Error!.Code);
    }

    [Fact]
    public async Task GetTransactionsPageAsync_AppliesOwnershipAccountDateAndDirectionFilters()
    {
        await using var dbContext = CreateDbContext();
        var primary = await SeedAccountAsync(dbContext);
        var secondary = await SeedAccountAsync(dbContext, primary.UserId);
        var otherUser = await SeedAccountAsync(dbContext);
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var expectedId = Guid.NewGuid();

        dbContext.Transactions.AddRange(
            CreateTransaction(expectedId, primary.AccountId, -12m, now, "Expected expense"),
            CreateTransaction(Guid.NewGuid(), primary.AccountId, 50m, now, "Income"),
            CreateTransaction(Guid.NewGuid(), primary.AccountId, -8m, now.AddDays(-3), "Too old"),
            CreateTransaction(Guid.NewGuid(), secondary.AccountId, -9m, now, "Other account"),
            CreateTransaction(Guid.NewGuid(), otherUser.AccountId, -10m, now, "Other user"));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, primary.UserId).GetTransactionsPageAsync(
            new TransactionPageRequest(
                primary.AccountId,
                20,
                new DateTimeOffset(now.AddDays(-1)),
                new DateTimeOffset(now.AddDays(1)),
                "EXPENSE",
                null),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedId, row.Id);
        Assert.Equal(primary.AccountId, result.Value.Filters.AccountId);
        Assert.Equal("expense", result.Value.Filters.Direction);
    }

    [Fact]
    public async Task GetTransactionsPageAsync_CrossUserRelationship_DoesNotExposeCounterparty()
    {
        await using var dbContext = CreateDbContext();
        var primary = await SeedAccountAsync(dbContext);
        var otherUser = await SeedAccountAsync(dbContext);
        var now = DateTime.UtcNow;
        var primaryTransactionId = Guid.NewGuid();
        var otherTransactionId = Guid.NewGuid();
        dbContext.Transactions.AddRange(
            CreateTransaction(primaryTransactionId, primary.AccountId, -25m, now, "Primary"),
            CreateTransaction(otherTransactionId, otherUser.AccountId, 25m, now, "Other user"));
        dbContext.TransactionRelationships.Add(new TransactionRelationship
        {
            Id = Guid.NewGuid(),
            RelationshipKey = $"cross-user-{Guid.NewGuid():N}",
            RelationshipType = TransactionRelationshipType.InternalAccountTransfer,
            RelationshipStatus = TransactionRelationshipStatus.Active,
            RelationshipDirection = TransactionRelationshipDirection.OutflowToInflow,
            SourceTransactionId = primaryTransactionId,
            TargetTransactionId = otherTransactionId,
            SourceFinancialAccountId = primary.AccountId,
            TargetFinancialAccountId = otherUser.AccountId,
            ConfidenceScore = 100,
            ConfidenceTier = "high",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, primary.UserId).GetTransactionsPageAsync(
            new TransactionPageRequest(null, 20, null, null, null, null),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(primaryTransactionId, row.Id);
        Assert.Null(row.RelationshipCounterpartyTransactionId);
        Assert.Null(row.RelationshipType);
    }

    [Theory]
    [InlineData("not-a-cursor", null, null, null, "transaction_cursor_invalid")]
    [InlineData(null, "sideways", null, null, "transaction_direction_invalid")]
    public async Task GetTransactionsPageAsync_InvalidQuery_ReturnsStructuredFailure(
        string? cursor,
        string? direction,
        string? fromUtc,
        string? toUtc,
        string expectedCode)
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext);
        var result = await CreateService(dbContext, seeded.UserId).GetTransactionsPageAsync(
            new TransactionPageRequest(
                null,
                20,
                fromUtc is null ? null : DateTimeOffset.Parse(fromUtc),
                toUtc is null ? null : DateTimeOffset.Parse(toUtc),
                direction,
                cursor),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.StatusCode);
    }

    [Fact]
    public async Task GetTransactionPageEndpoint_InvalidPageSize_ReturnsApiErrorResponse()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAccountAsync(dbContext);
        var response = await GetTransactionPageEndpoint.HandleAsync(
            null,
            0,
            null,
            null,
            null,
            null,
            CreateService(dbContext, seeded.UserId),
            CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(response);
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(response);
        var error = Assert.IsType<ApiErrorResponse>(valueResult.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
        Assert.Equal("transaction_page_size_invalid", error.Code);
    }

    [Fact]
    public void ApplyPageCursor_NpgsqlProvider_TranslatesStableUuidTieBreaker()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);
        var cursor = new TransactionPageCursor(
            new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 14, 11, 59, 0, DateTimeKind.Utc),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));

        var sql = TransactionService.ApplyPageCursor(dbContext.Transactions, cursor)
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(51)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static TransactionService CreateService(AppDbContext dbContext, Guid userId)
    {
        return new TransactionService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new ExpenseTaxonomyService());
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"transaction-paging-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedAccountAsync(
        AppDbContext dbContext,
        Guid? existingUserId = null)
    {
        var now = DateTime.UtcNow;
        var userId = existingUserId ?? Guid.NewGuid();
        if (!existingUserId.HasValue)
        {
            var email = $"paging-{userId:N}@local";
            dbContext.Users.Add(new User
            {
                Id = userId,
                PrimaryEmail = email,
                NormalizedEmail = email,
                DisplayName = "Paging Tester",
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
        }

        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Current account",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now
        });
        await dbContext.SaveChangesAsync();

        return (userId, accountId);
    }

    private static Transaction CreateTransaction(
        Guid id,
        Guid accountId,
        decimal amount,
        DateTime bookedAtUtc,
        string description)
    {
        return new Transaction
        {
            Id = id,
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = "EUR",
            Description = description,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
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
