namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceFindingFactory
{
    FinancialAdviceFinding Create(FinancialAdviceFindingBuildRequest request);
}

public sealed class FinancialAdviceFindingFactory(
    IInsightFreshnessEvaluator freshnessEvaluator) : IFinancialAdviceFindingFactory
{
    public FinancialAdviceFinding Create(FinancialAdviceFindingBuildRequest request)
    {
        var freshness = freshnessEvaluator.Build(
            request.FindingType,
            request.Severity,
            request.ComputedAtUtc);

        return new FinancialAdviceFinding(
            FindingId: request.FindingId,
            FindingType: request.FindingType,
            RelatedIntent: request.RelatedIntent,
            Severity: request.Severity,
            PriorityScore: FinancialAdvicePriorityScoring.Compute(request.Severity, request.Confidence),
            Confidence: Math.Clamp(request.Confidence, 0d, 1d),
            EvidenceSummary: request.EvidenceSummary,
            SupportingMetrics: request.SupportingMetrics,
            DomainCode: request.DomainCode,
            DomainName: request.DomainName,
            CategoryCode: request.CategoryCode,
            CategoryName: request.CategoryName,
            ProtectedCategoryFlags: request.ProtectedCategoryFlags,
            RecommendedActions: request.RecommendedActions,
            UncertaintyMarkers: request.UncertaintyMarkers,
            PolicyWarnings: [],
            PolicyExclusions: [],
            AiAdjudicationAllowed: request.AiAdjudicationAllowed,
            AiAdjudicationRecommended: request.AiAdjudicationRecommended,
            Freshness: freshness,
            RenderingHints: BuildRenderingHints(request.RenderingFamily));
    }

    private static IReadOnlyDictionary<string, string> BuildRenderingHints(string family)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["insightFamily"] = family,
            ["surface"] = "key_insight_or_chat",
            ["tone"] = "supportive",
            ["scope"] = "financial_guidance"
        };
    }
}

public static class FinancialAdvicePriorityScoring
{
    public static int Compute(FinancialAdviceSeverity severity, double confidence)
    {
        var severityWeight = severity switch
        {
            FinancialAdviceSeverity.Critical => 95,
            FinancialAdviceSeverity.High => 82,
            FinancialAdviceSeverity.Moderate => 65,
            FinancialAdviceSeverity.Low => 45,
            _ => 25
        };

        return Math.Clamp(
            severityWeight + (int)Math.Round(Math.Clamp(confidence, 0d, 1d) * 10d),
            1,
            100);
    }
}
