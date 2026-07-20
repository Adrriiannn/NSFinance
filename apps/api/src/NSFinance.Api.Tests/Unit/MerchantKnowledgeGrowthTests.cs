using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class MerchantKnowledgeGrowthTests
{
    [Fact]
    public async Task Growth_PromotesVerifiedAssignedMerchant_IntoKnowledge()
    {
        await using var dbContext = CreateDbContext();
        var transactions = new[]
        {
            CreateTransaction("VDP-NEWCAFE DUNDRUM 12", -6.4m),
            CreateTransaction("VDP-NEWCAFE DUNDRUM 12", -8.1m)
        };

        var investigation = new CountingInvestigationService(StrongResult("NewCafe Dundrum"));
        var judge = new FakeJudge(new MerchantCategoryJudgment(
            Assigned: true,
            DefinitionKey: "cat:13020",
            Confidence: 0.9,
            Rationale: "Cafe serving prepared food matches Dining inclusion rules.",
            AbstainReason: null));

        var service = CreateService(dbContext, investigation, judge, AcceptAll());
        var summary = await service.GrowAsync(transactions, CancellationToken.None);

        Assert.Equal(1, summary.Investigated);
        Assert.Equal(1, summary.Promoted);
        Assert.Equal(0, summary.SentToReview);

        var knowledge = await dbContext.MerchantKnowledge.SingleAsync();
        Assert.Equal("NEWCAFE DUNDRUM 12", knowledge.NormalizedPattern);
        Assert.Equal(MerchantKnowledgeSources.AiInvestigation, knowledge.Source);
        Assert.Equal(13020, knowledge.TaxonomyCategoryId);
        Assert.Equal(130, knowledge.TaxonomyDomainId);
        Assert.Equal(0.9, knowledge.Confidence);
        Assert.NotNull(knowledge.VerificationEvidenceJson);
        Assert.Contains("acceptance", knowledge.VerificationEvidenceJson);

        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.Promoted, candidate.Status);
        Assert.Equal(knowledge.Id, candidate.PromotedKnowledgeId);
        Assert.Equal(2, candidate.ObservedOccurrences);
    }

    [Fact]
    public async Task Growth_JudgeAbstains_ParksForReview_NeverWritesKnowledge()
    {
        await using var dbContext = CreateDbContext();
        var transactions = new[] { CreateTransaction("MYSTERY VENDOR LTD", -25m) };

        var judge = new FakeJudge(new MerchantCategoryJudgment(
            Assigned: false,
            DefinitionKey: null,
            Confidence: 0.3,
            Rationale: "Could be groceries or dining.",
            AbstainReason: "mixed_use"));

        var service = CreateService(
            dbContext,
            new CountingInvestigationService(StrongResult("Mystery Vendor")),
            judge,
            AcceptAll());
        var summary = await service.GrowAsync(transactions, CancellationToken.None);

        Assert.Equal(1, summary.SentToReview);
        Assert.Empty(await dbContext.MerchantKnowledge.ToListAsync());

        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.NeedsReview, candidate.Status);
        Assert.Equal("mixed_use", candidate.LastOutcomeCode);
        Assert.NotNull(candidate.InvestigationSummaryJson);
    }

    [Fact]
    public async Task Growth_IdentityNotAccepted_AppliesCooldown_AndSkipsNextRun()
    {
        await using var dbContext = CreateDbContext();
        var transactions = new[] { CreateTransaction("SHADY UNKNOWN 99", -10m) };

        var investigation = new CountingInvestigationService(StrongResult("Shady Unknown"));
        var rejectingPolicy = new FakeAcceptancePolicy(new MerchantAcceptanceDecision(
            MerchantAcceptanceDecisionType.Unresolved,
            0.4,
            null,
            ["weak_source_trust_profile"]));

        var service = CreateService(dbContext, investigation, new FakeJudge(null!), rejectingPolicy);

        var first = await service.GrowAsync(transactions, CancellationToken.None);
        Assert.Equal(1, first.Investigated);
        Assert.Equal(1, investigation.CallCount);

        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal(MerchantKnowledgeCandidateStatuses.Pending, candidate.Status);
        Assert.Equal(1, candidate.AttemptCount);
        Assert.NotNull(candidate.NextEligibleUtc);
        Assert.True(candidate.NextEligibleUtc > DateTime.UtcNow);

        // Within the cooldown the descriptor must not be re-investigated.
        var second = await service.GrowAsync(transactions, CancellationToken.None);
        Assert.Equal(0, second.Investigated);
        Assert.Equal(1, second.SkippedCooldown);
        Assert.Equal(1, investigation.CallCount);
    }

    [Fact]
    public async Task Growth_BelowConfidenceFloor_ParksForReview()
    {
        await using var dbContext = CreateDbContext();
        var transactions = new[] { CreateTransaction("BORDERLINE SHOP", -12m) };

        // Groceries floor is 0.75; judge only reaches 0.6.
        var judge = new FakeJudge(new MerchantCategoryJudgment(
            Assigned: true,
            DefinitionKey: "cat:13010",
            Confidence: 0.6,
            Rationale: "Possibly a grocer.",
            AbstainReason: null));

        var service = CreateService(
            dbContext,
            new CountingInvestigationService(StrongResult("Borderline Shop")),
            judge,
            AcceptAll());
        var summary = await service.GrowAsync(transactions, CancellationToken.None);

        Assert.Equal(1, summary.SentToReview);
        Assert.Empty(await dbContext.MerchantKnowledge.ToListAsync());
        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal("below_confidence_floor", candidate.LastOutcomeCode);
    }

    [Fact]
    public async Task Growth_DirectionMismatch_ParksForReview()
    {
        await using var dbContext = CreateDbContext();
        // Observed inflow only, but the judge picks outflow-only Groceries.
        var transactions = new[] { CreateTransaction("ODD REFUNDER", 30m) };

        var judge = new FakeJudge(new MerchantCategoryJudgment(
            Assigned: true,
            DefinitionKey: "cat:13010",
            Confidence: 0.95,
            Rationale: "Grocer.",
            AbstainReason: null));

        var service = CreateService(
            dbContext,
            new CountingInvestigationService(StrongResult("Odd Refunder")),
            judge,
            AcceptAll());
        var summary = await service.GrowAsync(transactions, CancellationToken.None);

        Assert.Equal(1, summary.SentToReview);
        Assert.Empty(await dbContext.MerchantKnowledge.ToListAsync());
        var candidate = await dbContext.MerchantKnowledgeCandidates.SingleAsync();
        Assert.Equal("direction_mismatch", candidate.LastOutcomeCode);
    }

    [Fact]
    public async Task Growth_RespectsPerRunInvestigationCap()
    {
        await using var dbContext = CreateDbContext();
        var transactions = new[]
        {
            CreateTransaction("VENDOR ALPHA", -10m),
            CreateTransaction("VENDOR ALPHA", -11m),
            CreateTransaction("VENDOR BETA", -12m)
        };

        var investigation = new CountingInvestigationService(StrongResult("Vendor"));
        var judge = new FakeJudge(new MerchantCategoryJudgment(
            Assigned: false,
            DefinitionKey: null,
            Confidence: 0.2,
            Rationale: "Unclear.",
            AbstainReason: "unclear"));

        var service = CreateService(dbContext, investigation, judge, AcceptAll(), maxPerRun: 1);
        var summary = await service.GrowAsync(transactions, CancellationToken.None);

        Assert.Equal(2, summary.DescriptorsConsidered);
        Assert.Equal(1, summary.Investigated);
        Assert.Equal(1, investigation.CallCount);
    }

    [Fact]
    public async Task BackfillWithGrowth_CategorizesPromotedRows_InTheSameRun()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var cafeVisit = CreateTransaction("VDP-NEWCAFE DUNDRUM 12", -6.4m);
        cafeVisit.FinancialAccountId = seeded.AccountId;
        dbContext.Transactions.Add(cafeVisit);
        await dbContext.SaveChangesAsync();

        var judge = new FakeJudge(new MerchantCategoryJudgment(
            Assigned: true,
            DefinitionKey: "cat:13020",
            Confidence: 0.9,
            Rationale: "Cafe.",
            AbstainReason: null));
        var growthService = new MerchantKnowledgeGrowthService(
            dbContext,
            new CountingInvestigationService(StrongResult("NewCafe Dundrum")),
            AcceptAll(),
            judge,
            Options.Create(new MerchantKnowledgeGrowthOptions { Enabled = true, MaxInvestigationsPerRun = 3 }),
            NullLogger<MerchantKnowledgeGrowthService>.Instance);
        var backfill = new MerchantCategorizationBackfillService(
            dbContext,
            growthService,
            Options.Create(new MerchantCategorizationOptions
            {
                BackfillOnGlobalSyncEnabled = true,
                MaxRowsPerRun = 500
            }),
            NullLogger<MerchantCategorizationBackfillService>.Instance);

        var summary = await backfill.BackfillAsync(seeded.UserId, CancellationToken.None);

        Assert.NotNull(summary.Growth);
        Assert.Equal(1, summary.Growth!.Promoted);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == cafeVisit.Id);
        Assert.Equal(13020, reloaded.TaxonomyCategoryId);
        Assert.Equal("merchant_knowledge", reloaded.CategorizationRuleKey);
        Assert.Equal("NEWCAFE DUNDRUM 12", reloaded.CategorizationSignal);
    }

    private static MerchantKnowledgeGrowthService CreateService(
        AppDbContext dbContext,
        IMerchantInvestigationService investigation,
        IMerchantCategoryJudge judge,
        IMerchantAcceptancePolicy acceptancePolicy,
        int maxPerRun = 3)
    {
        return new MerchantKnowledgeGrowthService(
            dbContext,
            investigation,
            acceptancePolicy,
            judge,
            Options.Create(new MerchantKnowledgeGrowthOptions
            {
                Enabled = true,
                MaxInvestigationsPerRun = maxPerRun,
                FailureCooldownHours = 72
            }),
            NullLogger<MerchantKnowledgeGrowthService>.Instance);
    }

    private static MerchantInvestigationResult StrongResult(string canonicalName)
    {
        var candidate = new MerchantInvestigationCandidate(
            ExistingMerchantId: null,
            CanonicalName: canonicalName,
            DisplayName: canonicalName,
            MerchantType: MerchantType.Merchant,
            MerchantUsageType: MerchantUsageType.NarrowUse,
            PrimaryCountryCode: "IE",
            Confidence: 0.95,
            AmbiguityScore: 0.05,
            MixedUseRisk: false,
            HasContradictions: false,
            OfficialWebsite: "https://example.ie",
            DescriptionSummary: "An Irish business.",
            AliasCandidates: []);

        return new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates: [candidate],
            Evidence:
            [
                new MerchantInvestigationEvidence(
                    MerchantEvidenceType.OfficialSource,
                    "Official site confirms the business.",
                    0.9,
                    "https://example.ie",
                    SourceClass: "official",
                    Relevance: 0.9,
                    SourceTrustLevel: MerchantSourceTrustLevel.OfficialDomain)
            ],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCandidate,
            OverallConfidence: 0.95,
            AmbiguityLevel: 0.05);
    }

    private static FakeAcceptancePolicy AcceptAll()
    {
        return new FakeAcceptancePolicy(decision: null);
    }

    private sealed class CountingInvestigationService(MerchantInvestigationResult result) : IMerchantInvestigationService
    {
        public int CallCount { get; private set; }

        public Task<MerchantInvestigationResult> InvestigateAsync(
            MerchantInvestigationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount += 1;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeJudge(MerchantCategoryJudgment judgment) : IMerchantCategoryJudge
    {
        public Task<MerchantCategoryJudgment> JudgeAsync(
            MerchantCategoryJudgmentInput input,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(judgment);
        }
    }

    // With a null decision, accepts the investigation's top candidate as
    // trusted; with a decision supplied, always returns that decision.
    private sealed class FakeAcceptancePolicy(MerchantAcceptanceDecision? decision) : IMerchantAcceptancePolicy
    {
        public MerchantAcceptanceDecision Evaluate(MerchantInvestigationResult result)
        {
            return decision ?? new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.AcceptedTrusted,
                0.95,
                result.Candidates.Count > 0 ? result.Candidates[0] : null,
                ["trusted_threshold_met"]);
        }
    }

    private static Transaction CreateTransaction(string description, decimal amount)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = Guid.NewGuid(),
            Amount = amount,
            Currency = "EUR",
            Description = description,
            EntryKind = TransactionEntryKinds.Ordinary,
            AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
            BookedAtUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"merchant-growth-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedUserWithAccountAsync(AppDbContext dbContext)
    {
        var userId = Guid.NewGuid();
        var email = $"growth-{userId:N}@example.test";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Growth Test User",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = DateTime.UtcNow
        });

        var accountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Growth Account",
            Type = "current",
            Currency = "EUR",
            Source = FinancialAccountSources.ProviderProjected,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return (userId, accountId);
    }
}
