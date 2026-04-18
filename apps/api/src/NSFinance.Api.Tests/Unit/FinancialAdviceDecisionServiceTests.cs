using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialAdviceDecisionServiceTests
{
    [Fact]
    public async Task DecideAsync_IncludesLifecycleMetadataEvidenceWindowAndRefreshHints()
    {
        var now = DateTime.UtcNow;
        var finding = BuildFinding(
            id: "finding_high",
            type: FinancialAdviceFindingType.BudgetSlippage,
            severity: FinancialAdviceSeverity.High,
            priority: 90,
            confidence: 0.82d,
            freshness: new FinancialAdviceFreshnessMetadata(
                ComputedAtUtc: now,
                EvidencePeriodStartUtc: now.AddDays(-30),
                EvidencePeriodEndUtc: now,
                FreshUntilUtc: now.AddHours(12),
                RecheckAfterUtc: now.AddHours(6),
                FreshnessState: FinancialAdviceFreshnessState.Fresh,
                ConfidenceDecayPerDay: 0.4d,
                RelevanceScore: 0.9d,
                RequiresRecheck: true,
                InvalidationHints: ["budget_state_materially_changed", "stale_age_exceeded"]));
        var sut = CreateSut(
            findings: [finding],
            adjudicationResult: new FinancialAdviceAdjudicationResult(
                UsedAi: false,
                Succeeded: true,
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                ModelUsed: "deterministic_only",
                InputTokens: 0,
                OutputTokens: 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: ["adjudication_skip_high_confidence_deterministic"]));

        var result = await sut.DecideAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "session-1", "How is my budget?"),
            CreateRouting(FinancialCompanionIntent.BudgetStatus),
            CreateContext(),
            AIModelClass.Fast,
            CancellationToken.None);

        Assert.Equal(now.AddDays(-30).Date, result.Packet.EvidenceWindow.StartUtc.Date);
        Assert.True(result.Packet.RequiresRefresh);
        Assert.Contains("budget_state_materially_changed", result.Packet.RefreshHints);
        Assert.Contains("stale_age_exceeded", result.Packet.RefreshHints);
        Assert.NotEmpty(result.Packet.FinalInsights);
        Assert.NotEmpty(result.Packet.EvidenceSummary);
    }

    [Fact]
    public async Task DecideAsync_ProvidesPriorityOrderedInsightsForKeyInsightReuse()
    {
        var insights = new[]
        {
            BuildFinding("finding_low", FinancialAdviceFindingType.PositiveProgress, FinancialAdviceSeverity.Low, 45, 0.75d),
            BuildFinding("finding_high", FinancialAdviceFindingType.AffordabilityRisk, FinancialAdviceSeverity.High, 88, 0.8d)
        };
        var sut = CreateSut(
            findings: insights,
            adjudicationResult: new FinancialAdviceAdjudicationResult(
                UsedAi: false,
                Succeeded: true,
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                ModelUsed: "deterministic_only",
                InputTokens: 0,
                OutputTokens: 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: []));

        var result = await sut.DecideAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "session-2", "What should I focus on?"),
            CreateRouting(FinancialCompanionIntent.GeneralFinancialQuestion),
            CreateContext(),
            AIModelClass.Fast,
            CancellationToken.None);

        Assert.Equal("finding_high", result.Packet.FinalInsights[0].FindingId);
        Assert.True(result.Packet.FinalInsights[0].PriorityScore >= result.Packet.FinalInsights[1].PriorityScore);
        Assert.False(string.IsNullOrWhiteSpace(result.Packet.UserSafeSummary));
    }

    [Fact]
    public async Task DecideAsync_AdjudicationOutcomesCanRejectOrLowerConfidenceWithoutAddingFindings()
    {
        var findingOne = BuildFinding("finding_1", FinancialAdviceFindingType.AffordabilityRisk, FinancialAdviceSeverity.High, 85, 0.8d);
        var findingTwo = BuildFinding("finding_2", FinancialAdviceFindingType.BudgetSlippage, FinancialAdviceSeverity.Moderate, 70, 0.7d);
        var sut = CreateSut(
            findings: [findingOne, findingTwo],
            adjudicationResult: new FinancialAdviceAdjudicationResult(
                UsedAi: true,
                Succeeded: true,
                Mode: FinancialAdviceAdjudicationMode.Required,
                ModelUsed: "mock-model",
                InputTokens: 45,
                OutputTokens: 40,
                ResponseSummary: null,
                FindingOutcomes:
                [
                    new FinancialAdviceFindingAdjudication("finding_1", FinancialAdviceAdjudicationOutcome.RejectConclusion, null, null, [], "reject"),
                    new FinancialAdviceFindingAdjudication("finding_2", FinancialAdviceAdjudicationOutcome.LowerConfidence, null, -0.20d, [], "lower"),
                    new FinancialAdviceFindingAdjudication("finding_999", FinancialAdviceAdjudicationOutcome.Approve, null, null, [], "unknown")
                ],
                Warnings: []));

        var result = await sut.DecideAsync(
            new FinancialCompanionRequest(Guid.NewGuid(), "session-3", "Can I afford this?"),
            CreateRouting(FinancialCompanionIntent.Affordability),
            CreateContext(),
            AIModelClass.HeavyReasoning,
            CancellationToken.None);

        Assert.DoesNotContain(result.Packet.FinalInsights, item => item.FindingId == "finding_1");
        var lowered = Assert.Single(result.Packet.FinalInsights, item => item.FindingId == "finding_2");
        Assert.True(lowered.Confidence < 0.7d);
        Assert.DoesNotContain(result.Packet.FinalInsights, item => item.FindingId == "finding_999");
    }

    private static FinancialAdviceDecisionService CreateSut(
        IReadOnlyList<FinancialAdviceFinding> findings,
        FinancialAdviceAdjudicationResult adjudicationResult)
    {
        return new FinancialAdviceDecisionService(
            new StubAdviceEngine(findings),
            new StubPolicyService(findings),
            new StubAdjudicationService(adjudicationResult),
            Options.Create(new CompanionAdviceOptions()));
    }

    private static CompanionIntentRoutingResult CreateRouting(FinancialCompanionIntent intent)
    {
        return new CompanionIntentRoutingResult(
            IntentFamily: intent,
            PrimaryIntent: intent,
            SecondaryIntents: [],
            Confidence: 0.8d,
            ReasonCodes: [],
            IsAmbiguous: false,
            IsUnsupported: false);
    }

    private static FinancialCompanionContext CreateContext()
    {
        var profile = new UserFinancialContextSnapshot(
            Country: "IE",
            Currency: "EUR",
            MonthlyIncomeRange: "2000-4000",
            KnownObligationsJson: "[]",
            BudgetStructureJson: "{}",
            ActivePlansJson: "[]",
            SpendingTendenciesJson: "[]",
            CategoryFlexibilityMarkersJson: "[]",
            AdviceStylePreference: "balanced");
        return new FinancialCompanionContext(
            Intent: FinancialCompanionIntent.GeneralFinancialQuestion,
            Profile: profile,
            ToolOutputs: new Dictionary<string, object?>(),
            ToolsUsed: [],
            Evidence: null);
    }

    private static FinancialAdviceFinding BuildFinding(
        string id,
        FinancialAdviceFindingType type,
        FinancialAdviceSeverity severity,
        int priority,
        double confidence,
        FinancialAdviceFreshnessMetadata? freshness = null)
    {
        var now = DateTime.UtcNow;
        return new FinancialAdviceFinding(
            FindingId: id,
            FindingType: type,
            RelatedIntent: FinancialCompanionIntent.GeneralFinancialQuestion,
            Severity: severity,
            PriorityScore: priority,
            Confidence: confidence,
            EvidenceSummary: $"{type} evidence",
            SupportingMetrics: [new FinancialAdviceEvidenceMetric("metric", 1m, "count")],
            DomainCode: null,
            DomainName: null,
            CategoryCode: null,
            CategoryName: null,
            ProtectedCategoryFlags: [],
            RecommendedActions:
            [
                new FinancialAdviceActionCandidate(
                    ActionId: $"action_{id}",
                    ActionType: FinancialAdviceActionType.ReviewSpend,
                    Title: "Review",
                    Guidance: "Review this finding")
            ],
            UncertaintyMarkers: [],
            PolicyWarnings: [],
            PolicyExclusions: [],
            AiAdjudicationAllowed: true,
            AiAdjudicationRecommended: true,
            Freshness: freshness ?? new FinancialAdviceFreshnessMetadata(
                ComputedAtUtc: now,
                EvidencePeriodStartUtc: now.AddDays(-15),
                EvidencePeriodEndUtc: now,
                FreshUntilUtc: now.AddHours(24),
                RecheckAfterUtc: now.AddHours(12),
                FreshnessState: FinancialAdviceFreshnessState.Fresh,
                ConfidenceDecayPerDay: 0.4d,
                RelevanceScore: 0.8d,
                RequiresRecheck: true,
                InvalidationHints: ["stale_age_exceeded"]),
            RenderingHints: new Dictionary<string, string> { ["surface"] = "key_insight_or_chat" });
    }

    private sealed class StubAdviceEngine(IReadOnlyList<FinancialAdviceFinding> findings) : IFinancialAdviceEngine
    {
        public IReadOnlyList<FinancialAdviceFinding> ComputeDeterministicFindings(
            FinancialCompanionRequest request,
            CompanionIntentRoutingResult routing,
            FinancialCompanionContext context,
            DateTime nowUtc)
        {
            return findings;
        }
    }

    private sealed class StubPolicyService(IReadOnlyList<FinancialAdviceFinding> findings) : IFinancialAdvicePolicyService
    {
        public IReadOnlyList<FinancialAdvicePolicyReviewedFinding> ApplyPolicy(
            FinancialCompanionContext context,
            IReadOnlyList<FinancialAdviceFinding> deterministicFindings)
        {
            return findings.Select(finding => new FinancialAdvicePolicyReviewedFinding(
                Finding: finding,
                Decision: FinancialAdvicePolicyDecision.Approved,
                Warnings: [],
                Exclusions: [])).ToArray();
        }
    }

    private sealed class StubAdjudicationService(FinancialAdviceAdjudicationResult result) : IFinancialAdviceAdjudicationService
    {
        public Task<FinancialAdviceAdjudicationResult> AdjudicateAsync(
            FinancialAdviceAdjudicationExecutionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
