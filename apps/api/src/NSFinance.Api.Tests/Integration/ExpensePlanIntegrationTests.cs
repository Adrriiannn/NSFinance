using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Modules.ExpenseTracker.Validators;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using Xunit;

namespace NSFinance.Api.Tests.Integration;

public class ExpensePlanIntegrationTests
{
    private const string SeedDisplayName = "Plan Alpha";
    private const string SeedHandle = "plan_alpha";
    private const string SeedEmail = "plan.alpha@example.test";

    [Fact]
    public async Task CreatePlanAsync_PersistsCanonicalLineItemsAndRecurrenceMetadata()
    {
        await using var harness = new TestHarness();

        var created = await harness.PlanService.CreatePlanAsync(
            new CreateExpensePlanRequest(
                "Monthly household runway",
                "Plan for essentials",
                null,
                ExpensePlanStatuses.Drafted,
                ExpensePlanTypes.Monthly,
                new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc),
                "eur",
                3000m,
                [
                    new ExpensePlanLineItemRequest(13010, 130111, 420m, null, 0),
                    new ExpensePlanLineItemRequest(14010, 140101, 120m, null, 1)
                ],
                ["home", "march"],
                false,
                true,
                new ExpensePlanRecurrenceDto(ExpensePlanRecurrenceTypes.Monthly, 1, null, new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc)),
                true,
                ExpensePlanSharingModes.DirectShare,
                null,
                ExpensePlanOriginTypes.Manual),
            CancellationToken.None);

        Assert.Equal("Monthly household runway", created.Title);
        Assert.Equal(540m, created.ExpectedSpendTotal);
        Assert.Equal(2460m, created.ExpectedRemainingTotal);
        Assert.True(created.IsRecurring);
        Assert.NotNull(created.Recurrence);
        Assert.Equal(2, created.LineItems.Count);
        Assert.Equal(130111, created.LineItems[0].TaxonomySubcategoryId);
        Assert.Equal(130, created.LineItems[0].TaxonomyDomainId);
        Assert.Equal($"@{SeedHandle}", created.CreatorTagSnapshot);

        var stored = await harness.DbContext.ExpensePlans.Include(x => x.LineItems).SingleAsync();
        Assert.Equal(harness.CurrentUserProvider.UserId, stored.UserId);
        Assert.Equal(2, stored.LineItems.Count);
    }

    [Fact]
    public async Task CompletedPlans_AreLockedAndReuseDuplicatesInsteadOfReopening()
    {
        await using var harness = new TestHarness();
        var created = await harness.PlanService.CreatePlanAsync(
            new CreateExpensePlanRequest(
                "Weekly food",
                null,
                null,
                ExpensePlanStatuses.Active,
                ExpensePlanTypes.Weekly,
                new DateTime(2026, 03, 02, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 03, 08, 0, 0, 0, DateTimeKind.Utc),
                "EUR",
                500m,
                [new ExpensePlanLineItemRequest(13010, 130111, 90m, null, 0)],
                null,
                false,
                false,
                null,
                false,
                null,
                null,
                ExpensePlanOriginTypes.Manual),
            CancellationToken.None);

        var completed = await harness.PlanService.TransitionPlanAsync(
            created.Id,
            new TransitionExpensePlanRequest(ExpensePlanStatuses.Completed, "Period finished"),
            CancellationToken.None);

        Assert.NotNull(completed);
        Assert.Equal(ExpensePlanStatuses.Completed, completed!.Status);
        Assert.NotNull(completed.LockedAtUtc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.PlanService.UpdatePlanAsync(
            created.Id,
            new UpdateExpensePlanRequest(
                "Changed",
                null,
                null,
                ExpensePlanTypes.Weekly,
                new DateTime(2026, 03, 02, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 03, 08, 0, 0, 0, DateTimeKind.Utc),
                "EUR",
                500m,
                [new ExpensePlanLineItemRequest(13010, 130111, 95m, null, 0)],
                null,
                false,
                false,
                null,
                false,
                null,
                null),
            CancellationToken.None));

        var duplicate = await harness.PlanService.DuplicatePlanAsync(created.Id, CancellationToken.None);

        Assert.NotNull(duplicate);
        Assert.NotEqual(created.Id, duplicate!.Id);
        Assert.Equal(created.Id, duplicate.SourcePlanId);
        Assert.Equal(ExpensePlanStatuses.Drafted, duplicate.Status);
    }

    [Fact]
    public async Task GetPlanByIdAsync_ComputesComparisonAndUnexpectedCategories()
    {
        await using var harness = new TestHarness();
        var created = await harness.PlanService.CreatePlanAsync(
            new CreateExpensePlanRequest(
                "March essentials",
                null,
                null,
                ExpensePlanStatuses.Active,
                ExpensePlanTypes.Monthly,
                new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc),
                "EUR",
                2000m,
                [new ExpensePlanLineItemRequest(13010, 130111, 100m, null, 0)],
                null,
                false,
                false,
                null,
                false,
                null,
                null,
                ExpensePlanOriginTypes.Manual),
            CancellationToken.None);

        await harness.EntryService.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Groceries",
                80m,
                "EUR",
                130111,
                "AIB",
                new DateTime(2026, 03, 10, 12, 0, 0, DateTimeKind.Utc),
                null,
                [],
                "completed",
                false,
                "Tesco"),
            CancellationToken.None);

        await harness.EntryService.CreateEntryAsync(
            new CreateExpenseTrackerEntryRequest(
                "Taxi home",
                25m,
                "EUR",
                120108,
                "AIB",
                new DateTime(2026, 03, 11, 23, 0, 0, DateTimeKind.Utc),
                null,
                [],
                "completed",
                false,
                "Uber"),
            CancellationToken.None);

        var detail = await harness.PlanService.GetPlanByIdAsync(created.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(105m, detail!.Comparison.ActualSpendTotal);
        Assert.Single(detail.Comparison.PlannedLineItems);
        Assert.Equal(80m, detail.Comparison.PlannedLineItems[0].ActualAmount);
        Assert.Single(detail.Comparison.UnexpectedCategories);
        Assert.Equal(25m, detail.Comparison.UnexpectedCategories[0].ActualAmount);
        Assert.Equal(1, detail.Comparison.UnexpectedTransactionCount);
    }

    [Fact]
    public void ExpensePlanRequestValidator_RejectsSystemSubcategoriesForManualPlans()
    {
        var taxonomy = new ExpenseTaxonomyService();
        var errors = ExpensePlanRequestValidator.ValidateCreate(
            new CreateExpensePlanRequest(
                "Invalid",
                null,
                null,
                ExpensePlanStatuses.Drafted,
                ExpensePlanTypes.Monthly,
                new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc),
                "EUR",
                1000m,
                [new ExpensePlanLineItemRequest(91010, 910101, 100m, null, 0)],
                null,
                false,
                false,
                null,
                false,
                null,
                null,
                ExpensePlanOriginTypes.Manual),
            taxonomy);

        Assert.True(errors.ContainsKey("lineItems") || errors.ContainsKey("LineItems"));
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public TestHarness()
        {
            CurrentUserProvider = new MutableCurrentUserProvider();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"expense-plan-tests-{Guid.NewGuid()}")
                .Options;

            DbContext = new AppDbContext(options);
            SeedUser();
            TaxonomyService = new ExpenseTaxonomyService();
            EntryService = new ExpenseTrackerService(DbContext, CurrentUserProvider, TaxonomyService);
            PlanService = new ExpensePlanService(DbContext, CurrentUserProvider, TaxonomyService);
        }

        public AppDbContext DbContext { get; }
        public MutableCurrentUserProvider CurrentUserProvider { get; }
        public ExpenseTaxonomyService TaxonomyService { get; }
        public ExpenseTrackerService EntryService { get; }
        public ExpensePlanService PlanService { get; }

        private void SeedUser()
        {
            DbContext.Users.Add(new User
            {
                Id = CurrentUserProvider.UserId,
                PrimaryEmail = SeedEmail,
                NormalizedEmail = SeedEmail.ToUpperInvariant(),
                DisplayName = SeedDisplayName,
                FullName = $"{SeedDisplayName} User",
                Handle = SeedHandle,
                Status = "active",
                OnboardingStatus = "complete",
                Role = "user",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                EmailVerified = true,
                Timezone = "Europe/London",
                Locale = "en-GB",
                PreferredCurrency = "EUR",
                PlanTier = "standard"
            });
            DbContext.SaveChanges();
        }

        public ValueTask DisposeAsync()
        {
            return DbContext.DisposeAsync();
        }
    }

    private sealed class MutableCurrentUserProvider : ICurrentUserProvider
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();

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

