namespace NSFinance.Api.Modules.AI.Services;

public interface IFindingRejectionPolicy
{
    void Apply(FinancialAdvicePolicyEvaluationState state);
}

public sealed class FindingRejectionPolicy : IFindingRejectionPolicy
{
    public void Apply(FinancialAdvicePolicyEvaluationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var finding = state.OriginalFinding;
        if (state.ApprovedActions.Count == 0
            && finding.RecommendedActions.Count > 0
            && finding.FindingType is FinancialAdviceFindingType.DiscretionaryOverspend
                or FinancialAdviceFindingType.BudgetSlippage
            && state.Confidence < 0.55d)
        {
            state.Decision = FinancialAdvicePolicyDecision.Rejected;
            state.AiAdjudicationAllowed = false;
            state.Warnings.Add("finding_rejected_policy_insufficient_safe_actions");
            return;
        }

        if (state.HasAdjustments)
        {
            state.Decision = FinancialAdvicePolicyDecision.ApprovedWithAdjustments;
        }
    }
}
