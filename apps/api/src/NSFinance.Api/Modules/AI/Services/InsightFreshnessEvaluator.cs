using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IInsightFreshnessEvaluator
{
    FinancialAdviceFreshnessMetadata Build(
        FinancialAdviceFindingType findingType,
        FinancialAdviceSeverity severity,
        DateTime computedAtUtc);
}

public sealed class InsightFreshnessEvaluator(
    IOptions<CompanionAdviceOptions> options,
    IInsightInvalidationHintBuilder invalidationHintBuilder) : IInsightFreshnessEvaluator
{
    private readonly CompanionAdviceOptions adviceOptions = options.Value;

    public FinancialAdviceFreshnessMetadata Build(
        FinancialAdviceFindingType findingType,
        FinancialAdviceSeverity severity,
        DateTime computedAtUtc)
    {
        var windowHours = ResolveFreshnessWindowHours(severity);
        var evidenceStartUtc = ResolveEvidenceStartUtc(findingType, computedAtUtc);
        var freshUntilUtc = computedAtUtc.AddHours(Math.Max(6, windowHours));
        var recheckAfterUtc = computedAtUtc.AddHours(Math.Max(3, windowHours / 2));
        var confidenceDecayPerDay = Math.Round(
            1d / Math.Max(1d, windowHours / 24d),
            4,
            MidpointRounding.AwayFromZero);

        return new FinancialAdviceFreshnessMetadata(
            ComputedAtUtc: computedAtUtc,
            EvidencePeriodStartUtc: evidenceStartUtc,
            EvidencePeriodEndUtc: computedAtUtc,
            FreshUntilUtc: freshUntilUtc,
            RecheckAfterUtc: recheckAfterUtc,
            FreshnessState: FinancialAdviceFreshnessState.Fresh,
            ConfidenceDecayPerDay: confidenceDecayPerDay,
            RelevanceScore: ResolveRelevanceScore(severity),
            RequiresRecheck: true,
            InvalidationHints: invalidationHintBuilder.Build(findingType));
    }

    private int ResolveFreshnessWindowHours(FinancialAdviceSeverity severity)
    {
        return severity switch
        {
            FinancialAdviceSeverity.Critical => adviceOptions.BaseFreshnessHoursHighSeverity,
            FinancialAdviceSeverity.High => adviceOptions.BaseFreshnessHoursHighSeverity,
            FinancialAdviceSeverity.Moderate => adviceOptions.BaseFreshnessHoursModerateSeverity,
            FinancialAdviceSeverity.Low => adviceOptions.BaseFreshnessHoursLowSeverity,
            _ => adviceOptions.BaseFreshnessHoursInfoSeverity
        };
    }

    private static DateTime ResolveEvidenceStartUtc(
        FinancialAdviceFindingType findingType,
        DateTime computedAtUtc)
    {
        return findingType switch
        {
            FinancialAdviceFindingType.RecurringSpendPressure => computedAtUtc.AddDays(-120),
            FinancialAdviceFindingType.CategoryPressure => computedAtUtc.AddDays(-60),
            FinancialAdviceFindingType.DiscretionaryOverspend => computedAtUtc.AddDays(-60),
            FinancialAdviceFindingType.BudgetSlippage => new DateTime(
                computedAtUtc.Year,
                computedAtUtc.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc),
            _ => computedAtUtc.AddDays(-30)
        };
    }

    private static double ResolveRelevanceScore(FinancialAdviceSeverity severity)
    {
        return severity switch
        {
            FinancialAdviceSeverity.Critical => 0.95d,
            FinancialAdviceSeverity.High => 0.85d,
            FinancialAdviceSeverity.Moderate => 0.72d,
            FinancialAdviceSeverity.Low => 0.55d,
            _ => 0.42d
        };
    }
}
