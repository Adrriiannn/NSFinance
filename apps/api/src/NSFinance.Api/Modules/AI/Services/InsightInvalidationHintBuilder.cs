namespace NSFinance.Api.Modules.AI.Services;

public interface IInsightInvalidationHintBuilder
{
    IReadOnlyList<string> Build(FinancialAdviceFindingType findingType);
}

public sealed class InsightInvalidationHintBuilder : IInsightInvalidationHintBuilder
{
    public IReadOnlyList<string> Build(FinancialAdviceFindingType findingType)
    {
        return findingType switch
        {
            FinancialAdviceFindingType.CategoryPressure or FinancialAdviceFindingType.DiscretionaryOverspend =>
            [
                "category_spend_materially_changed",
                "category_baseline_changed",
                "supporting_period_rolled_over",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.RecurringSpendPressure =>
            [
                "recurring_commitments_changed",
                "recurring_amount_materially_changed",
                "supporting_period_rolled_over",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.BudgetSlippage =>
            [
                "budget_state_materially_changed",
                "budget_plan_changed",
                "month_boundary_rollover",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.AffordabilityRisk =>
            [
                "income_or_spend_materially_changed",
                "required_payments_changed",
                "budget_state_materially_changed",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.PlanDrift or FinancialAdviceFindingType.PositiveProgress =>
            [
                "plan_state_materially_changed",
                "plan_targets_changed",
                "supporting_period_rolled_over",
                "stale_age_exceeded"
            ],
            _ =>
            [
                "supporting_data_changed",
                "stale_age_exceeded"
            ]
        };
    }
}
