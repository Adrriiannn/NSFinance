using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialCompanionContextAssemblerTests
{
    private static readonly UserFinancialContextSnapshot DefaultProfile = new(
        Country: "IE",
        Currency: "EUR",
        MonthlyIncomeRange: "2000-4000",
        KnownObligationsJson: "[]",
        BudgetStructureJson: "{}",
        ActivePlansJson: "[]",
        SpendingTendenciesJson: "[]",
        CategoryFlexibilityMarkersJson: "[]",
        AdviceStylePreference: "balanced");

    [Fact]
    public async Task Assemble_BudgetStatus_UsesExpectedToolsOnly()
    {
        var tools = new TrackingTools();
        var sut = CreateAssembler(tools);
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.BudgetStatus,
            PrimaryIntent: FinancialCompanionIntent.BudgetStatus,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: ["signal_budget_phrase"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var result = await sut.AssembleAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "s1", "How much budget do I have left?"),
            routing,
            DefaultProfile,
            CancellationToken.None);

        Assert.True(result.CanProceedToAI);
        Assert.Contains("IUserFinancialSummaryService", result.ToolsUsed);
        Assert.Contains("IBudgetStatusService", result.ToolsUsed);
        Assert.DoesNotContain("IPlacesSearchService", result.ToolsUsed);
        Assert.Equal(1, tools.SummaryCalls);
        Assert.Equal(1, tools.BudgetCalls);
    }

    [Fact]
    public async Task Assemble_Mixed_AffordabilityAndPlaces_MergesBoundedWithoutDuplicates()
    {
        var tools = new TrackingTools();
        var sut = CreateAssembler(tools);
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.MixedQuery,
            PrimaryIntent: FinancialCompanionIntent.Affordability,
            SecondaryIntents: [FinancialCompanionIntent.LocalPlacesOutings],
            Confidence: 0.72d,
            ReasonCodes: ["mixed_query_detected"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var result = await sut.AssembleAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "s2", "Can I afford to go out and where should I go nearby?"),
            routing,
            DefaultProfile,
            CancellationToken.None);

        Assert.True(result.CanProceedToAI);
        Assert.Contains("IUserFinancialSummaryService", result.ToolsUsed);
        Assert.Contains("IBudgetStatusService", result.ToolsUsed);
        Assert.Contains("ITransactionQueryService", result.ToolsUsed);
        Assert.Contains("IPlacesSearchService", result.ToolsUsed);
        Assert.Equal(result.ToolsUsed.Count, result.ToolsUsed.Distinct(StringComparer.Ordinal).Count());
        Assert.True(result.ToolsUsed.Count <= 6);
    }

    [Fact]
    public async Task Assemble_Ambiguous_SkipsBroadToolGathering()
    {
        var tools = new TrackingTools();
        var sut = CreateAssembler(tools);
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.Ambiguous,
            PrimaryIntent: FinancialCompanionIntent.Ambiguous,
            SecondaryIntents: [],
            Confidence: 0.3d,
            ReasonCodes: ["ambiguous_generic_money_question"],
            IsAmbiguous: true,
            IsUnsupported: false);

        var result = await sut.AssembleAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "s3", "What should I do?"),
            routing,
            DefaultProfile,
            CancellationToken.None);

        Assert.False(result.CanProceedToAI);
        Assert.True(result.HasInsufficientData);
        Assert.Empty(result.ToolsUsed);
        Assert.Equal(0, tools.SummaryCalls);
        Assert.Contains("ambiguous_query_requires_clarification", result.InsufficientDataReasons);
    }

    [Fact]
    public async Task Assemble_MissingRequiredTool_ReturnsInsufficientState()
    {
        var tools = new TrackingTools
        {
            FailSummary = true
        };
        var sut = CreateAssembler(tools);
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.BudgetStatus,
            PrimaryIntent: FinancialCompanionIntent.BudgetStatus,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: ["signal_budget_phrase"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var result = await sut.AssembleAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "s4", "How is my budget?"),
            routing,
            DefaultProfile,
            CancellationToken.None);

        Assert.False(result.CanProceedToAI);
        Assert.True(result.HasInsufficientData);
        Assert.Contains("IUserFinancialSummaryService", result.Evidence.MissingRequiredTools);
        Assert.Contains("missing_required_financial_summary", result.InsufficientDataReasons);
    }

    [Fact]
    public async Task Assemble_TransactionQuery_BoundsRows()
    {
        var tools = new TrackingTools
        {
            TransactionItemCount = 30
        };
        var sut = CreateAssembler(tools);
        var routing = new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.Affordability,
            PrimaryIntent: FinancialCompanionIntent.Affordability,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: ["signal_affordability_phrase"],
            IsAmbiguous: false,
            IsUnsupported: false);

        var result = await sut.AssembleAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "s5", "Can I afford this?"),
            routing,
            DefaultProfile,
            CancellationToken.None);

        Assert.True(result.Context.ToolOutputs.TryGetValue("transaction_matches", out var txObj));
        var tx = Assert.IsType<CompanionTransactionMatchesContext>(txObj);
        Assert.Equal(30, tx.TotalItemCount);
        Assert.True(tx.Items.Count <= 8);
    }

    private static FinancialCompanionContextAssembler CreateAssembler(TrackingTools tools)
    {
        var orchestrationOptions = Microsoft.Extensions.Options.Options.Create(new CompanionOrchestrationOptions());
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
        return new FinancialCompanionContextAssembler(
            planBuilder,
            toolExecutor,
            contextShaper,
            insufficiencyEvaluator,
            evidenceBuilder);
    }

    private sealed class TrackingTools :
        IUserFinancialSummaryService,
        ISpendingAnalysisService,
        IRecurringObligationsService,
        IBudgetStatusService,
        ITransactionQueryService,
        IPlacesSearchService,
        IPlaceDetailsService,
        IReviewInsightsService
    {
        public bool FailSummary { get; set; }
        public int TransactionItemCount { get; set; }

        public int SummaryCalls { get; private set; }
        public int BudgetCalls { get; private set; }

        public Task<UserFinancialSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSummary)
            {
                throw new InvalidOperationException("summary service failed");
            }

            SummaryCalls += 1;
            return Task.FromResult(new UserFinancialSummary(2500m, 1800m, 700m, "EUR"));
        }

        public Task<SpendingAnalysisResult> AnalyzeAsync(Guid userId, int lookbackDays, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new SpendingAnalysisResult(
                    new Dictionary<int, decimal> { [130] = 480m, [210] = 120m, [220] = 95m },
                    26m,
                    140m));
        }

        public Task<RecurringObligationsResult> GetRecurringAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new RecurringObligationsResult(
                    [new RecurringObligationItem("Rent", 900m, "EUR", 30)],
                    900m));
        }

        public Task<BudgetStatusResult> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BudgetCalls += 1;
            return Task.FromResult(new BudgetStatusResult(true, 1800m, 1200m, 600m));
        }

        public Task<TransactionQueryResult> QueryAsync(Guid userId, string query, int maxRows, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Max(TransactionItemCount, maxRows);
            var rows = Enumerable.Range(0, count)
                .Select(i => new TransactionQueryItem(
                    BookedAtUtc: DateTime.UtcNow.Date.AddDays(-i),
                    Amount: 20m + i,
                    Currency: "EUR",
                    Description: $"Txn {i}",
                    DomainCode: 130,
                    CategoryCode: 130100))
                .ToArray();
            return Task.FromResult(new TransactionQueryResult(rows));
        }

        public Task<PlaceSearchResult> SearchAsync(string query, string country, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new PlaceSearchResult(
                [
                    new PlaceSearchItem("p1", "Cafe One", "Cafe", "2"),
                    new PlaceSearchItem("p2", "Bistro Two", "Restaurant", "2")
                ]));
        }

        public Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PlaceDetailsResult(placeId, "Cafe One", "Street 1", "https://cafe.example", "2"));
        }

        public Task<ReviewInsightsResult> GetInsightsAsync(string placeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ReviewInsightsResult(placeId, "Great value and quick service.", 4.4));
        }
    }
}
