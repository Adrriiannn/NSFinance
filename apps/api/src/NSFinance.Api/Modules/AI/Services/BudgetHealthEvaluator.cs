using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class BudgetHealthEvaluator(
    IFinancialAdviceFindingFactory findingFactory,
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceFindingEvaluator
{
    private readonly CompanionAdviceOptions adviceOptions = options.Value;

    public void Evaluate(
        FinancialAdviceEvaluationContext context,
        FinancialAdviceEvaluationSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        var budget = context.Budget;
        if (budget is null || !budget.HasBudgetPlan || !budget.MonthlyBudget.HasValue || budget.MonthlyBudget <= 0m)
        {
            return;
        }

        var monthlyBudget = budget.MonthlyBudget.Value;
        var remaining = budget.RemainingBudget ?? (monthlyBudget - budget.MonthToDateSpend);
        var spendRatio = budget.MonthToDateSpend / monthlyBudget;
        var day = context.NowUtc.Day;
        var daysInMonth = DateTime.DaysInMonth(context.NowUtc.Year, context.NowUtc.Month);
        var monthProgress = daysInMonth > 0 ? (decimal)day / daysInMonth : 0.5m;

        if (remaining < 0m || spendRatio >= monthProgress + adviceOptions.BudgetSlippageRatioThreshold)
        {
            var severity = remaining < 0m ? FinancialAdviceSeverity.High : FinancialAdviceSeverity.Moderate;
            session.Findings.Add(
                findingFactory.Create(
                    new FinancialAdviceFindingBuildRequest(
                        FindingId: session.NextFindingId(FinancialAdviceFindingType.BudgetSlippage),
                        FindingType: FinancialAdviceFindingType.BudgetSlippage,
                        RelatedIntent: context.Routing.PrimaryIntent,
                        Severity: severity,
                        Confidence: 0.88d,
                        EvidenceSummary: remaining < 0m
                            ? $"You are currently {FinancialAdviceFormatting.FormatCurrency(Math.Abs(remaining), "currency")} over your active monthly budget."
                            : $"Month-to-date spend is ahead of budget pacing ({FinancialAdviceFormatting.FormatPercentage(spendRatio)} spent with {FinancialAdviceFormatting.FormatPercentage(monthProgress)} of month elapsed).",
                        SupportingMetrics:
                        [
                            new FinancialAdviceEvidenceMetric("monthlyBudget", monthlyBudget, "currency"),
                            new FinancialAdviceEvidenceMetric("monthToDateSpend", budget.MonthToDateSpend, "currency"),
                            new FinancialAdviceEvidenceMetric("remainingBudget", remaining, "currency"),
                            new FinancialAdviceEvidenceMetric("budgetSpendRatio", spendRatio, "ratio"),
                            new FinancialAdviceEvidenceMetric("monthProgressRatio", monthProgress, "ratio")
                        ],
                        DomainCode: null,
                        DomainName: null,
                        CategoryCode: null,
                        CategoryName: null,
                        ProtectedCategoryFlags: [],
                        RecommendedActions:
                        [
                            new FinancialAdviceActionCandidate(
                                ActionId: "rebalance_monthly_budget",
                                ActionType: FinancialAdviceActionType.AdjustBudget,
                                Title: "Rebalance the monthly budget",
                                Guidance: "Rebalance discretionary categories first and protect essentials while recovering plan alignment."),
                            new FinancialAdviceActionCandidate(
                                ActionId: "pause_nonessential_spend_short_term",
                                ActionType: FinancialAdviceActionType.ReduceSpend,
                                Title: "Pause non-essential spend briefly",
                                Guidance: "Use a short pause on non-essential spend to stabilize this month's budget.",
                                SuggestedMagnitude: 0.10m)
                        ],
                        UncertaintyMarkers: [],
                        AiAdjudicationAllowed: true,
                        AiAdjudicationRecommended: true,
                        ComputedAtUtc: context.NowUtc,
                        RenderingFamily: "budget_slippage")));
            return;
        }

        var remainingRatio = remaining / monthlyBudget;
        if (remainingRatio <= adviceOptions.BudgetLowRemainingRatioThreshold && monthProgress <= 0.75m)
        {
            session.Findings.Add(
                findingFactory.Create(
                    new FinancialAdviceFindingBuildRequest(
                        FindingId: session.NextFindingId(FinancialAdviceFindingType.BudgetSlippage),
                        FindingType: FinancialAdviceFindingType.BudgetSlippage,
                        RelatedIntent: context.Routing.PrimaryIntent,
                        Severity: FinancialAdviceSeverity.Low,
                        Confidence: 0.78d,
                        EvidenceSummary: $"Remaining budget is down to {FinancialAdviceFormatting.FormatPercentage(remainingRatio)} with part of the month still ahead.",
                        SupportingMetrics:
                        [
                            new FinancialAdviceEvidenceMetric("remainingBudgetRatio", remainingRatio, "ratio"),
                            new FinancialAdviceEvidenceMetric("monthProgressRatio", monthProgress, "ratio")
                        ],
                        DomainCode: null,
                        DomainName: null,
                        CategoryCode: null,
                        CategoryName: null,
                        ProtectedCategoryFlags: [],
                        RecommendedActions:
                        [
                            new FinancialAdviceActionCandidate(
                                ActionId: "tighten_discretionary_budget",
                                ActionType: FinancialAdviceActionType.AdjustBudget,
                                Title: "Tighten discretionary budget",
                                Guidance: "Tighten flexible categories to preserve room for essentials.")
                        ],
                        UncertaintyMarkers: [],
                        AiAdjudicationAllowed: true,
                        AiAdjudicationRecommended: false,
                        ComputedAtUtc: context.NowUtc,
                        RenderingFamily: "budget_watch")));
        }
    }
}
