using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionIntentNormalizer
{
    CompanionIntentNormalizedInput Normalize(string? userQuery);
}

public interface ICompanionIntentSignalExtractor
{
    CompanionIntentSignalExtractionResult Extract(CompanionIntentNormalizedInput normalizedInput);
}

public interface ICompanionIntentScorer
{
    CompanionIntentScoringResult Score(CompanionIntentSignalExtractionResult extractionResult);
}

public interface ICompanionIntentResolutionPolicy
{
    CompanionIntentResolutionResult Resolve(
        CompanionIntentSignalExtractionResult extractionResult,
        CompanionIntentScoringResult scoringResult);
}

public sealed record CompanionIntentNormalizedInput(
    string PromptSummary,
    string NormalizedText,
    IReadOnlyList<string> Tokens,
    int OriginalLength,
    int NormalizedLength,
    bool WasTruncated,
    bool IsEmpty,
    bool IsLikelyNoisy);

public sealed record CompanionIntentSignal(
    string SignalGroup,
    FinancialCompanionIntent? Intent,
    double Weight,
    string ReasonCode);

public sealed record CompanionIntentSignalExtractionResult(
    CompanionIntentNormalizedInput NormalizedInput,
    IReadOnlyList<CompanionIntentSignal> Signals,
    bool HasFinanceMarker,
    bool HasUnsupportedMarker,
    bool HasMixedConnector,
    bool HasAmbiguityMarker,
    IReadOnlyList<string> SignalGroups);

public sealed record CompanionIntentScore(
    FinancialCompanionIntent Intent,
    double Score);

public sealed record CompanionIntentScoringResult(
    IReadOnlyList<CompanionIntentScore> RankedScores,
    IReadOnlyDictionary<FinancialCompanionIntent, IReadOnlyList<string>> ReasonCodesByIntent,
    string ScoreSummary);

public sealed record CompanionIntentResolutionResult(
    CompanionIntentRoutingResult Routing,
    CompanionIntentNormalizedInput Normalized,
    IReadOnlyList<string> SignalGroups,
    string ScoreSummary,
    string ResolutionPath,
    string? FallbackReason);

public sealed class CompanionIntentNormalizer : ICompanionIntentNormalizer
{
    private const int MaxNormalizedLength = 640;
    private const int MaxTokenCount = 120;
    private const int PromptSummaryLength = 160;

    public CompanionIntentNormalizedInput Normalize(string? userQuery)
    {
        var original = userQuery ?? string.Empty;
        var promptSummary = BuildPromptSummary(original);
        var originalLength = original.Length;
        if (string.IsNullOrWhiteSpace(original))
        {
            return new CompanionIntentNormalizedInput(
                PromptSummary: promptSummary,
                NormalizedText: string.Empty,
                Tokens: [],
                OriginalLength: originalLength,
                NormalizedLength: 0,
                WasTruncated: false,
                IsEmpty: true,
                IsLikelyNoisy: false);
        }

        var lowered = original.Trim().ToLowerInvariant();
        var buffer = new char[lowered.Length];
        var index = 0;
        foreach (var ch in lowered)
        {
            buffer[index++] = char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ';
        }

        var normalized = Regex.Replace(new string(buffer, 0, index), "\\s+", " ").Trim();
        var wasTruncated = false;
        if (normalized.Length > MaxNormalizedLength)
        {
            normalized = normalized[..MaxNormalizedLength].TrimEnd();
            wasTruncated = true;
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (tokens.Count > MaxTokenCount)
        {
            tokens = tokens.Take(MaxTokenCount).ToList();
            normalized = string.Join(' ', tokens);
            wasTruncated = true;
        }

        var tokenCount = tokens.Count;
        var uniqueTokenCount = tokens
            .Distinct(StringComparer.Ordinal)
            .Count();
        var repeatedTokenBurst = tokenCount >= 8 && uniqueTokenCount <= Math.Max(2, tokenCount / 4);
        var punctuationChars = original.Count(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch));
        var alphanumericChars = original.Count(char.IsLetterOrDigit);
        var punctuationHeavy = punctuationChars > Math.Max(10, alphanumericChars * 2);
        var isLikelyNoisy = repeatedTokenBurst || punctuationHeavy;

        return new CompanionIntentNormalizedInput(
            PromptSummary: promptSummary,
            NormalizedText: normalized,
            Tokens: tokens,
            OriginalLength: originalLength,
            NormalizedLength: normalized.Length,
            WasTruncated: wasTruncated,
            IsEmpty: string.IsNullOrWhiteSpace(normalized),
            IsLikelyNoisy: isLikelyNoisy);
    }

    private static string BuildPromptSummary(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "empty";
        }

        var compact = Regex.Replace(query.Trim(), "\\s+", " ");
        if (compact.Length > PromptSummaryLength)
        {
            compact = compact[..PromptSummaryLength];
        }

        return compact;
    }
}

public sealed class CompanionIntentSignalExtractor : ICompanionIntentSignalExtractor
{
    private static readonly string[] MixedConnectors =
    [
        " and ",
        " plus ",
        " also ",
        " as well as ",
        " while "
    ];

    private static readonly HashSet<string> FinanceMarkers =
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
        "debt",
        "income"
    ];

    private static readonly HashSet<string> UnsupportedMarkers =
    [
        "python",
        "programming",
        "code",
        "debug",
        "recipe",
        "translate",
        "weather",
        "movie",
        "script",
        "physics",
        "homework",
        "sports"
    ];

    private static readonly string[] AmbiguousPhrases =
    [
        "what should i do",
        "help me with my money",
        "help with money",
        "how am i doing",
        "help me",
        "advice please"
    ];

    public CompanionIntentSignalExtractionResult Extract(CompanionIntentNormalizedInput normalizedInput)
    {
        var text = normalizedInput.NormalizedText;
        var tokenSet = normalizedInput.Tokens.ToHashSet(StringComparer.Ordinal);
        var seenReasonCodes = new HashSet<string>(StringComparer.Ordinal);
        var signalGroups = new HashSet<string>(StringComparer.Ordinal);
        var signals = new List<CompanionIntentSignal>(24);

        void AddSignal(string signalGroup, FinancialCompanionIntent? intent, double weight, string reasonCode)
        {
            if (!seenReasonCodes.Add(reasonCode))
            {
                return;
            }

            signals.Add(new CompanionIntentSignal(signalGroup, intent, weight, reasonCode));
            signalGroups.Add(signalGroup);
        }

        var hasMixedConnector = ContainsAnyPhrase(text, MixedConnectors);
        var hasFinanceMarker = tokenSet.Overlaps(FinanceMarkers)
            || ContainsAnyPhrase(text, ["financially", "with my finances", "with my money"]);
        var hasUnsupportedMarker = tokenSet.Overlaps(UnsupportedMarkers)
            || ContainsAnyPhrase(text, ["sports score"]);
        var hasAmbiguityMarker = ContainsAnyPhrase(text, AmbiguousPhrases);

        if (hasMixedConnector)
        {
            AddSignal("mixed_connectors", null, 0d, "signal_mixed_connector");
        }

        if (hasFinanceMarker)
        {
            AddSignal("finance_markers", null, 0d, "signal_finance_marker");
        }

        if (hasUnsupportedMarker)
        {
            AddSignal("unsupported_markers", null, 0d, "signal_unsupported_topic");
        }

        if (hasAmbiguityMarker)
        {
            AddSignal("ambiguity_markers", null, 0d, "signal_ambiguous_phrase");
        }

        ExtractSpendingSignals(text, tokenSet, AddSignal);
        ExtractSavingsSignals(text, tokenSet, AddSignal);
        ExtractAffordabilitySignals(text, tokenSet, AddSignal);
        ExtractBudgetSignals(text, tokenSet, AddSignal);
        ExtractPlanSignals(text, tokenSet, AddSignal);
        ExtractLocalPlacesSignals(text, tokenSet, AddSignal);
        ExtractGeneralFinancialSignals(text, tokenSet, AddSignal);
        ExtractCrossSignals(text, tokenSet, AddSignal);

        return new CompanionIntentSignalExtractionResult(
            NormalizedInput: normalizedInput,
            Signals: signals,
            HasFinanceMarker: hasFinanceMarker,
            HasUnsupportedMarker: hasUnsupportedMarker,
            HasMixedConnector: hasMixedConnector,
            HasAmbiguityMarker: hasAmbiguityMarker,
            SignalGroups: signalGroups.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private static void ExtractSpendingSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["spending the most", "money going", "category breakdown", "draining my budget", "am i overspending", "overspending"]))
        {
            addSignal("spending_analysis", FinancialCompanionIntent.SpendingAnalysis, 1.65d, "signal_spending_phrase");
        }

        if (HasAnyToken(tokenSet, "spending", "overspending", "spend")
            && HasAnyToken(tokenSet, "most", "where", "going", "pattern", "breakdown", "categories", "trend"))
        {
            addSignal("spending_analysis", FinancialCompanionIntent.SpendingAnalysis, 1.25d, "signal_spending_keywords");
        }

        if (tokenSet.Contains("overspending") && (tokenSet.Contains("on") || tokenSet.Contains("food")))
        {
            addSignal("spending_analysis", FinancialCompanionIntent.SpendingAnalysis, 1.45d, "signal_spending_overspend_focus");
        }
    }

    private static void ExtractSavingsSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["cut back", "cut expenses", "cut costs", "save more", "spend less", "reduce spending", "lower expenses", "reduce first", "what should i reduce first"]))
        {
            addSignal("savings_cutback", FinancialCompanionIntent.SavingsCutbackAdvice, 1.65d, "signal_cutback_phrase");
        }

        if (HasAnyToken(tokenSet, "cut", "reduce", "save", "lower")
            && HasAnyToken(tokenSet, "expenses", "costs", "spending", "spend", "less", "back", "first"))
        {
            addSignal("savings_cutback", FinancialCompanionIntent.SavingsCutbackAdvice, 1.25d, "signal_cutback_keywords");
        }
    }

    private static void ExtractAffordabilitySignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["can i afford", "would it be okay", "still be okay", "can i buy"]))
        {
            addSignal("affordability", FinancialCompanionIntent.Affordability, 1.75d, "signal_affordability_phrase");
        }

        if (HasAnyToken(tokenSet, "afford", "affordable", "buy", "purchase")
            && HasAnyToken(tokenSet, "can", "okay", "month", "tonight", "weekend", "still"))
        {
            addSignal("affordability", FinancialCompanionIntent.Affordability, 1.2d, "signal_affordability_keywords");
        }
    }

    private static void ExtractBudgetSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["budget looking", "over budget", "under budget", "budget status", "budget health", "room do i have left", "budget do i have left", "remaining budget", "budget overspend"]))
        {
            addSignal("budget_status", FinancialCompanionIntent.BudgetStatus, 1.7d, "signal_budget_phrase");
        }

        if (HasAnyToken(tokenSet, "budget", "overspend", "underspend")
            && HasAnyToken(tokenSet, "left", "remaining", "over", "under", "status", "health", "room", "looking"))
        {
            addSignal("budget_status", FinancialCompanionIntent.BudgetStatus, 1.25d, "signal_budget_keywords");
        }
    }

    private static void ExtractPlanSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["on track", "plan going", "how far along", "savings plan", "progress toward", "progress on"]))
        {
            addSignal("plan_progress", FinancialCompanionIntent.PlanProgress, 1.6d, "signal_plan_phrase");
        }

        if (HasAnyToken(tokenSet, "plan", "goal", "target", "progress", "track")
            && HasAnyToken(tokenSet, "on", "far", "along", "going", "reach"))
        {
            addSignal("plan_progress", FinancialCompanionIntent.PlanProgress, 1.2d, "signal_plan_keywords");
        }
    }

    private static void ExtractLocalPlacesSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["nearby", "near me", "where can i go", "where should i go", "eat nearby", "places around me", "restaurant near me"]))
        {
            addSignal("local_places", FinancialCompanionIntent.LocalPlacesOutings, 1.65d, "signal_local_places_phrase");
        }

        if (HasAnyToken(tokenSet, "nearby", "near", "local", "around", "restaurant", "cafe", "bar", "outing", "place", "places")
            && HasAnyToken(tokenSet, "where", "go", "suggest", "eat", "tonight", "weekend"))
        {
            addSignal("local_places", FinancialCompanionIntent.LocalPlacesOutings, 1.2d, "signal_local_places_keywords");
        }
    }

    private static void ExtractGeneralFinancialSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        if (ContainsAnyPhrase(text, ["how am i doing financially", "smartest next step", "what should i focus on", "what should i prioritize", "financially lately", "overall finances"]))
        {
            addSignal("general_financial", FinancialCompanionIntent.GeneralFinancialQuestion, 1.5d, "signal_general_financial_phrase");
        }

        if (HasAnyToken(tokenSet, "financial", "finances", "money")
            && HasAnyToken(tokenSet, "doing", "focus", "prioritize", "next", "step", "overall", "improve"))
        {
            addSignal("general_financial", FinancialCompanionIntent.GeneralFinancialQuestion, 1.1d, "signal_general_financial_keywords");
        }
    }

    private static void ExtractCrossSignals(
        string text,
        HashSet<string> tokenSet,
        Action<string, FinancialCompanionIntent?, double, string> addSignal)
    {
        var containsNumber = tokenSet.Any(token => token.All(char.IsDigit));
        var hasBudgetConstraint = containsNumber || tokenSet.Contains("budget") || tokenSet.Contains("afford");
        var hasPlacesSignal = ContainsAnyPhrase(text, ["where can i go", "where should i go", "nearby", "near me"]);
        if (hasPlacesSignal && hasBudgetConstraint)
        {
            addSignal("affordability", FinancialCompanionIntent.Affordability, 1.15d, "signal_places_budget_constraint");
        }
    }

    private static bool HasAnyToken(HashSet<string> tokenSet, params string[] values)
    {
        foreach (var value in values)
        {
            if (tokenSet.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnyPhrase(string value, params string[] phrases)
    {
        foreach (var phrase in phrases)
        {
            if (ContainsPhrase(value, phrase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPhrase(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        return haystack.Contains(needle, StringComparison.Ordinal);
    }
}

public sealed class CompanionIntentScorer : ICompanionIntentScorer
{
    public CompanionIntentScoringResult Score(CompanionIntentSignalExtractionResult extractionResult)
    {
        var scoreByIntent = new Dictionary<FinancialCompanionIntent, double>();
        var reasonCodesByIntent = new Dictionary<FinancialCompanionIntent, HashSet<string>>();

        foreach (var signal in extractionResult.Signals)
        {
            if (signal.Intent is null || signal.Weight <= 0d)
            {
                continue;
            }

            var intent = signal.Intent.Value;
            scoreByIntent[intent] = scoreByIntent.GetValueOrDefault(intent) + signal.Weight;
            if (!reasonCodesByIntent.TryGetValue(intent, out var reasonCodes))
            {
                reasonCodes = new HashSet<string>(StringComparer.Ordinal);
                reasonCodesByIntent[intent] = reasonCodes;
            }

            reasonCodes.Add(signal.ReasonCode);
        }

        if (extractionResult.NormalizedInput.IsLikelyNoisy)
        {
            foreach (var intent in scoreByIntent.Keys.ToList())
            {
                scoreByIntent[intent] = Math.Max(0d, scoreByIntent[intent] - 0.15d);
            }
        }

        var ranked = scoreByIntent
            .Select(x => new CompanionIntentScore(x.Key, Math.Round(x.Value, 4, MidpointRounding.AwayFromZero)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => (int)x.Intent)
            .ToArray();

        var reasons = reasonCodesByIntent.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)x.Value.OrderBy(code => code, StringComparer.Ordinal).ToArray());

        var scoreSummary = ranked.Length == 0
            ? "none"
            : string.Join(
                ";",
                ranked.Select(item => $"{item.Intent}:{item.Score:0.##}"));

        return new CompanionIntentScoringResult(ranked, reasons, scoreSummary);
    }
}

public sealed class CompanionIntentResolutionPolicy : ICompanionIntentResolutionPolicy
{
    private const double MatchThreshold = 1.0d;
    private const double MixedSecondaryRatioThreshold = 0.70d;
    private const double MixedSecondaryAbsoluteThreshold = 1.25d;

    public CompanionIntentResolutionResult Resolve(
        CompanionIntentSignalExtractionResult extractionResult,
        CompanionIntentScoringResult scoringResult)
    {
        var normalized = extractionResult.NormalizedInput;
        if (normalized.IsEmpty)
        {
            return Build(
                BuildAmbiguous(["query_empty_or_whitespace"], 0.05d),
                normalized,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "empty_input",
                fallbackReason: "query_empty_or_whitespace");
        }

        if (extractionResult.HasUnsupportedMarker
            && !extractionResult.HasFinanceMarker
            && !HasQualifiedCandidate(scoringResult))
        {
            return Build(
                BuildUnsupported(["outside_supported_companion_scope"], 0.91d),
                normalized,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "unsupported_marker_without_finance",
                fallbackReason: "outside_supported_companion_scope");
        }

        var candidates = scoringResult.RankedScores
            .Where(x => x.Score >= MatchThreshold)
            .ToArray();
        if (candidates.Length == 0)
        {
            return ResolveNoMatch(extractionResult, scoringResult);
        }

        var primary = candidates[0];
        var secondaries = candidates
            .Skip(1)
            .Where(x =>
                x.Score >= MixedSecondaryAbsoluteThreshold
                && (x.Score >= primary.Score * MixedSecondaryRatioThreshold || extractionResult.HasMixedConnector))
            .Where(x => x.Intent != FinancialCompanionIntent.GeneralFinancialQuestion || extractionResult.HasMixedConnector)
            .ToArray();

        if (primary.Intent == FinancialCompanionIntent.GeneralFinancialQuestion
            && primary.Score < 1.4d
            && extractionResult.HasAmbiguityMarker)
        {
            return Build(
                BuildAmbiguous(["supported_domain_but_low_intent_specificity"], 0.38d),
                normalized,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "ambiguous_general_override",
                fallbackReason: "supported_domain_but_low_intent_specificity");
        }

        if (secondaries.Length > 0)
        {
            var reasonCodes = CollectReasonCodes(primary, secondaries, scoringResult, "mixed_query_detected");
            var confidence = ComputeConfidence(
                primary.Score,
                secondaries[0].Score,
                mixed: true,
                noisy: normalized.IsLikelyNoisy,
                truncated: normalized.WasTruncated);

            var routing = new CompanionIntentRoutingResult(
                IntentFamily: FinancialCompanionIntent.MixedQuery,
                PrimaryIntent: primary.Intent,
                SecondaryIntents: secondaries.Select(x => x.Intent).ToArray(),
                Confidence: confidence,
                ReasonCodes: reasonCodes,
                IsAmbiguous: false,
                IsUnsupported: false);
            return Build(
                routing,
                normalized,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "mixed_intent");
        }

        var singleReasonCodes = CollectReasonCodes(primary, [], scoringResult);
        var singleConfidence = ComputeConfidence(
            primary.Score,
            secondaryScore: 0d,
            mixed: false,
            noisy: normalized.IsLikelyNoisy,
            truncated: normalized.WasTruncated);
        var single = new CompanionIntentRoutingResult(
            IntentFamily: primary.Intent,
            PrimaryIntent: primary.Intent,
            SecondaryIntents: [],
            Confidence: singleConfidence,
            ReasonCodes: singleReasonCodes,
            IsAmbiguous: false,
            IsUnsupported: false);
        return Build(
            single,
            normalized,
            extractionResult.SignalGroups,
            scoringResult.ScoreSummary,
            resolutionPath: "single_intent");
    }

    private static CompanionIntentResolutionResult ResolveNoMatch(
        CompanionIntentSignalExtractionResult extractionResult,
        CompanionIntentScoringResult scoringResult)
    {
        if (extractionResult.NormalizedInput.IsLikelyNoisy)
        {
            return Build(
                BuildAmbiguous(["noisy_or_repetitive_input"], 0.24d),
                extractionResult.NormalizedInput,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "no_match_noisy",
                fallbackReason: "noisy_or_repetitive_input");
        }

        if (extractionResult.HasAmbiguityMarker)
        {
            return Build(
                BuildAmbiguous(["ambiguous_generic_money_question"], 0.32d),
                extractionResult.NormalizedInput,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "no_match_ambiguous_phrase",
                fallbackReason: "ambiguous_generic_money_question");
        }

        if (extractionResult.HasFinanceMarker)
        {
            return Build(
                BuildAmbiguous(["supported_domain_but_low_intent_specificity"], 0.38d),
                extractionResult.NormalizedInput,
                extractionResult.SignalGroups,
                scoringResult.ScoreSummary,
                resolutionPath: "no_match_finance_adjacent",
                fallbackReason: "supported_domain_but_low_intent_specificity");
        }

        return Build(
            BuildUnsupported(["no_supported_financial_intent_detected"], 0.82d),
            extractionResult.NormalizedInput,
            extractionResult.SignalGroups,
            scoringResult.ScoreSummary,
            resolutionPath: "no_match_outside_scope",
            fallbackReason: "no_supported_financial_intent_detected");
    }

    private static IReadOnlyList<string> CollectReasonCodes(
        CompanionIntentScore primary,
        IReadOnlyList<CompanionIntentScore> secondaries,
        CompanionIntentScoringResult scoringResult,
        params string[] additionalReasons)
    {
        var result = new List<string>(8);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AppendForIntent(FinancialCompanionIntent intent)
        {
            if (!scoringResult.ReasonCodesByIntent.TryGetValue(intent, out var intentReasons))
            {
                return;
            }

            foreach (var reasonCode in intentReasons)
            {
                if (seen.Add(reasonCode))
                {
                    result.Add(reasonCode);
                }
            }
        }

        AppendForIntent(primary.Intent);
        foreach (var secondary in secondaries)
        {
            AppendForIntent(secondary.Intent);
        }

        foreach (var reason in additionalReasons)
        {
            if (!string.IsNullOrWhiteSpace(reason) && seen.Add(reason))
            {
                result.Add(reason);
            }
        }

        if (result.Count == 0)
        {
            result.Add("intent_match_without_reason_code");
        }

        return result;
    }

    private static CompanionIntentRoutingResult BuildAmbiguous(IReadOnlyList<string> reasonCodes, double confidence)
    {
        return new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.Ambiguous,
            PrimaryIntent: FinancialCompanionIntent.Ambiguous,
            SecondaryIntents: [],
            Confidence: Math.Round(Math.Clamp(confidence, 0.01d, 0.99d), 4, MidpointRounding.AwayFromZero),
            ReasonCodes: reasonCodes,
            IsAmbiguous: true,
            IsUnsupported: false);
    }

    private static CompanionIntentRoutingResult BuildUnsupported(IReadOnlyList<string> reasonCodes, double confidence)
    {
        return new CompanionIntentRoutingResult(
            IntentFamily: FinancialCompanionIntent.Unsupported,
            PrimaryIntent: FinancialCompanionIntent.Unsupported,
            SecondaryIntents: [],
            Confidence: Math.Round(Math.Clamp(confidence, 0.01d, 0.99d), 4, MidpointRounding.AwayFromZero),
            ReasonCodes: reasonCodes,
            IsAmbiguous: false,
            IsUnsupported: true);
    }

    private static double ComputeConfidence(
        double primaryScore,
        double secondaryScore,
        bool mixed,
        bool noisy,
        bool truncated)
    {
        var normalizedPrimary = Math.Clamp(primaryScore / 3.2d, 0d, 1d);
        var margin = primaryScore <= 0d
            ? 0d
            : Math.Clamp((primaryScore - secondaryScore) / primaryScore, 0d, 1d);
        var confidence = 0.40d + (normalizedPrimary * 0.38d) + (margin * 0.22d);
        if (mixed)
        {
            confidence -= 0.07d;
        }

        if (noisy)
        {
            confidence -= 0.12d;
        }

        if (truncated)
        {
            confidence -= 0.04d;
        }

        return Math.Round(Math.Clamp(confidence, 0.05d, 0.98d), 4, MidpointRounding.AwayFromZero);
    }

    private static bool HasQualifiedCandidate(CompanionIntentScoringResult scoringResult)
    {
        return scoringResult.RankedScores.Any(x => x.Score >= MatchThreshold);
    }

    private static CompanionIntentResolutionResult Build(
        CompanionIntentRoutingResult routing,
        CompanionIntentNormalizedInput normalized,
        IReadOnlyList<string> signalGroups,
        string scoreSummary,
        string resolutionPath,
        string? fallbackReason = null)
    {
        return new CompanionIntentResolutionResult(
            Routing: routing,
            Normalized: normalized,
            SignalGroups: signalGroups,
            ScoreSummary: scoreSummary,
            ResolutionPath: resolutionPath,
            FallbackReason: fallbackReason);
    }
}
