namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdvicePolicyService
{
    IReadOnlyList<FinancialAdvicePolicyReviewedFinding> ApplyPolicy(
        FinancialCompanionContext context,
        IReadOnlyList<FinancialAdviceFinding> deterministicFindings);
}

public sealed class FinancialAdvicePolicyService(
    IProtectedPreferenceHintParser hintParser,
    IProtectedCategoryPolicy protectedCategoryPolicy,
    IReductionSafetyPolicy reductionSafetyPolicy,
    IConfidenceAdjustmentPolicy confidenceAdjustmentPolicy,
    IFindingRejectionPolicy findingRejectionPolicy) : IFinancialAdvicePolicyService
{
    public IReadOnlyList<FinancialAdvicePolicyReviewedFinding> ApplyPolicy(
        FinancialCompanionContext context,
        IReadOnlyList<FinancialAdviceFinding> deterministicFindings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deterministicFindings);

        var protectedPreferenceHints = hintParser.Parse(context.Profile.CategoryFlexibilityMarkersJson);
        var reviewed = new List<FinancialAdvicePolicyReviewedFinding>(deterministicFindings.Count);

        foreach (var finding in deterministicFindings)
        {
            var state = new FinancialAdvicePolicyEvaluationState(finding);
            EvaluateActions(finding, state, protectedPreferenceHints);
            confidenceAdjustmentPolicy.Apply(state);
            findingRejectionPolicy.Apply(state);

            var adjustedFinding = finding with
            {
                Severity = state.Severity,
                PriorityScore = FinancialAdvicePriorityScoring.Compute(state.Severity, state.Confidence),
                Confidence = state.Confidence,
                RecommendedActions = state.ApprovedActions.ToArray(),
                PolicyWarnings = finding.PolicyWarnings.Concat(state.Warnings).Distinct(StringComparer.Ordinal).ToArray(),
                PolicyExclusions = finding.PolicyExclusions.Concat(state.Exclusions).Distinct(StringComparer.Ordinal).ToArray(),
                AiAdjudicationAllowed = state.AiAdjudicationAllowed
            };

            reviewed.Add(
                new FinancialAdvicePolicyReviewedFinding(
                    Finding: adjustedFinding,
                    Decision: state.Decision,
                    Warnings: state.Warnings.ToArray(),
                    Exclusions: state.Exclusions.ToArray()));
        }

        return reviewed
            .OrderByDescending(item => item.Finding.PriorityScore)
            .ThenBy(item => item.Finding.FindingId, StringComparer.Ordinal)
            .ToArray();
    }

    private void EvaluateActions(
        FinancialAdviceFinding finding,
        FinancialAdvicePolicyEvaluationState state,
        IReadOnlyList<string> protectedPreferenceHints)
    {
        foreach (var action in finding.RecommendedActions)
        {
            var actionContext = new FinancialAdvicePolicyActionContext(
                Finding: finding,
                Action: action,
                ProtectedPreferenceHints: protectedPreferenceHints);

            if (!reductionSafetyPolicy.CanApplyAction(actionContext, state))
            {
                continue;
            }

            if (!protectedCategoryPolicy.CanApplyAction(actionContext, state))
            {
                continue;
            }

            state.ApprovedActions.Add(action);
        }
    }
}
