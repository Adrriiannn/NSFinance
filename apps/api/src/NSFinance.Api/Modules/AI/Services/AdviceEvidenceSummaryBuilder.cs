namespace NSFinance.Api.Modules.AI.Services;

public interface IAdviceEvidenceSummaryBuilder
{
    IReadOnlyList<string> Build(IReadOnlyList<FinancialAdvicePolicyReviewedFinding> findings);
}

public sealed class AdviceEvidenceSummaryBuilder : IAdviceEvidenceSummaryBuilder
{
    public IReadOnlyList<string> Build(IReadOnlyList<FinancialAdvicePolicyReviewedFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var summary = new List<string>(findings.Count + 2);
        foreach (var item in findings.OrderByDescending(entry => entry.Finding.PriorityScore).Take(6))
        {
            summary.Add(item.Finding.EvidenceSummary);
            foreach (var metric in item.Finding.SupportingMetrics.Take(3))
            {
                summary.Add($"{metric.Key}:{metric.Value:0.####}:{metric.Unit}");
            }
        }

        return summary
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
