using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialCompanionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_BudgetIntent_InvokesBudgetToolset_AndPersistsCompanionLog()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId);
        var tools = new TrackingTools();
        var service = CreateService(dbContext, tools, enforceSoftCap: false);

        var response = await service.ExecuteAsync(
            new FinancialCompanionRequest(
                UserId: userId,
                SessionId: "session-budget-1",
                UserQuery: "Help me fix my monthly budget overspend."),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal(FinancialCompanionIntent.BudgetStatus, response.Intent);
        Assert.Contains("IUserFinancialSummaryService", response.ToolsUsed);
        Assert.Contains("IBudgetStatusService", response.ToolsUsed);
        Assert.NotNull(response.Evidence);
        Assert.NotNull(response.AdvicePacket);
        Assert.NotEmpty(response.AdvicePacket!.DeterministicFindings);
        Assert.Contains("based_on_budget_status", response.Evidence!.BasisSummary);
        Assert.Equal(1, await dbContext.CompanionAIInteractionLogs.CountAsync());
        Assert.Equal(0, await dbContext.MerchantAIDecisionLogs.CountAsync());
        Assert.True(tools.SummaryCalls >= 1);
        Assert.True(tools.BudgetCalls >= 1);
    }

    [Fact]
    public async Task ExecuteAsync_DailySoftCapReached_AddsWarningAndUsesFallbackModel()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId);
        for (var i = 0; i < 2; i++)
        {
            dbContext.CompanionAIInteractionLogs.Add(new CompanionAIInteractionLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionId = "seed",
                Intent = FinancialCompanionIntent.GeneralFinancialQuestion.ToString(),
                ToolsUsed = "none",
                TokensInput = 1,
                TokensOutput = 1,
                Model = "heavy-model",
                ResponseTimeMs = 10,
                Succeeded = true,
                CreatedUtc = DateTime.UtcNow.AddMinutes(-1)
            });
        }

        await dbContext.SaveChangesAsync();
        var tools = new TrackingTools();
        var service = CreateService(
            dbContext,
            tools,
            enforceSoftCap: false,
            dailySoftCap: 2);

        var response = await service.ExecuteAsync(
            new FinancialCompanionRequest(
                UserId: userId,
                SessionId: "session-cap-1",
                UserQuery: "Can I afford this purchase?"),
            CancellationToken.None);

        Assert.Contains("daily_soft_cap_reached", response.Warnings);
        Assert.Equal("fast-model", response.ModelUsed);
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousPrompt_ReturnsAmbiguousIntent_AndAvoidsExtraIntentTools()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId);
        var tools = new TrackingTools();
        var service = CreateService(dbContext, tools, enforceSoftCap: false);

        var response = await service.ExecuteAsync(
            new FinancialCompanionRequest(
                UserId: userId,
                SessionId: "session-ambiguous-1",
                UserQuery: "What should I do?"),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal(FinancialCompanionIntent.Ambiguous, response.Intent);
        Assert.Empty(response.ToolsUsed);
        Assert.DoesNotContain("IBudgetStatusService", response.ToolsUsed);
        Assert.Contains(response.Warnings, warning => warning.Equals("intent_ambiguous", StringComparison.Ordinal));
        Assert.True(response.HasInsufficientData);
        Assert.Contains("ambiguous_query_requires_clarification", response.InsufficientDataReasons ?? []);
    }

    [Fact]
    public async Task ExecuteAsync_RequiredToolMissing_ReturnsInsufficientGrounding()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId);
        var tools = new TrackingTools
        {
            FailBudgetStatus = true
        };
        var service = CreateService(dbContext, tools, enforceSoftCap: false);

        var response = await service.ExecuteAsync(
            new FinancialCompanionRequest(
                UserId: userId,
                SessionId: "session-insufficient-1",
                UserQuery: "Can I afford this purchase?"),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.True(response.HasInsufficientData);
        Assert.Contains("missing_required_budget_status", response.InsufficientDataReasons ?? []);
        Assert.Contains(response.Warnings, warning => warning.Contains("missing_required_budget_status", StringComparison.Ordinal));
    }

    private static FinancialCompanionService CreateService(
        AppDbContext dbContext,
        TrackingTools tools,
        bool enforceSoftCap,
        int dailySoftCap = 40)
    {
        var orchestrationOptions = Options.Create(new CompanionOrchestrationOptions());
        var policyProvider = new CompanionIntentToolPolicyProvider();
        var mergePolicy = new CompanionMixedIntentMergePolicy(policyProvider, orchestrationOptions);
        var planBuilder = new CompanionExecutionPlanBuilder(policyProvider, mergePolicy, orchestrationOptions);
        var contextShaper = new CompanionContextShaper(orchestrationOptions);
        var toolExecutor = new CompanionToolExecutor(
            tools,
            tools,
            tools,
            tools,
            tools,
            tools,
            tools,
            tools,
            contextShaper,
            orchestrationOptions,
            NullLogger<CompanionToolExecutor>.Instance);
        var insufficiencyEvaluator = new CompanionInsufficiencyEvaluator();
        var evidenceBuilder = new CompanionEvidenceBuilder();
        var assemblyResultBuilder = new CompanionAssemblyResultBuilder(evidenceBuilder);
        var assembler = new FinancialCompanionContextAssembler(
            planBuilder,
            toolExecutor,
            contextShaper,
            insufficiencyEvaluator,
            assemblyResultBuilder);
        var adviceOptions = Options.Create(new CompanionAdviceOptions());
        var taxonomy = new NSFinance.Api.Modules.ExpenseTracker.Services.ExpenseTaxonomyService();
        var categoryClassifier = new FinancialAdviceCategoryClassifier(taxonomy);
        var freshnessEvaluator = new InsightFreshnessEvaluator(adviceOptions, new InsightInvalidationHintBuilder());
        var findingFactory = new FinancialAdviceFindingFactory(freshnessEvaluator);
        var adviceEngine = new FinancialAdviceEngine(
            new CompanionProfileBaselineBuilder(),
            findingFactory,
            new CategoryPressureEvaluator(taxonomy, categoryClassifier, findingFactory, adviceOptions),
            new RecurringSpendEvaluator(categoryClassifier, findingFactory, adviceOptions),
            new BudgetHealthEvaluator(findingFactory, adviceOptions),
            new AffordabilityEvaluator(findingFactory, adviceOptions),
            new PlanDriftEvaluator(findingFactory),
            new PositiveSignalEvaluator(findingFactory));
        var advicePolicy = new FinancialAdvicePolicyService(
            new ProtectedPreferenceHintParser(),
            new ProtectedCategoryPolicy(),
            new ReductionSafetyPolicy(),
            new ConfidenceAdjustmentPolicy(),
            new FindingRejectionPolicy());
        var adviceAdjudication = new FinancialAdviceAdjudicationService(
            new StubModelRouter(),
            new StubAIClient(),
            new AdjudicationPromptBuilder(),
            new AdjudicationInputSanitizer(),
            new AdjudicationResultParser(),
            new AdjudicationResultValidator(),
            adviceOptions);
        var adviceDecision = new FinancialAdviceDecisionService(
            adviceEngine,
            advicePolicy,
            adviceAdjudication,
            new FinancialAdviceAdjudicationPlanSelector(adviceOptions),
            new AdviceEvidenceSummaryBuilder(),
            new AdvicePacketBuilder(
                new AdviceLifecycleMetadataBuilder(),
                new AdviceSummaryBuilder()),
            adviceOptions);
        return new FinancialCompanionService(
            dbContext,
            tools,
            new CompanionIntentRouter(NullLogger<CompanionIntentRouter>.Instance),
            assembler,
            adviceDecision,
            Options.Create(new CompanionAISettingsOptions
            {
                Enabled = true,
                MaxTokensPerResponse = 900,
                DailySoftCapPerUser = dailySoftCap,
                EnforceDailySoftCap = enforceSoftCap,
                PreferredModelClass = AIModelClass.HeavyReasoning,
                SoftCapFallbackModelClass = AIModelClass.Fast
            }),
            NullLogger<FinancialCompanionService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"financial-companion-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedUser(AppDbContext dbContext, Guid userId)
    {
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = $"{userId:N}@example.com",
            NormalizedEmail = $"{userId:N}@example.com",
            DisplayName = "Test User",
            FullName = "Test User",
            Role = "user",
            Status = "active",
            OnboardingStatus = "completed",
            Timezone = "UTC",
            Locale = "en-GB",
            PreferredCurrency = "EUR",
            PlanTier = "standard",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            CountryRegion = "IE"
        });
        dbContext.SaveChanges();
    }

    private sealed class StubModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return preferredModelClass == AIModelClass.Fast
                ? new AIModelRoute(taskType, preferredModelClass, "fast-model", "fast-model", false, "fast", [])
                : new AIModelRoute(taskType, preferredModelClass, "heavy-model", "heavy-model", false, "heavy", []);
        }
    }

    private sealed class StubAIClient : IAIClient
    {
        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = """
                          {
                            "outcome": "approve",
                            "summaryRefinement": "Grounded guidance prepared from deterministic evidence.",
                            "adjustments": [],
                            "warnings": [],
                            "rationale": "deterministic findings are coherent"
                          }
                          """;
            return Task.FromResult(
                new AIResponse(
                    Content: payload,
                    StructuredPayloadJson: payload,
                    FinishReason: "stop",
                    Provider: "Mock",
                    Model: route.Model,
                    Deployment: route.Deployment,
                    InputTokenEstimate: 40,
                    OutputTokenEstimate: 60,
                    LatencyMs: 5,
                    WasMocked: true,
                    RawDiagnostics: null,
                    Succeeded: true,
                    FailureReason: null));
        }
    }

    private sealed class TrackingTools :
        IUserFinancialContextProfileService,
        IUserFinancialSummaryService,
        ISpendingAnalysisService,
        IRecurringObligationsService,
        IBudgetStatusService,
        ITransactionQueryService,
        IPlacesSearchService,
        IPlaceDetailsService,
        IReviewInsightsService
    {
        public bool FailBudgetStatus { get; set; }

        public int SummaryCalls { get; private set; }
        public int BudgetCalls { get; private set; }
        public int SpendingCalls { get; private set; }
        public int RecurringCalls { get; private set; }

        public Task<UserFinancialContextSnapshot> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new UserFinancialContextSnapshot(
                    Country: "IE",
                    Currency: "EUR",
                    MonthlyIncomeRange: "2000-4000",
                    KnownObligationsJson: "[]",
                    BudgetStructureJson: "{}",
                    ActivePlansJson: "[]",
                    SpendingTendenciesJson: "[]",
                    CategoryFlexibilityMarkersJson: "[]",
                    AdviceStylePreference: "balanced"));
        }

        public Task<UserFinancialSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SummaryCalls += 1;
            return Task.FromResult(new UserFinancialSummary(2500m, 1800m, 700m, "EUR"));
        }

        public Task<SpendingAnalysisResult> AnalyzeAsync(Guid userId, int lookbackDays, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpendingCalls += 1;
            return Task.FromResult(
                new SpendingAnalysisResult(
                    SpendByDomain: new Dictionary<int, decimal> { [130] = 480m, [210] = 120m },
                    AverageDailySpend: 26m,
                    LargestExpense: 140m));
        }

        public Task<RecurringObligationsResult> GetRecurringAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecurringCalls += 1;
            return Task.FromResult(
                new RecurringObligationsResult(
                    [new RecurringObligationItem("Rent", 900m, "EUR", 30)],
                    900m));
        }

        public Task<BudgetStatusResult> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailBudgetStatus)
            {
                throw new InvalidOperationException("Budget service unavailable");
            }

            BudgetCalls += 1;
            return Task.FromResult(new BudgetStatusResult(true, 1800m, 1200m, 600m));
        }

        public Task<TransactionQueryResult> QueryAsync(Guid userId, string query, int maxRows, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TransactionQueryResult([]));
        }

        public Task<PlaceSearchResult> SearchAsync(string query, string country, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PlaceSearchResult([]));
        }

        public Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PlaceDetailsResult(placeId, "Test Place", null, null, null));
        }

        public Task<ReviewInsightsResult> GetInsightsAsync(string placeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ReviewInsightsResult(placeId, "No review data.", null));
        }
    }
}
