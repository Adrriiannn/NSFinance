using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialAdviceAdjudicationServiceTests
{
    [Fact]
    public async Task AdjudicateAsync_BuildsBoundedStructuredInputAndReturnsExplicitOutcomeContract()
    {
        var aiClient = new CapturingAIClient(
            """
            {
              "outcome": "approve",
              "summaryRefinement": "Use deterministic findings as guidance.",
              "adjustments": [],
              "warnings": [],
              "rationale": "ok"
            }
            """);
        var sut = CreateSut(aiClient);
        var request = CreateExecutionRequest(FinancialAdviceAdjudicationMode.Optional);

        var result = await sut.AdjudicateAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.UsedAi);
        Assert.Equal(FinancialAdviceAdjudicationMode.Optional, result.Mode);
        Assert.NotNull(aiClient.LastRequest);
        Assert.Contains("AdjudicationInputJson", aiClient.LastRequest!.Messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolContextJson", aiClient.LastRequest.Messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("transaction_matches", aiClient.LastRequest.Messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.All(result.FindingOutcomes, outcome => Assert.Equal(FinancialAdviceAdjudicationOutcome.Approve, outcome.Outcome));
    }

    [Fact]
    public async Task AdjudicateAsync_LowerConfidenceAndRejectPaths_AreMappedCorrectly()
    {
        var aiClient = new CapturingAIClient(
            """
            {
              "outcome": "approve",
              "summaryRefinement": null,
              "adjustments": [
                {
                  "findingId": "finding_1",
                  "outcome": "lower_confidence",
                  "confidenceDelta": -0.2,
                  "refinedSummary": "Keep this directional only.",
                  "nuanceNotes": ["limited baseline"],
                  "reasonCode": "weak_signal"
                },
                {
                  "findingId": "finding_2",
                  "outcome": "reject_conclusion",
                  "confidenceDelta": null,
                  "refinedSummary": null,
                  "nuanceNotes": [],
                  "reasonCode": "unsupported"
                }
              ],
              "warnings": ["reviewed"],
              "rationale": "ok"
            }
            """);
        var sut = CreateSut(aiClient);
        var request = CreateExecutionRequest(FinancialAdviceAdjudicationMode.Required, findingCount: 2);

        var result = await sut.AdjudicateAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(result.FindingOutcomes, outcome =>
            outcome.FindingId == "finding_1"
            && outcome.Outcome == FinancialAdviceAdjudicationOutcome.LowerConfidence
            && outcome.ConfidenceDelta == -0.2d);
        Assert.Contains(result.FindingOutcomes, outcome =>
            outcome.FindingId == "finding_2"
            && outcome.Outcome == FinancialAdviceAdjudicationOutcome.RejectConclusion);
    }

    [Fact]
    public async Task AdjudicateAsync_UnknownFindingOrOutcome_IsSafelyHandled()
    {
        var aiClient = new CapturingAIClient(
            """
            {
              "outcome": "not_a_real_outcome",
              "summaryRefinement": null,
              "adjustments": [
                {
                  "findingId": "unknown",
                  "outcome": "approve",
                  "confidenceDelta": null,
                  "refinedSummary": null,
                  "nuanceNotes": [],
                  "reasonCode": null
                }
              ],
              "warnings": [],
              "rationale": "ok"
            }
            """);
        var sut = CreateSut(aiClient);
        var request = CreateExecutionRequest(FinancialAdviceAdjudicationMode.Optional);

        var result = await sut.AdjudicateAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("adjudication_unknown_finding_ignored", result.Warnings);
        Assert.All(result.FindingOutcomes, outcome =>
            Assert.Equal(FinancialAdviceAdjudicationOutcome.InsufficientEvidence, outcome.Outcome));
    }

    [Fact]
    public async Task AdjudicateAsync_SkippedMode_ReturnsDeterministicOnlyContract()
    {
        var aiClient = new CapturingAIClient("{}");
        var sut = CreateSut(aiClient);
        var request = CreateExecutionRequest(FinancialAdviceAdjudicationMode.Skipped);

        var result = await sut.AdjudicateAsync(request, CancellationToken.None);

        Assert.False(result.UsedAi);
        Assert.Equal("deterministic_only", result.ModelUsed);
        Assert.Empty(result.FindingOutcomes);
        Assert.Null(aiClient.LastRequest);
    }

    [Fact]
    public async Task AdjudicateAsync_RefinedSummaryWithUnsupportedNumbers_IsSanitized()
    {
        var aiClient = new CapturingAIClient(
            """
            {
              "outcome": "approve_with_refinement",
              "summaryRefinement": "You can save 9999 next month.",
              "adjustments": [
                {
                  "findingId": "finding_1",
                  "outcome": "approve_with_refinement",
                  "confidenceDelta": null,
                  "refinedSummary": "Spend changed by 9999.",
                  "nuanceNotes": [],
                  "reasonCode": null
                }
              ],
              "warnings": [],
              "rationale": "ok"
            }
            """);
        var sut = CreateSut(aiClient);
        var request = CreateExecutionRequest(FinancialAdviceAdjudicationMode.Optional);

        var result = await sut.AdjudicateAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.ResponseSummary);
        Assert.Contains(result.Warnings, warning => warning.Contains("sanitized", StringComparison.OrdinalIgnoreCase));
    }

    private static FinancialAdviceAdjudicationService CreateSut(CapturingAIClient aiClient)
    {
        var adviceOptions = Options.Create(new CompanionAdviceOptions
        {
            MaxAdjudicatedFindings = 3,
            MaxAdjudicationInputChars = 6_000,
            MaxAdjudicationOutputTokens = 400
        });

        return new FinancialAdviceAdjudicationService(
            new StubModelRouter(),
            aiClient,
            new AdjudicationPromptBuilder(),
            new AdjudicationInputSanitizer(),
            new AdjudicationResultParser(),
            new AdjudicationResultValidator(),
            adviceOptions);
    }

    private static FinancialAdviceAdjudicationExecutionRequest CreateExecutionRequest(
        FinancialAdviceAdjudicationMode mode,
        int findingCount = 1)
    {
        var reviewed = Enumerable.Range(1, findingCount)
            .Select(index =>
            {
                var finding = new FinancialAdviceFinding(
                    FindingId: $"finding_{index}",
                    FindingType: FinancialAdviceFindingType.AffordabilityRisk,
                    RelatedIntent: FinancialCompanionIntent.Affordability,
                    Severity: FinancialAdviceSeverity.High,
                    PriorityScore: 80,
                    Confidence: 0.72d,
                    EvidenceSummary: "Affordability room is narrow.",
                    SupportingMetrics:
                    [
                        new FinancialAdviceEvidenceMetric("affordabilityRoomToIncomeRatio", 0.08m, "ratio")
                    ],
                    DomainCode: null,
                    DomainName: null,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: [],
                    RecommendedActions:
                    [
                        new FinancialAdviceActionCandidate(
                            ActionId: $"action_{index}",
                            ActionType: FinancialAdviceActionType.BuildBuffer,
                            Title: "Build buffer",
                            Guidance: "Build affordability buffer.")
                    ],
                    UncertaintyMarkers: [],
                    PolicyWarnings: [],
                    PolicyExclusions: [],
                    AiAdjudicationAllowed: true,
                    AiAdjudicationRecommended: true,
                    Freshness: new FinancialAdviceFreshnessMetadata(
                        ComputedAtUtc: DateTime.UtcNow,
                        EvidencePeriodStartUtc: DateTime.UtcNow.AddDays(-30),
                        EvidencePeriodEndUtc: DateTime.UtcNow,
                        FreshUntilUtc: DateTime.UtcNow.AddHours(24),
                        RecheckAfterUtc: DateTime.UtcNow.AddHours(12),
                        FreshnessState: FinancialAdviceFreshnessState.Fresh,
                        ConfidenceDecayPerDay: 0.5d,
                        RelevanceScore: 0.8d,
                        RequiresRecheck: true,
                        InvalidationHints: ["stale_age_exceeded"]),
                    RenderingHints: new Dictionary<string, string> { ["surface"] = "key_insight_or_chat" });
                return new FinancialAdvicePolicyReviewedFinding(
                    Finding: finding,
                    Decision: FinancialAdvicePolicyDecision.Approved,
                    Warnings: [],
                    Exclusions: []);
            })
            .ToArray();

        return new FinancialAdviceAdjudicationExecutionRequest(
            UserQuery: "Can I afford this purchase?",
            Intent: FinancialCompanionIntent.Affordability,
            Profile: new UserFinancialContextSnapshot(
                Country: "IE",
                Currency: "EUR",
                MonthlyIncomeRange: "2000-4000",
                KnownObligationsJson: """[{"name":"Rent","amount":900}]""",
                BudgetStructureJson: "{}",
                ActivePlansJson: "[]",
                SpendingTendenciesJson: """{"averageDailySpend":22}""",
                CategoryFlexibilityMarkersJson: "[]",
                AdviceStylePreference: "balanced"),
            Plan: new FinancialAdviceAdjudicationPlan(
                Mode: mode,
                TargetFindingIds: reviewed.Select(item => item.Finding.FindingId).ToArray(),
                ReasonCodes: ["test_mode"]),
            PolicyReviewedFindings: reviewed,
            EvidenceSummary: ["affordabilityRoomToIncomeRatio:0.08:ratio"],
            PreferredModelClass: AIModelClass.Fast,
            CorrelationId: Guid.NewGuid().ToString("N"),
            Metadata: null);
    }

    private sealed class StubModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return new AIModelRoute(taskType, preferredModelClass, "mock-model", "mock-model", false, "mock", []);
        }
    }

    private sealed class CapturingAIClient(string payload) : IAIClient
    {
        public AIRequest? LastRequest { get; private set; }

        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AIResponse(
                Content: payload,
                StructuredPayloadJson: payload,
                FinishReason: "stop",
                Provider: "Mock",
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: 50,
                OutputTokenEstimate: 40,
                LatencyMs: 2,
                WasMocked: true,
                RawDiagnostics: null,
                Succeeded: true,
                FailureReason: null));
        }
    }
}
