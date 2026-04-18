using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class RecurringSpendEvaluator(
    IFinancialAdviceCategoryClassifier categoryClassifier,
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

        if (context.Recurring is null || context.Recurring.EstimatedMonthlyTotal <= 0m)
        {
            return;
        }

        var recurringTotal = context.Recurring.EstimatedMonthlyTotal;
        var baselineRecurring = context.Baseline.BaselineRecurringMonthlyTotal;
        var hasBaselineRecurring = baselineRecurring > 0m;
        var ratio = hasBaselineRecurring ? recurringTotal / baselineRecurring : 0m;
        var delta = hasBaselineRecurring ? recurringTotal - baselineRecurring : recurringTotal;
        var income = context.Summary?.IncomeLast30Days ?? 0m;
        var recurringToIncome = income > 0m ? recurringTotal / income : 0m;

        var hasPressure = hasBaselineRecurring
            ? ratio >= adviceOptions.RecurringPressureIncreaseRatioThreshold
              && delta >= adviceOptions.RecurringPressureAbsoluteDeltaThreshold
            : recurringToIncome >= adviceOptions.RecurringToIncomePressureRatioThreshold
              && recurringTotal >= adviceOptions.MaterialSpendThreshold;
        if (!hasPressure)
        {
            return;
        }

        var protectedFlags = context.Recurring.TopItems.Any(item => categoryClassifier.IsProtectedRecurringName(item.Name))
            ? ["contains_essential_or_obligatory_recurring_charges"]
            : Array.Empty<string>();
        var severity = recurringToIncome >= 0.65m || ratio >= 1.35m
            ? FinancialAdviceSeverity.High
            : FinancialAdviceSeverity.Moderate;
        var confidence = hasBaselineRecurring ? 0.78d : 0.69d;
        var uncertainty = hasBaselineRecurring
            ? Array.Empty<string>()
            : ["missing_recurring_baseline"];

        var actions = new List<FinancialAdviceActionCandidate>(2)
        {
            new(
                ActionId: "review_recurring_charges",
                ActionType: FinancialAdviceActionType.TrackRecurringCharge,
                Title: "Review recurring charges",
                Guidance: "Check recurring charges for duplicates, outdated subscriptions, and negotiable plans."),
            new(
                ActionId: "sequence_recurring_schedule",
                ActionType: FinancialAdviceActionType.BuildBuffer,
                Title: "Align bill timing with cashflow",
                Guidance: "Where possible, align recurring charge dates with income timing to reduce pressure spikes.")
        };

        session.Findings.Add(
            findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: session.NextFindingId(FinancialAdviceFindingType.RecurringSpendPressure),
                    FindingType: FinancialAdviceFindingType.RecurringSpendPressure,
                    RelatedIntent: context.Routing.PrimaryIntent,
                    Severity: severity,
                    Confidence: confidence,
                    EvidenceSummary: hasBaselineRecurring
                        ? $"Recurring monthly commitments are {FinancialAdviceFormatting.FormatRatio(ratio)} vs your historical recurring baseline."
                        : $"Recurring commitments are taking about {FinancialAdviceFormatting.FormatPercentage(recurringToIncome)} of recent monthly income.",
                    SupportingMetrics:
                    [
                        new FinancialAdviceEvidenceMetric("recurringMonthlyTotal", recurringTotal, context.Summary?.Currency ?? "currency"),
                        new FinancialAdviceEvidenceMetric("baselineRecurringMonthlyTotal", baselineRecurring, context.Summary?.Currency ?? "currency"),
                        new FinancialAdviceEvidenceMetric("recurringToIncomeRatio", recurringToIncome, "ratio"),
                        new FinancialAdviceEvidenceMetric("recurringIncreaseRatio", ratio, "ratio")
                    ],
                    DomainCode: null,
                    DomainName: null,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: protectedFlags,
                    RecommendedActions: actions,
                    UncertaintyMarkers: uncertainty,
                    AiAdjudicationAllowed: true,
                    AiAdjudicationRecommended: severity >= FinancialAdviceSeverity.High
                                             || confidence < adviceOptions.HighConfidenceSkipThreshold,
                    ComputedAtUtc: context.NowUtc,
                    RenderingFamily: "recurring_pressure")));
    }
}
