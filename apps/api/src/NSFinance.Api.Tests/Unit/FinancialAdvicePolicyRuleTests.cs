using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialAdvicePolicyRuleTests
{
    [Fact]
    public void ProtectedCategoryPolicy_BlocksReductionForProtectedFinding()
    {
        var policy = new ProtectedCategoryPolicy();
        var finding = BuildFinding(confidence: 0.8d, protectedFlags: ["protected_or_essential_domain"]);
        var action = new FinancialAdviceActionCandidate(
            ActionId: "reduce",
            ActionType: FinancialAdviceActionType.ReduceSpend,
            Title: "Reduce",
            Guidance: "Reduce by 10%",
            SuggestedMagnitude: 0.10m,
            IsProtectedCategory: true);
        var state = new FinancialAdvicePolicyEvaluationState(finding);
        var context = new FinancialAdvicePolicyActionContext(finding, action, []);

        var allowed = policy.CanApplyAction(context, state);

        Assert.False(allowed);
        Assert.Contains("protected_category_reduction_blocked", state.Exclusions);
    }

    [Fact]
    public void ReductionSafetyPolicy_BlocksAggressiveCutWhenConfidenceIsWeak()
    {
        var policy = new ReductionSafetyPolicy();
        var finding = BuildFinding(confidence: 0.6d);
        var action = new FinancialAdviceActionCandidate(
            ActionId: "reduce",
            ActionType: FinancialAdviceActionType.ReduceSpend,
            Title: "Reduce",
            Guidance: "Reduce by 30%",
            SuggestedMagnitude: 0.30m);
        var state = new FinancialAdvicePolicyEvaluationState(finding);
        var context = new FinancialAdvicePolicyActionContext(finding, action, []);

        var allowed = policy.CanApplyAction(context, state);

        Assert.False(allowed);
        Assert.Contains("aggressive_reduction_blocked_without_strong_evidence", state.Exclusions);
    }

    [Fact]
    public void ConfidenceAdjustmentPolicy_CapsConfidenceAndLowersSeverity()
    {
        var policy = new ConfidenceAdjustmentPolicy();
        var finding = BuildFinding(
            confidence: 0.9d,
            severity: FinancialAdviceSeverity.High,
            uncertainty: ["missing_baseline"]);
        var state = new FinancialAdvicePolicyEvaluationState(finding)
        {
            Confidence = 0.4d
        };

        policy.Apply(state);

        Assert.Equal(FinancialAdviceSeverity.Moderate, state.Severity);
        Assert.Contains("severity_downgraded_due_to_weak_evidence", state.Warnings);
    }

    [Fact]
    public void FindingRejectionPolicy_RejectsWeakFindingWithoutSafeActions()
    {
        var policy = new FindingRejectionPolicy();
        var finding = BuildFinding(
            confidence: 0.5d,
            type: FinancialAdviceFindingType.BudgetSlippage,
            severity: FinancialAdviceSeverity.Moderate,
            actions:
            [
                new FinancialAdviceActionCandidate(
                    ActionId: "reduce",
                    ActionType: FinancialAdviceActionType.ReduceSpend,
                    Title: "Reduce",
                    Guidance: "Reduce by 10%")
            ]);
        var state = new FinancialAdvicePolicyEvaluationState(finding)
        {
            Confidence = 0.5d
        };

        policy.Apply(state);

        Assert.Equal(FinancialAdvicePolicyDecision.Rejected, state.Decision);
        Assert.False(state.AiAdjudicationAllowed);
        Assert.Contains("finding_rejected_policy_insufficient_safe_actions", state.Warnings);
    }

    private static FinancialAdviceFinding BuildFinding(
        double confidence,
        FinancialAdviceFindingType type = FinancialAdviceFindingType.DiscretionaryOverspend,
        FinancialAdviceSeverity severity = FinancialAdviceSeverity.Moderate,
        IReadOnlyList<string>? uncertainty = null,
        IReadOnlyList<string>? protectedFlags = null,
        IReadOnlyList<FinancialAdviceActionCandidate>? actions = null)
    {
        var now = DateTime.UtcNow;
        return new FinancialAdviceFinding(
            FindingId: "finding",
            FindingType: type,
            RelatedIntent: FinancialCompanionIntent.SavingsCutbackAdvice,
            Severity: severity,
            PriorityScore: 80,
            Confidence: confidence,
            EvidenceSummary: "Evidence",
            SupportingMetrics: [new FinancialAdviceEvidenceMetric("metric", 1m, "count")],
            DomainCode: 130,
            DomainName: "Dining",
            CategoryCode: null,
            CategoryName: null,
            ProtectedCategoryFlags: protectedFlags ?? [],
            RecommendedActions: actions ?? [],
            UncertaintyMarkers: uncertainty ?? [],
            PolicyWarnings: [],
            PolicyExclusions: [],
            AiAdjudicationAllowed: true,
            AiAdjudicationRecommended: true,
            Freshness: new FinancialAdviceFreshnessMetadata(
                ComputedAtUtc: now,
                EvidencePeriodStartUtc: now.AddDays(-30),
                EvidencePeriodEndUtc: now,
                FreshUntilUtc: now.AddHours(24),
                RecheckAfterUtc: now.AddHours(12),
                FreshnessState: FinancialAdviceFreshnessState.Fresh,
                ConfidenceDecayPerDay: 0.5d,
                RelevanceScore: 0.8d,
                RequiresRecheck: true,
                InvalidationHints: ["stale_age_exceeded"]),
            RenderingHints: new Dictionary<string, string> { ["surface"] = "key_insight_or_chat" });
    }
}
