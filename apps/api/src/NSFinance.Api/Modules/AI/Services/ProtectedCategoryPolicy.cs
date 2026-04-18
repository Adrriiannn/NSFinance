namespace NSFinance.Api.Modules.AI.Services;

public interface IProtectedCategoryPolicy
{
    bool CanApplyAction(
        FinancialAdvicePolicyActionContext context,
        FinancialAdvicePolicyEvaluationState state);
}

public sealed class ProtectedCategoryPolicy : IProtectedCategoryPolicy
{
    public bool CanApplyAction(
        FinancialAdvicePolicyActionContext context,
        FinancialAdvicePolicyEvaluationState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        if (context.Action.ActionType != FinancialAdviceActionType.ReduceSpend)
        {
            return true;
        }

        var blockedByProtectedCategory = context.Action.IsProtectedCategory
                                         || context.Finding.ProtectedCategoryFlags.Count > 0;
        if (blockedByProtectedCategory)
        {
            state.Exclusions.Add("protected_category_reduction_blocked");
            state.Warnings.Add("policy_protected_category_guardrail_applied");
            return false;
        }

        if (ConflictsWithProtectedPreference(context))
        {
            state.Exclusions.Add("profile_protected_preference_conflict");
            state.Warnings.Add("policy_profile_preference_guardrail_applied");
            return false;
        }

        return true;
    }

    private static bool ConflictsWithProtectedPreference(FinancialAdvicePolicyActionContext context)
    {
        if (context.ProtectedPreferenceHints.Count == 0)
        {
            return false;
        }

        var domainToken = context.Action.TargetDomainCode?.ToString() ?? context.Finding.DomainCode?.ToString();
        var categoryToken = context.Action.TargetCategoryCode?.ToString() ?? context.Finding.CategoryCode?.ToString();
        var domainName = context.Finding.DomainName ?? string.Empty;
        var categoryName = context.Finding.CategoryName ?? string.Empty;
        var combined = $"{domainToken}|{categoryToken}|{domainName}|{categoryName}".ToLowerInvariant();

        foreach (var hint in context.ProtectedPreferenceHints)
        {
            var hintLower = hint.ToLowerInvariant();
            if (combined.Contains(hintLower, StringComparison.Ordinal))
            {
                return true;
            }

            if (hintLower.Contains("essential", StringComparison.Ordinal)
                || hintLower.Contains("protected", StringComparison.Ordinal)
                || hintLower.Contains("do_not_cut", StringComparison.Ordinal))
            {
                if (context.Finding.ProtectedCategoryFlags.Count > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
