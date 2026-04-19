using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AffordabilityEvaluator(
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

        if (context.Summary is null)
        {
            return;
        }

        var recurringMonthly = context.Recurring?.EstimatedMonthlyTotal
                               ?? context.Baseline.BaselineRecurringMonthlyTotal;
        var income = context.Summary.IncomeLast30Days;
        var net = context.Summary.NetLast30Days;
        var affordabilityRoom = net - recurringMonthly;
        var roomToIncome = income > 0m ? affordabilityRoom / income : 0m;
        var budgetRemaining = context.Budget?.RemainingBudget ?? 0m;

        if (net >= 0m
            && affordabilityRoom >= 0m
            && roomToIncome >= adviceOptions.AffordabilityBufferRatioThreshold)
        {
            return;
        }

        var severity = net < 0m || affordabilityRoom < 0m
            ? FinancialAdviceSeverity.High
            : FinancialAdviceSeverity.Moderate;
        var evidenceSummary = net < 0m
            ? "Recent monthly net cashflow is negative, which indicates affordability pressure."
            : "Affordability room after recurring commitments is limited "
              + $"({FinancialAdviceFormatting.FormatPercentage(roomToIncome)} of income).";

        session.Findings.Add(
            findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: session.NextFindingId(FinancialAdviceFindingType.AffordabilityRisk),
                    FindingType: FinancialAdviceFindingType.AffordabilityRisk,
                    RelatedIntent: context.Routing.PrimaryIntent,
                    Severity: severity,
                    Confidence: 0.86d,
                    EvidenceSummary: evidenceSummary,
                    SupportingMetrics:
                    [
                        new FinancialAdviceEvidenceMetric("incomeLast30Days", income, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("netLast30Days", net, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("recurringMonthly", recurringMonthly, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("affordabilityRoom", affordabilityRoom, context.Summary.Currency),
                        new FinancialAdviceEvidenceMetric("affordabilityRoomToIncomeRatio", roomToIncome, "ratio"),
                        new FinancialAdviceEvidenceMetric("budgetRemaining", budgetRemaining, context.Summary.Currency)
                    ],
                    DomainCode: null,
                    DomainName: null,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: [],
                    RecommendedActions:
                    [
                        new FinancialAdviceActionCandidate(
                            ActionId: "build_affordability_buffer",
                            ActionType: FinancialAdviceActionType.BuildBuffer,
                            Title: "Rebuild affordability buffer",
                            Guidance:
                            "Prioritize preserving essentials and rebuild a small monthly buffer "
                            + "before discretionary increases."),
                        new FinancialAdviceActionCandidate(
                            ActionId: "sequence_large_purchases",
                            ActionType: FinancialAdviceActionType.ReviewSpend,
                            Title: "Sequence large discretionary purchases",
                            Guidance: "Sequence larger discretionary purchases only after buffer recovery.")
                    ],
                    UncertaintyMarkers: [],
                    AiAdjudicationAllowed: true,
                    AiAdjudicationRecommended: true,
                    ComputedAtUtc: context.NowUtc,
                    RenderingFamily: "affordability_risk")));
    }
}
