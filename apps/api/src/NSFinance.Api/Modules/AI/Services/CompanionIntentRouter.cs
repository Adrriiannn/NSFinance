using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionIntentRouter
{
    CompanionIntentRoutingResult Route(string? userQuery);
}

public sealed class CompanionIntentRouter(ILogger<CompanionIntentRouter> logger) : ICompanionIntentRouter
{
    private const double MatchThreshold = 1.0d;
    private const double MixedSecondaryRatioThreshold = 0.70d;
    private const double MixedSecondaryAbsoluteThreshold = 1.3d;

    private static readonly IntentPattern[] Patterns =
    [
        new(FinancialCompanionIntent.SpendingAnalysis, "spending the most", 1.5d, "matched_spending_most"),
        new(FinancialCompanionIntent.SpendingAnalysis, "money going", 1.2d, "matched_money_flow"),
        new(FinancialCompanionIntent.SpendingAnalysis, "spending pattern", 1.1d, "matched_spending_pattern"),
        new(FinancialCompanionIntent.SpendingAnalysis, "overspending", 1.3d, "matched_overspending"),
        new(FinancialCompanionIntent.SpendingAnalysis, "am i overspending", 1.4d, "matched_am_i_overspending"),
        new(FinancialCompanionIntent.SpendingAnalysis, "category breakdown", 1.3d, "matched_category_breakdown"),
        new(FinancialCompanionIntent.SpendingAnalysis, "draining my budget", 1.3d, "matched_budget_drain"),
        new(FinancialCompanionIntent.SpendingAnalysis, "where is most of my money", 1.2d, "matched_money_distribution"),
        new(FinancialCompanionIntent.SpendingAnalysis, "what am i spending", 1.0d, "matched_spending_question"),

        new(FinancialCompanionIntent.SavingsCutbackAdvice, "cut back", 1.5d, "matched_cut_back"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "cut expenses", 1.5d, "matched_cut_expenses"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "cut costs", 1.5d, "matched_cut_costs"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "save more", 1.4d, "matched_save_more"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "spend less", 1.2d, "matched_spend_less"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "reduce spending", 1.3d, "matched_reduce_spending"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "reduce first", 1.1d, "matched_reduce_first"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "what should i reduce first", 1.5d, "matched_explicit_reduce_first"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "lower expenses", 1.2d, "matched_lower_expenses"),
        new(FinancialCompanionIntent.SavingsCutbackAdvice, "how can i save", 1.1d, "matched_how_save"),

        new(FinancialCompanionIntent.Affordability, "can i afford", 1.6d, "matched_can_i_afford"),
        new(FinancialCompanionIntent.Affordability, "afford", 1.2d, "matched_affordability"),
        new(FinancialCompanionIntent.Affordability, "can i buy", 1.2d, "matched_can_i_buy"),
        new(FinancialCompanionIntent.Affordability, "purchase", 1.0d, "matched_purchase"),
        new(FinancialCompanionIntent.Affordability, "still be okay", 1.1d, "matched_still_okay"),
        new(FinancialCompanionIntent.Affordability, "go out tonight", 1.0d, "matched_go_out_affordability"),

        new(FinancialCompanionIntent.BudgetStatus, "budget looking", 1.5d, "matched_budget_looking"),
        new(FinancialCompanionIntent.BudgetStatus, "over budget", 1.5d, "matched_over_budget"),
        new(FinancialCompanionIntent.BudgetStatus, "under budget", 1.4d, "matched_under_budget"),
        new(FinancialCompanionIntent.BudgetStatus, "budget status", 1.3d, "matched_budget_status"),
        new(FinancialCompanionIntent.BudgetStatus, "budget health", 1.2d, "matched_budget_health"),
        new(FinancialCompanionIntent.BudgetStatus, "room do i have left", 1.3d, "matched_budget_room_left"),
        new(FinancialCompanionIntent.BudgetStatus, "budget do i have left", 1.3d, "matched_budget_left"),
        new(FinancialCompanionIntent.BudgetStatus, "remaining budget", 1.3d, "matched_remaining_budget"),
        new(FinancialCompanionIntent.BudgetStatus, "budget overspend", 1.5d, "matched_budget_overspend"),

        new(FinancialCompanionIntent.PlanProgress, "on track", 1.4d, "matched_on_track"),
        new(FinancialCompanionIntent.PlanProgress, "plan going", 1.3d, "matched_plan_progress"),
        new(FinancialCompanionIntent.PlanProgress, "goal", 1.2d, "matched_goal_progress"),
        new(FinancialCompanionIntent.PlanProgress, "how far along", 1.4d, "matched_progress_distance"),
        new(FinancialCompanionIntent.PlanProgress, "target", 1.1d, "matched_target_progress"),
        new(FinancialCompanionIntent.PlanProgress, "my plan", 1.1d, "matched_plan_reference"),

        new(FinancialCompanionIntent.LocalPlacesOutings, "nearby", 1.3d, "matched_nearby"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "near me", 1.3d, "matched_near_me"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "where can i go", 1.4d, "matched_where_can_i_go"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "where should i go", 1.4d, "matched_where_should_i_go"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "restaurant", 1.2d, "matched_restaurant"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "outing", 1.0d, "matched_outing"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "go out this weekend", 1.1d, "matched_weekend_outing"),
        new(FinancialCompanionIntent.LocalPlacesOutings, "eat nearby", 1.2d, "matched_eat_nearby"),

        new(FinancialCompanionIntent.GeneralFinancialQuestion, "how am i doing financially", 1.4d, "matched_financially_how_doing"),
        new(FinancialCompanionIntent.GeneralFinancialQuestion, "smartest next step", 1.5d, "matched_smart_next_step"),
        new(FinancialCompanionIntent.GeneralFinancialQuestion, "what should i focus on", 1.4d, "matched_focus_priority"),
        new(FinancialCompanionIntent.GeneralFinancialQuestion, "what should i prioritize", 1.4d, "matched_prioritize"),
        new(FinancialCompanionIntent.GeneralFinancialQuestion, "how am i doing with my finances", 1.4d, "matched_finances_status"),
        new(FinancialCompanionIntent.GeneralFinancialQuestion, "financially lately", 1.2d, "matched_financially_lately")
    ];

    private static readonly string[] AmbiguousPhrases =
    [
        "what should i do",
        "help me with my money",
        "how am i doing",
        "help with money",
        "help me",
        "advice please"
    ];

    private static readonly string[] FinanceMarkers =
    [
        "money",
        "budget",
        "spending",
        "expense",
        "expenses",
        "save",
        "savings",
        "afford",
        "financial",
        "finances",
        "plan",
        "goal",
        "outing",
        "restaurant"
    ];

    private static readonly string[] UnsupportedMarkers =
    [
        "python",
        "programming",
        "code",
        "debug",
        "recipe",
        "translate",
        "weather",
        "sports score",
        "movie script",
        "physics homework"
    ];

    private static readonly string[] MixedIntentConnectors =
    [
        " and ",
        " also ",
        " plus ",
        " as well as ",
        " while "
    ];

    public CompanionIntentRoutingResult Route(string? userQuery)
    {
        var normalized = Normalize(userQuery);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            var emptyResult = BuildAmbiguousResult(["query_empty_or_whitespace"], confidence: 0.05d);
            LogRoutingDecision(userQuery, emptyResult);
            return emptyResult;
        }

        if (IsUnsupported(normalized))
        {
            var unsupportedResult = new CompanionIntentRoutingResult(
                IntentFamily: FinancialCompanionIntent.Unsupported,
                PrimaryIntent: FinancialCompanionIntent.Unsupported,
                SecondaryIntents: [],
                Confidence: 0.90d,
                ReasonCodes: ["outside_supported_companion_scope"],
                IsAmbiguous: false,
                IsUnsupported: true);
            LogRoutingDecision(userQuery, unsupportedResult);
            return unsupportedResult;
        }

        var scoreByIntent = new Dictionary<FinancialCompanionIntent, double>();
        var reasonCodesByIntent = new Dictionary<FinancialCompanionIntent, HashSet<string>>();
        foreach (var pattern in Patterns)
        {
            if (!ContainsPhrase(normalized, pattern.Phrase))
            {
                continue;
            }

            scoreByIntent[pattern.Intent] = scoreByIntent.GetValueOrDefault(pattern.Intent) + pattern.Weight;
            if (!reasonCodesByIntent.TryGetValue(pattern.Intent, out var intentReasonCodes))
            {
                intentReasonCodes = new HashSet<string>(StringComparer.Ordinal);
                reasonCodesByIntent[pattern.Intent] = intentReasonCodes;
            }

            intentReasonCodes.Add(pattern.ReasonCode);
        }

        var ranked = scoreByIntent
            .Where(x => x.Value >= MatchThreshold)
            .OrderByDescending(x => x.Value)
            .ThenBy(x => (int)x.Key)
            .ToList();

        if (ranked.Count == 0)
        {
            var noMatchResult = ResolveNoMatchOutcome(normalized);
            LogRoutingDecision(userQuery, noMatchResult);
            return noMatchResult;
        }

        var primary = ranked[0].Key;
        var primaryScore = ranked[0].Value;
        var hasMixedConnector = ContainsAny(normalized, MixedIntentConnectors);
        var secondary = ranked
            .Skip(1)
            .Where(x =>
                x.Value >= MatchThreshold
                && x.Value >= MixedSecondaryAbsoluteThreshold
                && (x.Value >= primaryScore * MixedSecondaryRatioThreshold || hasMixedConnector))
            .Select(x => x.Key)
            .Distinct()
            .ToArray();

        var reasonCodes = reasonCodesByIntent.GetValueOrDefault(primary, [])!.ToList();
        var secondaryReasonCodes = secondary
            .SelectMany(intent => reasonCodesByIntent.GetValueOrDefault(intent, []))
            .Distinct(StringComparer.Ordinal);
        reasonCodes.AddRange(secondaryReasonCodes);

        var confidence = ComputeConfidence(primaryScore, ranked.Skip(1).Select(x => x.Value).FirstOrDefault(), secondary.Length > 0);
        CompanionIntentRoutingResult result;
        if (secondary.Length > 0)
        {
            reasonCodes.Add("mixed_query_detected");
            result = new CompanionIntentRoutingResult(
                IntentFamily: FinancialCompanionIntent.MixedQuery,
                PrimaryIntent: primary,
                SecondaryIntents: secondary,
                Confidence: confidence,
                ReasonCodes: reasonCodes.Distinct(StringComparer.Ordinal).ToArray(),
                IsAmbiguous: false,
                IsUnsupported: false);
        }
        else
        {
            result = new CompanionIntentRoutingResult(
                IntentFamily: primary,
                PrimaryIntent: primary,
                SecondaryIntents: [],
                Confidence: confidence,
                ReasonCodes: reasonCodes.Distinct(StringComparer.Ordinal).ToArray(),
                IsAmbiguous: false,
                IsUnsupported: false);
        }

        LogRoutingDecision(userQuery, result);
        return result;
    }

    private CompanionIntentRoutingResult ResolveNoMatchOutcome(string normalizedQuery)
    {
        if (ContainsAny(normalizedQuery, AmbiguousPhrases))
        {
            return BuildAmbiguousResult(["ambiguous_generic_money_question"], confidence: 0.32d);
        }

        if (ContainsAny(normalizedQuery, FinanceMarkers))
        {
            return BuildAmbiguousResult(["supported_domain_but_low_intent_specificity"], confidence: 0.38d);
        }

        return new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.Unsupported,
            PrimaryIntent: FinancialCompanionIntent.Unsupported,
            SecondaryIntents: [],
            Confidence: 0.82d,
            ReasonCodes: ["no_supported_financial_intent_detected"],
            IsAmbiguous: false,
            IsUnsupported: true);
    }

    private static CompanionIntentRoutingResult BuildAmbiguousResult(
        IReadOnlyList<string> reasonCodes,
        double confidence)
    {
        return new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.Ambiguous,
            PrimaryIntent: FinancialCompanionIntent.Ambiguous,
            SecondaryIntents: [],
            Confidence: Math.Clamp(confidence, 0d, 0.99d),
            ReasonCodes: reasonCodes,
            IsAmbiguous: true,
            IsUnsupported: false);
    }

    private bool IsUnsupported(string normalizedQuery)
    {
        var containsUnsupportedMarker = ContainsAny(normalizedQuery, UnsupportedMarkers);
        if (!containsUnsupportedMarker)
        {
            return false;
        }

        var financeRelated = ContainsAny(normalizedQuery, FinanceMarkers);
        return !financeRelated;
    }

    private static double ComputeConfidence(double primaryScore, double secondaryScore, bool mixed)
    {
        var normalizedPrimary = Math.Clamp(primaryScore / 2.6d, 0d, 1d);
        var margin = primaryScore <= 0d
            ? 0d
            : Math.Clamp((primaryScore - secondaryScore) / primaryScore, 0d, 1d);
        var confidence = 0.44d + (normalizedPrimary * 0.34d) + (margin * 0.22d);
        if (mixed)
        {
            confidence -= 0.06d;
        }

        return Math.Round(Math.Clamp(confidence, 0d, 0.98d), 4, MidpointRounding.AwayFromZero);
    }

    private void LogRoutingDecision(string? query, CompanionIntentRoutingResult result)
    {
        var promptSummary = BuildPromptSummary(query);
        var secondaryIntents = result.SecondaryIntents.Count == 0
            ? "none"
            : string.Join(",", result.SecondaryIntents);
        var reasonCodes = result.ReasonCodes.Count == 0
            ? "none"
            : string.Join(",", result.ReasonCodes);
        logger.LogInformation(
            "[AI_COMPANION_ROUTING] promptSummary={PromptSummary} intentFamily={IntentFamily} primaryIntent={PrimaryIntent} secondaryIntents={SecondaryIntents} confidence={Confidence} reasonCodes={ReasonCodes} ambiguous={Ambiguous} unsupported={Unsupported}",
            promptSummary,
            result.IntentFamily,
            result.PrimaryIntent,
            secondaryIntents,
            result.Confidence,
            reasonCodes,
            result.IsAmbiguous,
            result.IsUnsupported);
    }

    private static string BuildPromptSummary(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "empty";
        }

        var compact = Regex.Replace(query.Trim(), "\\s+", " ");
        if (compact.Length > 160)
        {
            compact = compact[..160];
        }

        return compact;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.Trim().ToLowerInvariant();
        var buffer = new char[lowered.Length];
        var index = 0;
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                buffer[index++] = ch;
            }
            else
            {
                buffer[index++] = ' ';
            }
        }

        var normalized = new string(buffer, 0, index);
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    private static bool ContainsAny(string value, IReadOnlyCollection<string> phrases)
    {
        return phrases.Any(phrase => ContainsPhrase(value, phrase));
    }

    private static bool ContainsPhrase(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        return haystack.Contains(needle, StringComparison.Ordinal);
    }

    private sealed record IntentPattern(
        FinancialCompanionIntent Intent,
        string Phrase,
        double Weight,
        string ReasonCode);
}
