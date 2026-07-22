using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Tests.Unit;

// CAT-001 phase two, reference lane: per-row constrained assignment for P2P
// rail rows, with the same gate discipline as the merchant lane.
public sealed class ReferenceLaneAssignmentTests
{
    [Fact]
    public void Allowlist_EveryKeyResolvesToFlooredDefinition()
    {
        foreach (var key in ReferenceRowJudgmentService.AllowedDefinitionKeys)
        {
            Assert.True(
                ReferenceRowJudgmentService.TryGetAllowedDefinition(key, out var definition),
                $"Reference-lane key '{key}' does not resolve to an AI-eligible definition.");
            Assert.NotNull(definition.ConfidenceFloor);
        }
    }

    [Fact]
    public async Task Assign_ZpayInflow_WritesAiAssignmentAndLedger()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var row = CreateTransaction(seeded.AccountId, "THREE BILL *ZPAY", 12.5m);
        dbContext.Transactions.Add(row);
        await dbContext.SaveChangesAsync();

        var judge = new FakeReferenceJudge(Assign("sub:900202", 0.8));
        var summary = await CreateLane(dbContext, judge).AssignAsync(seeded.UserId, [row], CancellationToken.None);

        Assert.Equal(1, summary.RowsEligible);
        Assert.Equal(1, summary.Judged);
        Assert.Equal(1, summary.Assigned);
        Assert.Equal(0, summary.Abstained);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == row.Id);
        Assert.Equal(900, reloaded.TaxonomyDomainId);
        Assert.Equal(90020, reloaded.TaxonomyCategoryId);
        Assert.Equal(900202, reloaded.TaxonomySubcategoryId);
        Assert.Equal("ai_assignment", reloaded.CategorizationRuleKey);
        // The evidence signal names the lane, never the reference text.
        Assert.Equal("reference_lane", reloaded.CategorizationSignal);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, reloaded.CategorizationCharacteristicsVersion);
        Assert.NotNull(reloaded.CategorizedUtc);

        var ledger = await dbContext.ReferenceLaneJudgments.SingleAsync(x => x.TransactionId == row.Id);
        Assert.Equal(ReferenceLaneJudgmentOutcomes.Assigned, ledger.Outcome);
        Assert.Equal("sub:900202", ledger.DefinitionKey);
        Assert.Equal(CategoryCharacteristicsCatalog.Version, ledger.CharacteristicsVersion);
        Assert.Equal(seeded.UserId, ledger.UserId);

        // The judge saw the row context, including the user's account names.
        Assert.Equal("inflow", judge.LastInput!.Direction);
        Assert.Contains("Reference Account", judge.LastInput.UserAccountNames);
    }

    [Fact]
    public async Task Assign_BelowFloor_LeavesRowUncategorized()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var row = CreateTransaction(seeded.AccountId, "SOMEONE *ZPAY", 20m);
        dbContext.Transactions.Add(row);
        await dbContext.SaveChangesAsync();

        var summary = await CreateLane(dbContext, new FakeReferenceJudge(Assign("sub:900202", 0.3)))
            .AssignAsync(seeded.UserId, [row], CancellationToken.None);

        Assert.Equal(0, summary.Assigned);
        Assert.Equal(1, summary.Abstained);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == row.Id);
        Assert.Null(reloaded.TaxonomyCategoryId);
        Assert.Null(reloaded.CategorizationRuleKey);

        var ledger = await dbContext.ReferenceLaneJudgments.SingleAsync(x => x.TransactionId == row.Id);
        Assert.Equal(ReferenceLaneJudgmentOutcomes.Abstained, ledger.Outcome);
        Assert.Equal("below_confidence_floor", ledger.OutcomeCode);
    }

    [Fact]
    public async Task Assign_DirectionMismatch_Refused()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        // Outflow row judged into an inflow-only definition must be refused.
        var row = CreateTransaction(seeded.AccountId, "SOMEONE *ZPAY", -20m);
        dbContext.Transactions.Add(row);
        await dbContext.SaveChangesAsync();

        await CreateLane(dbContext, new FakeReferenceJudge(Assign("sub:900202", 0.9)))
            .AssignAsync(seeded.UserId, [row], CancellationToken.None);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == row.Id);
        Assert.Null(reloaded.TaxonomyCategoryId);

        var ledger = await dbContext.ReferenceLaneJudgments.SingleAsync(x => x.TransactionId == row.Id);
        Assert.Equal("direction_mismatch", ledger.OutcomeCode);
    }

    [Fact]
    public async Task Assign_NonRailRows_AreNotEligible()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var row = CreateTransaction(seeded.AccountId, "TESCO STORES 3", -33m);
        dbContext.Transactions.Add(row);
        await dbContext.SaveChangesAsync();

        var summary = await CreateLane(dbContext, new ThrowingReferenceJudge())
            .AssignAsync(seeded.UserId, [row], CancellationToken.None);

        Assert.Equal(0, summary.RowsEligible);
        Assert.Equal(0, summary.Judged);
    }

    [Fact]
    public async Task Assign_RelationshipClaimedRow_IsNeverJudged()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var row = CreateTransaction(seeded.AccountId, "OWN MOVE *ZPAY", -20m);
        row.DeterministicRelationshipType = "internal_transfer";
        dbContext.Transactions.Add(row);
        await dbContext.SaveChangesAsync();

        var summary = await CreateLane(dbContext, new ThrowingReferenceJudge())
            .AssignAsync(seeded.UserId, [row], CancellationToken.None);

        Assert.Equal(0, summary.RowsEligible);
    }

    [Fact]
    public async Task Assign_AlreadyJudgedAtCurrentVersion_SkipsWithoutModelCall()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var row = CreateTransaction(seeded.AccountId, "SOMEONE *ZPAY", 20m);
        dbContext.Transactions.Add(row);
        dbContext.ReferenceLaneJudgments.Add(new ReferenceLaneJudgment
        {
            Id = Guid.NewGuid(),
            TransactionId = row.Id,
            UserId = seeded.UserId,
            Outcome = ReferenceLaneJudgmentOutcomes.Abstained,
            OutcomeCode = "judgment_abstained",
            CharacteristicsVersion = CategoryCharacteristicsCatalog.Version,
            JudgedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var summary = await CreateLane(dbContext, new ThrowingReferenceJudge())
            .AssignAsync(seeded.UserId, [row], CancellationToken.None);

        Assert.Equal(1, summary.RowsEligible);
        Assert.Equal(0, summary.Judged);
        Assert.Equal(1, summary.SkippedAlreadyJudged);
    }

    [Fact]
    public async Task Assign_JudgedAtOlderCatalogVersion_IsEligibleAgain()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        var row = CreateTransaction(seeded.AccountId, "SOMEONE *ZPAY", 20m);
        dbContext.Transactions.Add(row);
        dbContext.ReferenceLaneJudgments.Add(new ReferenceLaneJudgment
        {
            Id = Guid.NewGuid(),
            TransactionId = row.Id,
            UserId = seeded.UserId,
            Outcome = ReferenceLaneJudgmentOutcomes.Abstained,
            OutcomeCode = "judgment_abstained",
            CharacteristicsVersion = CategoryCharacteristicsCatalog.Version - 1,
            JudgedUtc = DateTime.UtcNow.AddDays(-30)
        });
        await dbContext.SaveChangesAsync();

        var judge = new FakeReferenceJudge(Assign("sub:900202", 0.8));
        var summary = await CreateLane(dbContext, judge).AssignAsync(seeded.UserId, [row], CancellationToken.None);

        Assert.Equal(1, summary.Judged);
        Assert.Equal(1, summary.Assigned);
        Assert.Equal(2, await dbContext.ReferenceLaneJudgments.CountAsync(x => x.TransactionId == row.Id));
    }

    [Fact]
    public async Task Backfill_RunsReferenceLane_WhenEnabled()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedUserWithAccountAsync(dbContext);
        // Inflow: the THREE merchant seed is direction-refused, so the row
        // survives the merchant passes and reaches the lane.
        var row = CreateTransaction(seeded.AccountId, "THREE BILL *ZPAY", 12.5m);
        dbContext.Transactions.Add(row);
        await dbContext.SaveChangesAsync();

        var backfill = new MerchantCategorizationBackfillService(
            dbContext,
            DisabledGrowth(dbContext),
            CreateLane(dbContext, new FakeReferenceJudge(Assign("sub:900202", 0.8))),
            new MerchantKnowledgeCurationService(
                dbContext,
                Options.Create(new MerchantCurationOptions()),
                NullLogger<MerchantKnowledgeCurationService>.Instance),
            Options.Create(new MerchantCategorizationOptions
            {
                BackfillOnGlobalSyncEnabled = true,
                MaxRowsPerRun = 500
            }),
            NullLogger<MerchantCategorizationBackfillService>.Instance);

        var summary = await backfill.BackfillAsync(seeded.UserId, CancellationToken.None);

        Assert.NotNull(summary.ReferenceLane);
        Assert.Equal(1, summary.ReferenceLane!.Assigned);
        Assert.Equal(1, summary.RowsCategorized);

        var reloaded = await dbContext.Transactions.SingleAsync(x => x.Id == row.Id);
        Assert.Equal(900202, reloaded.TaxonomySubcategoryId);
        Assert.Equal("ai_assignment", reloaded.CategorizationRuleKey);
    }

    // ---- Judge service: prompt and parse contracts ----

    [Fact]
    public async Task JudgeService_Prompt_CarriesRowContextAndAllowlistOnly()
    {
        var client = new CannedAIClient("""
            {"decision":"abstain","definitionKey":null,"confidence":0,"rationale":"n/a","abstainReason":"n/a"}
            """);
        await CreateJudgeService(client).JudgeAsync(JudgeInput(), CancellationToken.None);

        var prompt = client.LastRequest!.Messages.Single().Content;
        Assert.Contains("THREE BILL ZPAY", prompt);
        Assert.Contains("Main Current", prompt);
        foreach (var key in ReferenceRowJudgmentService.AllowedDefinitionKeys)
        {
            Assert.Contains(key, prompt);
        }

        // Merchant-catalog definitions outside the lane are never offered.
        Assert.DoesNotContain("cat:13010", prompt);
    }

    [Fact]
    public async Task JudgeService_KeyOutsideAllowlist_BecomesAbstention()
    {
        // cat:13010 is a perfectly valid catalog key - but not a reference-lane
        // meaning, so the lane must refuse it.
        var client = new CannedAIClient("""
            {"decision":"assign","definitionKey":"cat:13010","confidence":0.9,"rationale":"Groceries?","abstainReason":null}
            """);
        var judgment = await CreateJudgeService(client).JudgeAsync(JudgeInput(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("unknown_definition_key", judgment.AbstainReason);
    }

    [Fact]
    public async Task JudgeService_ValidAssignment_Parses()
    {
        var client = new CannedAIClient("""
            {"decision":"assign","definitionKey":"sub:900202","confidence":0.82,"rationale":"Reimbursement of a shared bill.","abstainReason":null}
            """);
        var judgment = await CreateJudgeService(client).JudgeAsync(JudgeInput(), CancellationToken.None);

        Assert.True(judgment.Assigned);
        Assert.Equal("sub:900202", judgment.DefinitionKey);
        Assert.Equal(0.82, judgment.Confidence);
    }

    // ---- Helpers ----

    private static MerchantCategoryJudgment Assign(string key, double confidence)
    {
        return new MerchantCategoryJudgment(
            Assigned: true,
            DefinitionKey: key,
            Confidence: confidence,
            Rationale: "test",
            AbstainReason: null);
    }

    private static ReferenceLaneAssignmentService CreateLane(AppDbContext dbContext, IReferenceRowJudge judge)
    {
        return new ReferenceLaneAssignmentService(
            dbContext,
            judge,
            Options.Create(new ReferenceLaneOptions { Enabled = true, MaxJudgmentsPerRun = 5 }),
            NullLogger<ReferenceLaneAssignmentService>.Instance);
    }

    private static MerchantKnowledgeGrowthService DisabledGrowth(AppDbContext dbContext)
    {
        return new MerchantKnowledgeGrowthService(
            dbContext,
            new ThrowingInvestigation(),
            new MerchantAcceptancePolicy(),
            new ThrowingCategoryJudge(),
            Options.Create(new MerchantKnowledgeGrowthOptions { Enabled = false }),
            NullLogger<MerchantKnowledgeGrowthService>.Instance);
    }

    private static ReferenceRowJudgmentService CreateJudgeService(CannedAIClient client)
    {
        return new ReferenceRowJudgmentService(
            new FakeRouter(),
            client,
            NullLogger<ReferenceRowJudgmentService>.Instance);
    }

    private static ReferenceRowJudgmentInput JudgeInput()
    {
        return new ReferenceRowJudgmentInput(
            ReferenceText: "THREE BILL ZPAY",
            Direction: "inflow",
            AbsAmountEur: 12.5m,
            BookedDate: new DateOnly(2026, 7, 14),
            SameReferenceOccurrences: 2,
            UserAccountNames: ["Main Current"]);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"reference-lane-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static Transaction CreateTransaction(Guid accountId, string description, decimal amount)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Description = description,
            Amount = amount,
            Currency = "EUR",
            BookedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static async Task<(Guid UserId, Guid AccountId)> SeedUserWithAccountAsync(AppDbContext dbContext)
    {
        var userId = Guid.NewGuid();
        var email = $"reference-{userId:N}@example.test";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Reference Test User",
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
            Name = "Reference Account",
            Type = "current",
            Currency = "EUR",
            Source = FinancialAccountSources.ProviderProjected,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return (userId, accountId);
    }

    private sealed class FakeReferenceJudge(MerchantCategoryJudgment judgment) : IReferenceRowJudge
    {
        public ReferenceRowJudgmentInput? LastInput { get; private set; }

        public Task<MerchantCategoryJudgment> JudgeAsync(
            ReferenceRowJudgmentInput input,
            CancellationToken cancellationToken)
        {
            LastInput = input;
            return Task.FromResult(judgment);
        }
    }

    private sealed class ThrowingReferenceJudge : IReferenceRowJudge
    {
        public Task<MerchantCategoryJudgment> JudgeAsync(
            ReferenceRowJudgmentInput input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("This row must not reach the reference judge.");
        }
    }

    private sealed class ThrowingInvestigation : IMerchantInvestigationService
    {
        public Task<MerchantInvestigationResult> InvestigateAsync(
            MerchantInvestigationRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Growth is disabled in these tests.");
        }
    }

    private sealed class ThrowingCategoryJudge : IMerchantCategoryJudge
    {
        public Task<MerchantCategoryJudgment> JudgeAsync(
            MerchantCategoryJudgmentInput input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Growth is disabled in these tests.");
        }
    }

    private sealed class FakeRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return new AIModelRoute(taskType, preferredModelClass, "test-model", "test-deployment", false, "resolved", []);
        }
    }

    private sealed class CannedAIClient(string payload) : IAIClient
    {
        public AIRequest? LastRequest { get; private set; }

        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AIResponse(
                Content: null,
                StructuredPayloadJson: payload,
                FinishReason: "stop",
                Provider: "test",
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: null,
                OutputTokenEstimate: null,
                LatencyMs: 1,
                WasMocked: true,
                RawDiagnostics: null,
                Succeeded: true,
                FailureReason: null));
        }
    }
}
