using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using Xunit;

namespace NSFinance.Api.Tests.Integration;

public class ExpenseTrackerIntegrationTests
{
    [Fact]
    public async Task CreateEntryAsync_PersistsEntryForCurrentUser()
    {
        await using var harness = new TestHarness();

        var created = await harness.Service.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Weekly groceries",
                76.25m,
                "eur",
                "Groceries",
                "AIB",
                new DateTime(2026, 03, 14, 19, 30, 0, DateTimeKind.Utc),
                "Bought dinner supplies",
                ["home", "food", "food"],
                "completed",
                false,
                "Tesco"),
            CancellationToken.None);

        Assert.Equal("Weekly groceries", created.Title);
        Assert.Equal(76.25m, created.Amount);
        Assert.Equal("EUR", created.Currency);
        Assert.Equal("Groceries", created.Category);
        Assert.Equal("AIB", created.PaymentSource);
        Assert.Equal("completed", created.Status);
        Assert.Equal(["home", "food"], created.Tags);

        var stored = await harness.DbContext.ExpenseTrackerEntries.SingleAsync();
        Assert.Equal(harness.CurrentUserProvider.UserId, stored.UserId);
    }

    [Fact]
    public async Task UpdateEntryAsync_UpdatesTrackedFields()
    {
        await using var harness = new TestHarness();
        var created = await harness.Service.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Cinema",
                18m,
                "EUR",
                "Entertainment",
                "Cash",
                new DateTime(2026, 03, 14, 20, 0, 0, DateTimeKind.Utc),
                null,
                ["weekend"],
                "planned",
                false,
                null),
            CancellationToken.None);

        var updated = await harness.Service.UpdateEntryAsync(
            created.Id,
            new UpdateExpenseTrackerEntryRequest(
                "Cinema and snacks",
                24.50m,
                "EUR",
                "Entertainment",
                "Credit Card",
                new DateTime(2026, 03, 15, 20, 0, 0, DateTimeKind.Utc),
                "Added popcorn",
                ["weekend", "treat"],
                "completed",
                true,
                "Odeon"),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Cinema and snacks", updated!.Title);
        Assert.Equal(24.50m, updated.Amount);
        Assert.Equal("Credit Card", updated.PaymentSource);
        Assert.Equal("completed", updated.Status);
        Assert.True(updated.IsRecurring);
        Assert.Equal("Odeon", updated.Merchant);
    }

    [Fact]
    public async Task DeleteEntryAsync_RemovesExistingEntry()
    {
        await using var harness = new TestHarness();
        var created = await harness.Service.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Bus fare",
                3.40m,
                "EUR",
                "Transport",
                "Cash",
                DateTime.UtcNow,
                null,
                [],
                "completed",
                false,
                null),
            CancellationToken.None);

        var deleted = await harness.Service.DeleteEntryAsync(created.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(await harness.DbContext.ExpenseTrackerEntries.ToListAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_ReturnsOnlyCurrentUsersEntriesOrderedByOccurredAtUtc()
    {
        await using var harness = new TestHarness();
        await harness.Service.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Older entry",
                12m,
                "EUR",
                "Bills",
                "AIB",
                new DateTime(2026, 03, 10, 9, 0, 0, DateTimeKind.Utc),
                null,
                [],
                "completed",
                false,
                null),
            CancellationToken.None);
        await harness.Service.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Latest entry",
                20m,
                "EUR",
                "Shopping",
                "Revolut",
                new DateTime(2026, 03, 14, 9, 0, 0, DateTimeKind.Utc),
                null,
                [],
                "completed",
                false,
                null),
            CancellationToken.None);

        harness.DbContext.ExpenseTrackerEntries.Add(new Persistence.Entities.ExpenseTrackerEntry
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Title = "Other user",
            Amount = 99m,
            Currency = "EUR",
            Category = "Other",
            PaymentSource = "Cash",
            OccurredAtUtc = new DateTime(2026, 03, 16, 9, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            TagsJson = "[]",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
        await harness.DbContext.SaveChangesAsync();

        var entries = await harness.Service.GetEntriesAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Latest entry", entries[0].Title);
        Assert.Equal("Older entry", entries[1].Title);
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public TestHarness()
        {
            CurrentUserProvider = new MutableCurrentUserProvider();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"expense-tracker-tests-{Guid.NewGuid()}")
                .Options;

            DbContext = new AppDbContext(options);
            Service = new ExpenseTrackerService(DbContext, CurrentUserProvider);
        }

        public AppDbContext DbContext { get; }
        public MutableCurrentUserProvider CurrentUserProvider { get; }
        public ExpenseTrackerService Service { get; }

        public ValueTask DisposeAsync()
        {
            return DbContext.DisposeAsync();
        }
    }

    private sealed class MutableCurrentUserProvider : ICurrentUserProvider
    {
        public Guid UserId { get; private set; } = Guid.NewGuid();
        public Guid SessionId { get; private set; } = Guid.NewGuid();

        public bool TryGetUserId(out Guid userId)
        {
            userId = UserId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = SessionId;
            return true;
        }
    }
}
