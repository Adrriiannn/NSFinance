using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class AITriggerGateServiceTests
{
    [Fact]
    public async Task Evaluate_DeterministicResolved_BlocksAI()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(deterministicResolved: true),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.DeterministicTerminal, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_RegistryResolved_BlocksAI()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(registryResolved: true),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.RegistryResolved, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_D0DomainPolicy_BlocksAI()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(triggerMode: DomainTriggerMode.D0),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.DomainPolicyDisallowsAI, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_D1DomainPolicy_BlocksAIByDefault()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(triggerMode: DomainTriggerMode.D1),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.DomainPolicyDisallowsAI, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_D2DomainPolicy_AllowsWhenGated()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(triggerMode: DomainTriggerMode.D2, merchantOccurrenceCount: 2),
            CancellationToken.None);

        Assert.True(decision.ShouldTriggerAI);
        Assert.Null(decision.SkipReason);
        Assert.False(decision.SuggestionOnly);
    }

    [Fact]
    public async Task Evaluate_D3DomainPolicy_AllowsSuggestionOnly()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(triggerMode: DomainTriggerMode.D3, merchantOccurrenceCount: 2),
            CancellationToken.None);

        Assert.True(decision.ShouldTriggerAI);
        Assert.True(decision.SuggestionOnly);
        Assert.Null(decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_DuplicateMerchantInRun_BlocksAfterFirstDecision()
    {
        await using var dbContext = CreateDbContext();
        var runState = new MerchantResolutionRunState(Guid.NewGuid());
        var gate = CreateGate(dbContext);

        var first = await gate.EvaluateAsync(
            CreateInput(runState: runState, merchantOccurrenceCount: 2),
            CancellationToken.None);
        var second = await gate.EvaluateAsync(
            CreateInput(runState: runState, merchantOccurrenceCount: 2),
            CancellationToken.None);

        Assert.True(first.ShouldTriggerAI);
        Assert.False(second.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.DuplicateMerchantInRun, second.SkipReason);
    }

    [Fact]
    public async Task Evaluate_RunBudgetExceeded_BlocksWhenRunQuotaUsed()
    {
        await using var dbContext = CreateDbContext();
        var runState = new MerchantResolutionRunState(Guid.NewGuid());
        runState.MarkAICallExecuted(Guid.Parse("7F6F54C2-8D2C-4892-BAB8-9D6757A13C12"));
        runState.MarkAICallExecuted(Guid.Parse("7F6F54C2-8D2C-4892-BAB8-9D6757A13C12"));
        runState.MarkAICallExecuted(Guid.Parse("7F6F54C2-8D2C-4892-BAB8-9D6757A13C12"));

        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(runState: runState, merchantOccurrenceCount: 2),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.RunBudgetExceeded, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_DailyBudgetExceeded_BlocksWhenUserQuotaUsed()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.Parse("CB8CD66D-EFAC-41AA-86EA-312B3A80353C");
        for (var i = 0; i < 10; i++)
        {
            dbContext.MerchantAIDecisionLogs.Add(new MerchantAIDecisionLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Descriptor = "seed",
                NormalizedDescriptor = "seed",
                MerchantKey = $"seed-{i}",
                DomainCandidates = "130",
                TriggerMode = "D2",
                DeterministicResult = "not_terminal",
                RegistryResult = "registry_miss",
                AIGateDecision = true,
                AISkipReason = "None",
                BudgetState = "seed",
                CooldownState = "seed",
                FinalState = "AIResolvedTerminal",
                AICallExecuted = true,
                CreatedUtc = DateTime.UtcNow.AddMinutes(-5)
            });
        }

        await dbContext.SaveChangesAsync();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(userId: userId, merchantOccurrenceCount: 2),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.DailyBudgetExceeded, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_CooldownActive_BlocksAI()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(unresolvedCooldownUntilUtc: DateTime.UtcNow.AddHours(2), merchantOccurrenceCount: 2),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.MerchantOnCooldown, decision.SkipReason);
    }

    [Fact]
    public async Task Evaluate_UnknownMerchantAlone_DoesNotTriggerAI()
    {
        await using var dbContext = CreateDbContext();
        var gate = CreateGate(dbContext);
        var decision = await gate.EvaluateAsync(
            CreateInput(
                normalizedDescriptor: "unknown merchant 123456",
                amount: -8m,
                merchantOccurrenceCount: 1),
            CancellationToken.None);

        Assert.False(decision.ShouldTriggerAI);
        Assert.Equal(AITriggerSkipReason.ExpectedValueTooLow, decision.SkipReason);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ai-trigger-gate-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static AITriggerGateService CreateGate(AppDbContext dbContext)
    {
        return new AITriggerGateService(
            dbContext,
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true,
                MaxAICallsPerSyncRun = 3,
                MaxAICallsPerConnectionPerRun = 2,
                MaxAICallsPerUserPer24h = 10,
                MinimumOccurrencesForExpectedValue = 2,
                MeaningfulSpendThreshold = 75m
            }),
            Options.Create(new AIIntegrationOptions
            {
                Enabled = true
            }));
    }

    private static AITriggerGateInput CreateInput(
        DomainTriggerMode triggerMode = DomainTriggerMode.D2,
        bool deterministicResolved = false,
        bool registryResolved = false,
        bool descriptorMerchantLike = true,
        DateTime? unresolvedCooldownUntilUtc = null,
        Guid? userId = null,
        decimal amount = -120m,
        int merchantOccurrenceCount = 2,
        string normalizedDescriptor = "coffee shop",
        MerchantResolutionRunState? runState = null)
    {
        var request = new MerchantResolutionRequest(
            RawDescriptor: normalizedDescriptor,
            UserId: userId,
            ConnectionId: Guid.Parse("7F6F54C2-8D2C-4892-BAB8-9D6757A13C12"),
            SyncRunId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            NormalizedTransactionId: Guid.NewGuid(),
            TaxonomyDomainId: 130,
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: null,
            DeterministicTerminal: deterministicResolved,
            DeterministicResultCode: deterministicResolved ? "deterministic_terminal" : "not_terminal",
            ManualOverridePresent: false,
            Amount: amount,
            DescriptorMerchantLike: descriptorMerchantLike,
            TriggerSource: "unit_test",
            RunState: runState ?? new MerchantResolutionRunState(Guid.NewGuid()));

        return new AITriggerGateInput(
            Request: request,
            MerchantKey: normalizedDescriptor,
            NormalizedDescriptor: normalizedDescriptor,
            PolicyEvaluation: new DomainTriggerPolicyEvaluation(
                TriggerMode: triggerMode,
                DomainCandidates: triggerMode == DomainTriggerMode.D0 ? [920] : [130],
                UsedInferredCandidates: false),
            DeterministicResolved: deterministicResolved,
            RegistryResolved: registryResolved,
            DescriptorMerchantLike: descriptorMerchantLike,
            MerchantInvestigatedAtUtc: null,
            MerchantCooldownUntilUtc: null,
            UnresolvedCooldownUntilUtc: unresolvedCooldownUntilUtc,
            MerchantOccurrenceCount: merchantOccurrenceCount);
    }
}

