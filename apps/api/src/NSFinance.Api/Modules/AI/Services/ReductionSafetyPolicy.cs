namespace NSFinance.Api.Modules.AI.Services;

public interface IReductionSafetyPolicy
{
    bool CanApplyAction(
        FinancialAdvicePolicyActionContext context,
        FinancialAdvicePolicyEvaluationState state);
}

public sealed class ReductionSafetyPolicy : IReductionSafetyPolicy
{
    private static readonly HashSet<FinancialAdviceActionType> SupportedActionTypes =
    [
        FinancialAdviceActionType.ReviewSpend,
        FinancialAdviceActionType.ReduceSpend,
        FinancialAdviceActionType.TrackRecurringCharge,
        FinancialAdviceActionType.AdjustBudget,
        FinancialAdviceActionType.BuildBuffer,
        FinancialAdviceActionType.ReviewPlan,
        FinancialAdviceActionType.KeepCourse
    ];

    public bool CanApplyAction(
        FinancialAdvicePolicyActionContext context,
        FinancialAdvicePolicyEvaluationState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        if (!SupportedActionTypes.Contains(context.Action.ActionType))
        {
            state.Exclusions.Add("unsupported_action_outside_current_scope");
            return false;
        }

        if (context.Action.ActionType != FinancialAdviceActionType.ReduceSpend)
        {
            return true;
        }

        if (context.Action.SuggestedMagnitude.GetValueOrDefault() > 0.20m && state.Confidence < 0.85d)
        {
            state.Exclusions.Add("aggressive_reduction_blocked_without_strong_evidence");
            state.Warnings.Add("policy_aggressive_reduction_guardrail_applied");
            return false;
        }

        if (state.Confidence < 0.60d)
        {
            state.Exclusions.Add("reduction_blocked_low_confidence");
            state.Warnings.Add("policy_low_confidence_reduction_guardrail_applied");
            return false;
        }

        return true;
    }
}
