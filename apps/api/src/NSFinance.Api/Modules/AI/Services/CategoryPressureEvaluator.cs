using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CategoryPressureEvaluator(
    ExpenseTaxonomyService taxonomyService,
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

        if (context.Spending is null || context.Spending.TopDomainSpend.Count == 0)
        {
            return;
        }

        var totalSpend = context.Summary?.SpendLast30Days ?? context.Spending.TopDomainSpend.Sum(x => x.Amount);
        foreach (var domain in context.Spending.TopDomainSpend.Take(3))
        {
            var currentAmount = Math.Abs(domain.Amount);
            if (currentAmount <= 0m)
            {
                continue;
            }

            var domainName = taxonomyService.GetDomainName(domain.DomainCode);
            var baselineAmount = context.Baseline.BaselineSpendByDomain.GetValueOrDefault(domain.DomainCode);
            var hasBaseline = baselineAmount > 0m;
            var ratio = hasBaseline
                ? currentAmount / baselineAmount
                : totalSpend > 0m
                    ? currentAmount / totalSpend
                    : 0m;
            var delta = hasBaseline ? currentAmount - baselineAmount : currentAmount;
            var concentration = totalSpend > 0m ? currentAmount / totalSpend : 0m;
            var isProtectedDomain = categoryClassifier.IsProtectedDomain(domain.DomainCode, domainName);
            var isDiscretionary = categoryClassifier.IsDiscretionaryDomain(domainName);

            var thresholdHit = hasBaseline
                ? ratio >= adviceOptions.CategoryPressureIncreaseRatioThreshold
                  && delta >= adviceOptions.CategoryPressureAbsoluteDeltaThreshold
                : concentration >= 0.35m
                  && currentAmount >= adviceOptions.MaterialSpendThreshold;
            if (!thresholdHit)
            {
                continue;
            }

            var severity = ratio >= 1.55m || concentration >= 0.5m
                ? FinancialAdviceSeverity.High
                : FinancialAdviceSeverity.Moderate;
            var confidence = hasBaseline ? 0.82d : 0.64d;
            var uncertainty = hasBaseline
                ? Array.Empty<string>()
                : ["missing_category_specific_baseline"];

            var actions = new List<FinancialAdviceActionCandidate>(2);
            if (isProtectedDomain)
            {
                actions.Add(new FinancialAdviceActionCandidate(
                    ActionId: $"review_{domain.DomainCode}_usage",
                    ActionType: FinancialAdviceActionType.ReviewSpend,
                    Title: "Review billing and usage changes",
                    Guidance: "This category appears essential. Focus on billing accuracy, duplicate charges, or provider plans before reductions.",
                    TargetDomainCode: domain.DomainCode,
                    IsProtectedCategory: true));
            }
            else
            {
                var suggestedCutRatio = decimal.Clamp((ratio - 1m) / 2m, 0.05m, 0.20m);
                actions.Add(new FinancialAdviceActionCandidate(
                    ActionId: $"reduce_{domain.DomainCode}_spend",
                    ActionType: FinancialAdviceActionType.ReduceSpend,
                    Title: "Set a temporary cap",
                    Guidance: "Apply a temporary cap for this category and review results over the next two weeks.",
                    SuggestedMagnitude: decimal.Round(suggestedCutRatio, 2, MidpointRounding.AwayFromZero),
                    TargetDomainCode: domain.DomainCode,
                    IsProtectedCategory: false));
                actions.Add(new FinancialAdviceActionCandidate(
                    ActionId: $"track_{domain.DomainCode}_trend",
                    ActionType: FinancialAdviceActionType.ReviewSpend,
                    Title: "Track weekly trend",
                    Guidance: "Track this category weekly against your own baseline to confirm whether pressure is easing.",
                    TargetDomainCode: domain.DomainCode));
            }

            var findingType = isDiscretionary
                ? FinancialAdviceFindingType.DiscretionaryOverspend
                : FinancialAdviceFindingType.CategoryPressure;
            var finding = findingFactory.Create(
                new FinancialAdviceFindingBuildRequest(
                    FindingId: session.NextFindingId(findingType),
                    FindingType: findingType,
                    RelatedIntent: context.Routing.PrimaryIntent,
                    Severity: severity,
                    Confidence: confidence,
                    EvidenceSummary: hasBaseline
                        ? $"{domainName ?? "Category"} spend is {FinancialAdviceFormatting.FormatRatio(ratio)} vs your own baseline ({FinancialAdviceFormatting.FormatCurrency(delta, context.Summary?.Currency)} above baseline)."
                        : $"{domainName ?? "Category"} accounts for {FinancialAdviceFormatting.FormatPercentage(concentration)} of tracked spending and stands out as a concentration pressure point.",
                    SupportingMetrics:
                    [
                        new FinancialAdviceEvidenceMetric("currentDomainSpend", currentAmount, context.Summary?.Currency ?? "currency"),
                        new FinancialAdviceEvidenceMetric("baselineDomainSpend", baselineAmount, context.Summary?.Currency ?? "currency"),
                        new FinancialAdviceEvidenceMetric("domainSpendRatio", ratio, "ratio"),
                        new FinancialAdviceEvidenceMetric("domainSpendConcentration", concentration, "ratio")
                    ],
                    DomainCode: domain.DomainCode,
                    DomainName: domainName,
                    CategoryCode: null,
                    CategoryName: null,
                    ProtectedCategoryFlags: isProtectedDomain ? ["protected_or_essential_domain"] : [],
                    RecommendedActions: actions,
                    UncertaintyMarkers: uncertainty,
                    AiAdjudicationAllowed: true,
                    AiAdjudicationRecommended: confidence < adviceOptions.HighConfidenceSkipThreshold
                                             || severity >= FinancialAdviceSeverity.High,
                    ComputedAtUtc: context.NowUtc,
                    RenderingFamily: "category_pressure"));
            session.Findings.Add(finding);

            if (findingType == FinancialAdviceFindingType.DiscretionaryOverspend)
            {
                break;
            }
        }
    }
}
