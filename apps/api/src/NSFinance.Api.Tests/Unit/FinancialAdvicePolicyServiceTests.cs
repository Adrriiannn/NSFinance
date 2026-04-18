using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialAdvicePolicyServiceTests
{
    private readonly FinancialAdvicePolicyService _sut = new();

    [Fact]
    public void ApplyPolicy_ProtectedCategoryCutRecommendation_IsExcluded()
    {
        var finding = BuildFinding(
            confidence: 0.85d,
            protectedFlags: ["protected_or_essential_domain"],
            actions:
            [
                new FinancialAdviceActionCandidate(
                    ActionId: "cut_housing",
                    ActionType: FinancialAdviceActionType.ReduceSpend,
                    Title: "Reduce housing spend",
                    Guidance: "Cut housing by 15%",
                    SuggestedMagnitude: 0.15m,
                    TargetDomainCode: 100,
                    IsProtectedCategory: true)
            ]);

        var reviewed = _sut.ApplyPolicy(CreateContext(), [finding]);

        var item = Assert.Single(reviewed);
        Assert.Equal(FinancialAdvicePolicyDecision.ApprovedWithAdjustments, item.Decision);
        Assert.Empty(item.Finding.RecommendedActions);
        Assert.Contains("protected_category_reduction_blocked", item.Exclusions);
    }

    [Fact]
    public void ApplyPolicy_WeakEvidenceAndAggressiveReduction_BlocksUnsafeActionAndDowngrades()
    {
        var finding = BuildFinding(
            confidence: 0.40d,
            severity: FinancialAdviceSeverity.High,
            actions:
            [
                new FinancialAdviceActionCandidate(
                    ActionId: "aggressive_cut",
                    ActionType: FinancialAdviceActionType.ReduceSpend,
                    Title: "Cut heavily",
                    Guidance: "Cut by 30%",
                    SuggestedMagnitude: 0.30m,
                    TargetDomainCode: 220)
            ]);

        var reviewed = _sut.ApplyPolicy(CreateContext(), [finding]);

        var item = Assert.Single(reviewed);
        Assert.Equal(FinancialAdvicePolicyDecision.Rejected, item.Decision);
        Assert.Contains(item.Warnings, warning => warning.Contains("severity_downgraded_due_to_weak_evidence", StringComparison.Ordinal));
        Assert.Contains("aggressive_reduction_blocked_without_strong_evidence", item.Exclusions);
    }

    [Fact]
    public void ApplyPolicy_UnsupportedAction_IsRejectedByScopeGuardrail()
    {
        var finding = BuildFinding(
            confidence: 0.8d,
            actions:
            [
                new FinancialAdviceActionCandidate(
                    ActionId: "unsupported",
                    ActionType: (FinancialAdviceActionType)999,
                    Title: "Unsupported",
                    Guidance: "Out of scope")
            ]);

        var reviewed = _sut.ApplyPolicy(CreateContext(), [finding]);

        var item = Assert.Single(reviewed);
        Assert.Contains("unsupported_action_outside_current_scope", item.Exclusions);
        Assert.Equal(FinancialAdvicePolicyDecision.ApprovedWithAdjustments, item.Decision);
    }

    [Fact]
    public void ApplyPolicy_ProfileProtectedPreferenceConflict_BlocksReductionAction()
    {
        var context = CreateContext(categoryFlexibilityMarkersJson: """["130","protected"]""");
        var finding = BuildFinding(
            confidence: 0.82d,
            domainCode: 130,
            actions:
            [
                new FinancialAdviceActionCandidate(
                    ActionId: "cut_domain_130",
                    ActionType: FinancialAdviceActionType.ReduceSpend,
                    Title: "Reduce category",
                    Guidance: "Reduce category spend",
                    SuggestedMagnitude: 0.10m,
                    TargetDomainCode: 130)
            ]);

        var reviewed = _sut.ApplyPolicy(context, [finding]);

        var item = Assert.Single(reviewed);
        Assert.Empty(item.Finding.RecommendedActions);
        Assert.Contains("profile_protected_preference_conflict", item.Exclusions);
    }

    private static FinancialCompanionContext CreateContext(string categoryFlexibilityMarkersJson = "[]")
    {
        var profile = new UserFinancialContextSnapshot(
            Country: "IE",
            Currency: "EUR",
            MonthlyIncomeRange: "2000-4000",
            KnownObligationsJson: "[]",
            BudgetStructureJson: "{}",
            ActivePlansJson: "[]",
            SpendingTendenciesJson: "[]",
            CategoryFlexibilityMarkersJson: categoryFlexibilityMarkersJson,
            AdviceStylePreference: "balanced");
        return new FinancialCompanionContext(
            Intent: FinancialCompanionIntent.SavingsCutbackAdvice,
            Profile: profile,
            ToolOutputs: new Dictionary<string, object?>(),
            ToolsUsed: [],
            Evidence: null);
    }

    private static FinancialAdviceFinding BuildFinding(
        double confidence,
        FinancialAdviceSeverity severity = FinancialAdviceSeverity.Moderate,
        int? domainCode = null,
        IReadOnlyList<string>? protectedFlags = null,
        IReadOnlyList<FinancialAdviceActionCandidate>? actions = null)
    {
        return new FinancialAdviceFinding(
            FindingId: "finding_1",
            FindingType: FinancialAdviceFindingType.DiscretionaryOverspend,
            RelatedIntent: FinancialCompanionIntent.SavingsCutbackAdvice,
            Severity: severity,
            PriorityScore: 80,
            Confidence: confidence,
            EvidenceSummary: "Sample evidence",
            SupportingMetrics: [new FinancialAdviceEvidenceMetric("domainSpendRatio", 1.6m, "ratio")],
            DomainCode: domainCode,
            DomainName: "Sample Domain",
            CategoryCode: null,
            CategoryName: null,
            ProtectedCategoryFlags: protectedFlags ?? [],
            RecommendedActions: actions ?? [],
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
    }
}
