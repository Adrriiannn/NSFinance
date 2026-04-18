namespace NSFinance.Api.Modules.AI.Services;

public interface IAdviceSummaryBuilder
{
    string BuildUserSafeSummary(
        IReadOnlyList<FinancialAdviceInsightItem> insights,
        FinancialAdviceAdjudicationResult adjudication);
}

public sealed class AdviceSummaryBuilder : IAdviceSummaryBuilder
{
    public string BuildUserSafeSummary(
        IReadOnlyList<FinancialAdviceInsightItem> insights,
        FinancialAdviceAdjudicationResult adjudication)
    {
        ArgumentNullException.ThrowIfNull(insights);
        ArgumentNullException.ThrowIfNull(adjudication);

        if (insights.Count == 0)
        {
            return "I don't have enough grounded evidence for strong guidance yet.";
        }

        if (!string.IsNullOrWhiteSpace(adjudication.ResponseSummary))
        {
            return adjudication.ResponseSummary!;
        }

        var top = insights[0];
        var follow = insights.Skip(1).FirstOrDefault();
        if (follow is null)
        {
            return $"{top.Headline}. {top.Summary}";
        }

        return $"{top.Headline}. {top.Summary} Also: {follow.Summary}";
    }
}
