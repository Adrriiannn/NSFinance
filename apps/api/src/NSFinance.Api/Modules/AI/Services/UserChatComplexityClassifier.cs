using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed partial class UserChatComplexityClassifier : IUserChatComplexityClassifier
{
    private static readonly string[] ConstraintMarkers =
    [
        "must",
        "should",
        "cannot",
        "without",
        "only",
        "at most",
        "at least",
        "prefer",
        "compare",
        "rank",
        "budget",
        "plan",
        "forecast",
        "simulate",
        "tradeoff",
        "scenario"
    ];

    public UserChatComplexityEvaluation Evaluate(UserChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = request.UserMessage?.Trim() ?? string.Empty;
        if (message.Length == 0)
        {
            return new UserChatComplexityEvaluation(
                UserChatComplexity.Simple,
                ["empty_message"],
                0,
                false,
                false,
                false);
        }

        var lowered = message.ToLowerInvariant();
        var constraintCount = ConstraintMarkers.Count(marker => lowered.Contains(marker, StringComparison.Ordinal));
        var financialReasoning = FinancialReasoningPattern().IsMatch(lowered);
        var rankingIntent = RankingPattern().IsMatch(lowered);
        var multiStepLanguage = MultiStepPattern().IsMatch(lowered) || EnumeratedStepsPattern().IsMatch(message);

        var complexityScore = 0;
        if (message.Length >= 220)
        {
            complexityScore += 2;
        }
        else if (message.Length >= 120)
        {
            complexityScore += 1;
        }

        complexityScore += Math.Min(constraintCount, 4);
        if (financialReasoning)
        {
            complexityScore += 2;
        }

        if (rankingIntent)
        {
            complexityScore += 2;
        }

        if (multiStepLanguage)
        {
            complexityScore += 2;
        }

        var reasonCodes = new List<string>();
        if (message.Length >= 120)
        {
            reasonCodes.Add("long_message");
        }

        if (constraintCount > 0)
        {
            reasonCodes.Add($"constraint_count_{Math.Min(constraintCount, 5)}");
        }

        if (financialReasoning)
        {
            reasonCodes.Add("financial_reasoning_intent");
        }

        if (rankingIntent)
        {
            reasonCodes.Add("ranking_intent");
        }

        if (multiStepLanguage)
        {
            reasonCodes.Add("multi_step_language");
        }

        var complexity = complexityScore >= 5
            ? UserChatComplexity.Complex
            : UserChatComplexity.Simple;

        if (reasonCodes.Count == 0)
        {
            reasonCodes.Add("simple_chat_default");
        }

        return new UserChatComplexityEvaluation(
            complexity,
            reasonCodes,
            constraintCount,
            financialReasoning,
            rankingIntent,
            multiStepLanguage);
    }

    [GeneratedRegex(@"\b(budget|cashflow|forecast|projection|optimi[sz]e|amorti[sz]e|interest|loan|debt|savings|invest|subscription)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FinancialReasoningPattern();

    [GeneratedRegex(@"\b(rank|compare|top|best|prioriti[sz]e|which option|trade.?off)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankingPattern();

    [GeneratedRegex(@"\b(step by step|first\b.+second\b|analyze|analysis|reason through|break down)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiStepPattern();

    [GeneratedRegex(@"\b(1\.|2\.|3\.)", RegexOptions.CultureInvariant)]
    private static partial Regex EnumeratedStepsPattern();
}
