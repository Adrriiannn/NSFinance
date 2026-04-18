namespace NSFinance.Api.Modules.AI.Services;

public sealed record AdviceLifecycleMetadata(
    FinancialAdviceEvidenceWindow EvidenceWindow,
    bool RequiresRefresh,
    IReadOnlyList<string> RefreshHints);

public interface IAdviceLifecycleMetadataBuilder
{
    AdviceLifecycleMetadata Build(
        IReadOnlyList<FinancialAdviceInsightItem> insights,
        DateTime nowUtc);
}

public sealed class AdviceLifecycleMetadataBuilder : IAdviceLifecycleMetadataBuilder
{
    public AdviceLifecycleMetadata Build(
        IReadOnlyList<FinancialAdviceInsightItem> insights,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(insights);

        var evidenceWindow = ResolveEvidenceWindow(insights, nowUtc);
        var refreshHints = insights
            .SelectMany(item => item.Freshness.InvalidationHints)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var requiresRefresh = insights.Any(item => item.Freshness.RequiresRecheck);

        return new AdviceLifecycleMetadata(
            EvidenceWindow: evidenceWindow,
            RequiresRefresh: requiresRefresh,
            RefreshHints: refreshHints);
    }

    private static FinancialAdviceEvidenceWindow ResolveEvidenceWindow(
        IReadOnlyList<FinancialAdviceInsightItem> insights,
        DateTime nowUtc)
    {
        if (insights.Count == 0)
        {
            return new FinancialAdviceEvidenceWindow(
                StartUtc: nowUtc.AddDays(-30),
                EndUtc: nowUtc,
                Label: "default_30d_window");
        }

        var startUtc = insights.Min(item => item.Freshness.EvidencePeriodStartUtc);
        var endUtc = insights.Max(item => item.Freshness.EvidencePeriodEndUtc);
        return new FinancialAdviceEvidenceWindow(
            StartUtc: startUtc,
            EndUtc: endUtc,
            Label: "aggregated_findings_window");
    }
}
