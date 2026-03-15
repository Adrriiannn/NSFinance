using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using Xunit;

namespace NSFinance.Api.Tests.Integration;

public class ExpensePlanCommunityIntegrationTests
{
    [Fact]
    public async Task PublishPlanAsync_CreatesPublishedCommunityPlanWithCreatorAttribution()
    {
        await using var harness = new TestHarness();
        var sourcePlan = await harness.CreatePlanAsync("Monthly essentials");

        var publication = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(sourcePlan.Id, "Monthly essentials blueprint", "A calm essentials-first plan.", ["monthly", "essentials"]),
            CancellationToken.None);

        Assert.Equal("published", publication.PublicationStatus);
        Assert.Equal("approved", publication.ModerationStatus);
        Assert.Equal(sourcePlan.Id, publication.SourcePlanId);
        Assert.Equal("Marius", publication.CreatorDisplayNameSnapshot);
        Assert.Equal("@marius", publication.CreatorTagSnapshot);
    }

    [Fact]
    public async Task PublishPlanAsync_BlockedMetadata_IsStoredButNotPubliclyVisible()
    {
        await using var harness = new TestHarness();
        var sourcePlan = await harness.CreatePlanAsync("Weekly reset");

        var publication = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(sourcePlan.Id, "Guaranteed profit budget", "Get rich quick with this plan.", ["profit"]),
            CancellationToken.None);

        Assert.Equal("blocked", publication.PublicationStatus);
        var visible = await harness.CommunityService.GetCommunityPlansAsync(
            new BrowseExpensePlanPublicationsRequest(null, "trending", null, null, false, 20),
            CancellationToken.None);
        Assert.DoesNotContain(visible, item => item.Id == publication.Id);
    }

    [Fact]
    public async Task ToggleLikeAsync_TogglesWithoutDuplicateLikes()
    {
        await using var harness = new TestHarness();
        var sourcePlan = await harness.CreatePlanAsync("Groceries");
        var publication = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(sourcePlan.Id, "Groceries", "Weekly grocery guardrails.", ["weekly"]),
            CancellationToken.None);

        var liked = await harness.CommunityService.ToggleLikeAsync(publication.Id, CancellationToken.None);
        Assert.NotNull(liked);
        Assert.Equal(1, liked!.LikeCount);

        var unliked = await harness.CommunityService.ToggleLikeAsync(publication.Id, CancellationToken.None);
        Assert.NotNull(unliked);
        Assert.Equal(0, unliked!.LikeCount);
    }

    [Fact]
    public async Task UsePublicationAsync_CreatesNewLocalPlanAndIncrementsDownloadCount()
    {
        await using var harness = new TestHarness();
        var sourcePlan = await harness.CreatePlanAsync("Travel pack");
        var publication = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(sourcePlan.Id, "Travel pack", "Travel planning shell.", ["travel", "monthly"]),
            CancellationToken.None);

        harness.CurrentUserProvider.SwitchUser(harness.SecondUserId);
        var imported = await harness.CommunityService.UsePublicationAsync(publication.Id, CancellationToken.None);

        Assert.NotNull(imported);
        Assert.Equal("drafted", imported!.Status);
        Assert.Equal(publication.Id, imported.ImportedFromPublicPlanId);

        var stored = await harness.DbContext.ExpensePlans.SingleAsync(x => x.Id == imported.Id);
        Assert.Equal(harness.SecondUserId, stored.UserId);
        Assert.Equal(publication.Id, stored.ImportedFromPublicPlanId);

        var refreshed = await harness.CommunityService.GetPublicationByIdAsync(publication.Id, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.Equal(1, refreshed!.DownloadCount);
    }

    [Fact]
    public async Task ReportPublicationAsync_FlagsPlanAfterThreshold()
    {
        await using var harness = new TestHarness();
        var sourcePlan = await harness.CreatePlanAsync("Family plan");
        var publication = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(sourcePlan.Id, "Family plan", "Monthly family budget.", ["family"]),
            CancellationToken.None);

        harness.CurrentUserProvider.SwitchUser(harness.SecondUserId);
        var first = await harness.CommunityService.ReportPublicationAsync(
            publication.Id,
            new ReportExpensePlanPublicationRequest(ExpensePlanReportReasons.Misleading, "Feels off."),
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1, first!.ReportCount);

        harness.CurrentUserProvider.SwitchUser(harness.ThirdUserId);
        var second = await harness.CommunityService.ReportPublicationAsync(
            publication.Id,
            new ReportExpensePlanPublicationRequest(ExpensePlanReportReasons.Spam, "Spammy wording."),
            CancellationToken.None);

        Assert.NotNull(second);
        Assert.Equal("flagged", second!.PublicationStatus);
        Assert.Equal("flagged_after_publish", second.ModerationStatus);
    }

    [Fact]
    public async Task GetCommunityPlansAsync_SortsByLikesDescending()
    {
        await using var harness = new TestHarness();
        var firstPlan = await harness.CreatePlanAsync("Alpha plan");
        var secondPlan = await harness.CreatePlanAsync("Beta plan");

        var alpha = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(firstPlan.Id, "Alpha plan", "Alpha", ["monthly"]),
            CancellationToken.None);
        var beta = await harness.CommunityService.PublishPlanAsync(
            new PublishExpensePlanRequest(secondPlan.Id, "Beta plan", "Beta", ["weekly"]),
            CancellationToken.None);

        harness.CurrentUserProvider.SwitchUser(harness.SecondUserId);
        await harness.CommunityService.ToggleLikeAsync(beta.Id, CancellationToken.None);
        harness.CurrentUserProvider.SwitchUser(harness.ThirdUserId);
        await harness.CommunityService.ToggleLikeAsync(beta.Id, CancellationToken.None);
        harness.CurrentUserProvider.SwitchUser(harness.FirstUserId);

        var ordered = await harness.CommunityService.GetCommunityPlansAsync(
            new BrowseExpensePlanPublicationsRequest(null, ExpensePlanPublicationSorts.MostLiked, null, null, false, 10),
            CancellationToken.None);

        Assert.Equal(beta.Id, ordered[0].Id);
        Assert.Equal(alpha.Id, ordered[1].Id);
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public TestHarness()
        {
            CurrentUserProvider = new MutableCurrentUserProvider();
            FirstUserId = CurrentUserProvider.UserId;
            SecondUserId = Guid.NewGuid();
            ThirdUserId = Guid.NewGuid();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"expense-plan-community-tests-{Guid.NewGuid()}")
                .Options;

            DbContext = new AppDbContext(options);
            SeedUser(FirstUserId, "marius@example.com", "Marius", "marius");
            SeedUser(SecondUserId, "aoife@example.com", "Aoife", "aoife");
            SeedUser(ThirdUserId, "liam@example.com", "Liam", "liam");
            TaxonomyService = new ExpenseTaxonomyService();
            PlanService = new ExpensePlanService(DbContext, CurrentUserProvider, TaxonomyService);
            CommunityService = new ExpensePlanCommunityService(DbContext, CurrentUserProvider, new NullAuditService());
        }

        public AppDbContext DbContext { get; }
        public MutableCurrentUserProvider CurrentUserProvider { get; }
        public ExpenseTaxonomyService TaxonomyService { get; }
        public ExpensePlanService PlanService { get; }
        public ExpensePlanCommunityService CommunityService { get; }
        public Guid FirstUserId { get; }
        public Guid SecondUserId { get; }
        public Guid ThirdUserId { get; }

        public Task<ExpensePlanDto> CreatePlanAsync(string title)
        {
            return PlanService.CreatePlanAsync(
                new CreateExpensePlanRequest(
                    title,
                    null,
                    null,
                    ExpensePlanStatuses.Drafted,
                    ExpensePlanTypes.Monthly,
                    new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc),
                    "EUR",
                    2500m,
                    [new ExpensePlanLineItemRequest(13010, 130111, 250m, null, 0)],
                    ["seed"],
                    false,
                    false,
                    null,
                    false,
                    null,
                    null,
                    ExpensePlanOriginTypes.Manual),
                CancellationToken.None);
        }

        private void SeedUser(Guid userId, string email, string displayName, string handle)
        {
            DbContext.Users.Add(new User
            {
                Id = userId,
                PrimaryEmail = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = displayName,
                FullName = displayName,
                Handle = handle,
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

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class MutableCurrentUserProvider : ICurrentUserProvider
    {
        public Guid UserId { get; private set; } = Guid.NewGuid();
        private Guid SessionId { get; set; } = Guid.NewGuid();

        public void SwitchUser(Guid userId)
        {
            UserId = userId;
            SessionId = Guid.NewGuid();
        }

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

    private sealed class NullAuditService : IAuditService
    {
        public Task WriteEventAsync(string category, string eventName, string targetEntityType, string? targetEntityId, Guid? actorId, string actorType, object? metadata, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
