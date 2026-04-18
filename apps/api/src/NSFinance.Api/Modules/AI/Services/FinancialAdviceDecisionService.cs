using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceDecisionService
{
    Task<FinancialAdviceDecisionResult> DecideAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        AIModelClass preferredModelClass,
        CancellationToken cancellationToken);
}

public sealed class FinancialAdviceDecisionService(
    IFinancialAdviceEngine adviceEngine,
    IFinancialAdvicePolicyService policyService,
    IFinancialAdviceAdjudicationService adjudicationService,
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceDecisionService
{
    private readonly CompanionAdviceOptions _options = options.Value;

    public async Task<FinancialAdviceDecisionResult> DecideAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        AIModelClass preferredModelClass,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(context);

        var nowUtc = DateTime.UtcNow;
        var deterministicFindings = adviceEngine.ComputeDeterministicFindings(request, routing, context, nowUtc);
        var policyReviewed = policyService.ApplyPolicy(context, deterministicFindings);
        var adjudicationPlan = BuildAdjudicationPlan(routing, policyReviewed);
        var evidenceSummary = BuildEvidenceSummary(policyReviewed);

        FinancialAdviceAdjudicationResult adjudication;
        if (!_options.EnableAiAdjudication || adjudicationPlan.Mode == FinancialAdviceAdjudicationMode.Skipped)
        {
            adjudication = new FinancialAdviceAdjudicationResult(
                UsedAi: false,
                Succeeded: true,
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                ModelUsed: "deterministic_only",
                InputTokens: 0,
                OutputTokens: 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: _options.EnableAiAdjudication
                    ? adjudicationPlan.ReasonCodes
                    : ["adjudication_disabled"]);
        }
        else
        {
            adjudication = await adjudicationService.AdjudicateAsync(
                new FinancialAdviceAdjudicationExecutionRequest(
                    UserQuery: request.UserQuery,
                    Intent: routing.PrimaryIntent,
                    Profile: context.Profile,
                    Plan: adjudicationPlan,
                    PolicyReviewedFindings: policyReviewed,
                    EvidenceSummary: evidenceSummary,
                    PreferredModelClass: preferredModelClass,
                    CorrelationId: string.IsNullOrWhiteSpace(request.CorrelationId)
                        ? Guid.NewGuid().ToString("N")
                        : request.CorrelationId,
                    Metadata: request.Metadata),
                cancellationToken);
        }

        var finalInsights = BuildFinalInsights(policyReviewed, adjudication);
        var finalWarnings = policyReviewed
            .SelectMany(item => item.Warnings.Concat(item.Exclusions))
            .Concat(adjudication.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var evidenceWindow = ResolveEvidenceWindow(finalInsights, nowUtc);
        var refreshHints = finalInsights
            .SelectMany(item => item.Freshness.InvalidationHints)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var userSafeSummary = BuildUserSafeSummary(finalInsights, adjudication);
        var conclusionSource = adjudication.UsedAi && adjudication.Succeeded
            ? FinancialAdviceConclusionSource.Adjudicated
            : FinancialAdviceConclusionSource.DeterministicOnly;
        var highestPriority = finalInsights.Count == 0 ? 0 : finalInsights.Max(item => item.PriorityScore);

        var packet = new FinancialAdviceDecisionPacket(
            ComputedAtUtc: nowUtc,
            Intent: routing.PrimaryIntent,
            ConclusionSource: conclusionSource,
            EvidenceWindow: evidenceWindow,
            DeterministicFindings: deterministicFindings,
            PolicyReviewedFindings: policyReviewed,
            Adjudication: adjudication,
            FinalInsights: finalInsights,
            EvidenceSummary: evidenceSummary,
            Warnings: finalWarnings,
            UserSafeSummary: userSafeSummary,
            HighestPriorityScore: highestPriority,
            RequiresRefresh: finalInsights.Any(item => item.Freshness.RequiresRecheck),
            RefreshHints: refreshHints);

        return new FinancialAdviceDecisionResult(
            Packet: packet,
            ModelUsed: adjudication.UsedAi ? adjudication.ModelUsed : "deterministic_only",
            InputTokens: adjudication.InputTokens,
            OutputTokens: adjudication.OutputTokens,
            Warnings: finalWarnings);
    }

    private FinancialAdviceAdjudicationPlan BuildAdjudicationPlan(
        CompanionIntentRoutingResult routing,
        IReadOnlyList<FinancialAdvicePolicyReviewedFinding> policyReviewed)
    {
        var eligible = policyReviewed
            .Where(item => item.Decision != FinancialAdvicePolicyDecision.Rejected)
            .Where(item => item.Finding.AiAdjudicationAllowed)
            .OrderByDescending(item => item.Finding.PriorityScore)
            .Take(Math.Clamp(_options.MaxAdjudicatedFindings, 1, 6))
            .ToArray();
        if (eligible.Length == 0)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                TargetFindingIds: [],
                ReasonCodes: ["adjudication_skip_no_eligible_findings"]);
        }

        var onlyLowImpact = eligible.All(item =>
            item.Finding.FindingType is FinancialAdviceFindingType.NoMaterialIssueDetected or FinancialAdviceFindingType.InsufficientEvidence);
        if (onlyLowImpact)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                TargetFindingIds: [],
                ReasonCodes: ["adjudication_skip_low_impact_findings"]);
        }

        var highImpactRequiresReview = eligible.Any(item =>
            item.Finding.Severity >= FinancialAdviceSeverity.High
            && item.Finding.Confidence < _options.HighConfidenceSkipThreshold);
        if (highImpactRequiresReview)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Required,
                TargetFindingIds: eligible.Select(item => item.Finding.FindingId).ToArray(),
                ReasonCodes: ["adjudication_required_high_impact"]);
        }

        var hasBorderlineConfidence = eligible.Any(item =>
            item.Finding.Confidence >= _options.BorderlineConfidenceThreshold
            && item.Finding.Confidence < _options.HighConfidenceSkipThreshold);
        var nuancedIntent = routing.PrimaryIntent is FinancialCompanionIntent.SavingsCutbackAdvice
            or FinancialCompanionIntent.GeneralFinancialQuestion
            or FinancialCompanionIntent.Affordability;
        if (nuancedIntent && hasBorderlineConfidence)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Optional,
                TargetFindingIds: eligible.Select(item => item.Finding.FindingId).ToArray(),
                ReasonCodes: ["adjudication_optional_nuanced_guidance"]);
        }

        var anyRecommended = eligible.Any(item => item.Finding.AiAdjudicationRecommended);
        if (anyRecommended && hasBorderlineConfidence)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Optional,
                TargetFindingIds: eligible.Select(item => item.Finding.FindingId).ToArray(),
                ReasonCodes: ["adjudication_optional_borderline_confidence"]);
        }

        return new FinancialAdviceAdjudicationPlan(
            Mode: FinancialAdviceAdjudicationMode.Skipped,
            TargetFindingIds: [],
            ReasonCodes: ["adjudication_skip_high_confidence_deterministic"]);
    }

    private static IReadOnlyList<string> BuildEvidenceSummary(IReadOnlyList<FinancialAdvicePolicyReviewedFinding> findings)
    {
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

    private static IReadOnlyList<FinancialAdviceInsightItem> BuildFinalInsights(
        IReadOnlyList<FinancialAdvicePolicyReviewedFinding> policyReviewed,
        FinancialAdviceAdjudicationResult adjudication)
    {
        var outcomeMap = adjudication.FindingOutcomes
            .ToDictionary(item => item.FindingId, item => item, StringComparer.Ordinal);
        var insights = new List<FinancialAdviceInsightItem>(policyReviewed.Count);

        foreach (var reviewed in policyReviewed)
        {
            if (reviewed.Decision == FinancialAdvicePolicyDecision.Rejected)
            {
                continue;
            }

            var finding = reviewed.Finding;
            var source = adjudication.UsedAi && adjudication.Succeeded
                ? FinancialAdviceConclusionSource.Adjudicated
                : FinancialAdviceConclusionSource.DeterministicOnly;

            if (outcomeMap.TryGetValue(finding.FindingId, out var outcome))
            {
                if (outcome.Outcome == FinancialAdviceAdjudicationOutcome.RejectConclusion)
                {
                    continue;
                }

                if (outcome.Outcome is FinancialAdviceAdjudicationOutcome.LowerConfidence or FinancialAdviceAdjudicationOutcome.InsufficientEvidence)
                {
                    var delta = outcome.ConfidenceDelta ?? -0.12d;
                    var adjustedConfidence = Math.Clamp(finding.Confidence + delta, 0d, finding.Confidence);
                    finding = finding with
                    {
                        Confidence = adjustedConfidence,
                        Severity = adjustedConfidence < 0.5d ? LowerSeverity(finding.Severity) : finding.Severity,
                        PriorityScore = RecomputePriority(
                            adjustedConfidence < 0.5d ? LowerSeverity(finding.Severity) : finding.Severity,
                            adjustedConfidence),
                        UncertaintyMarkers = finding.UncertaintyMarkers
                            .Concat(new[] { "adjudication_lowered_confidence" })
                            .Distinct(StringComparer.Ordinal)
                            .ToArray()
                    };
                }

                var summary = !string.IsNullOrWhiteSpace(outcome.RefinedSummary)
                    ? outcome.RefinedSummary!.Trim()
                    : finding.EvidenceSummary;
                var headline = BuildHeadline(finding);
                insights.Add(new FinancialAdviceInsightItem(
                    InsightId: $"insight_{finding.FindingId}",
                    FindingId: finding.FindingId,
                    FindingType: finding.FindingType,
                    Severity: finding.Severity,
                    PriorityScore: finding.PriorityScore,
                    Confidence: finding.Confidence,
                    Headline: headline,
                    Summary: summary,
                    Actions: finding.RecommendedActions,
                    Freshness: finding.Freshness,
                    ConclusionSource: source));
                continue;
            }

            insights.Add(new FinancialAdviceInsightItem(
                InsightId: $"insight_{finding.FindingId}",
                FindingId: finding.FindingId,
                FindingType: finding.FindingType,
                Severity: finding.Severity,
                PriorityScore: finding.PriorityScore,
                Confidence: finding.Confidence,
                Headline: BuildHeadline(finding),
                Summary: finding.EvidenceSummary,
                Actions: finding.RecommendedActions,
                Freshness: finding.Freshness,
                ConclusionSource: source));
        }

        return insights
            .OrderByDescending(item => item.PriorityScore)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.InsightId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildHeadline(FinancialAdviceFinding finding)
    {
        return finding.FindingType switch
        {
            FinancialAdviceFindingType.CategoryPressure => $"{finding.DomainName ?? "Category"} spend pressure detected",
            FinancialAdviceFindingType.DiscretionaryOverspend => "Discretionary spend pressure detected",
            FinancialAdviceFindingType.RecurringSpendPressure => "Recurring commitment pressure detected",
            FinancialAdviceFindingType.BudgetSlippage => "Budget slippage detected",
            FinancialAdviceFindingType.AffordabilityRisk => "Affordability pressure detected",
            FinancialAdviceFindingType.PlanDrift => "Plan drift detected",
            FinancialAdviceFindingType.PositiveProgress => "Positive financial progress detected",
            FinancialAdviceFindingType.InsufficientEvidence => "Insufficient evidence for strong guidance",
            _ => "No material issue detected"
        };
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

    private static string BuildUserSafeSummary(
        IReadOnlyList<FinancialAdviceInsightItem> insights,
        FinancialAdviceAdjudicationResult adjudication)
    {
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

    private static FinancialAdviceSeverity LowerSeverity(FinancialAdviceSeverity severity)
    {
        return severity switch
        {
            FinancialAdviceSeverity.Critical => FinancialAdviceSeverity.High,
            FinancialAdviceSeverity.High => FinancialAdviceSeverity.Moderate,
            FinancialAdviceSeverity.Moderate => FinancialAdviceSeverity.Low,
            FinancialAdviceSeverity.Low => FinancialAdviceSeverity.Info,
            _ => FinancialAdviceSeverity.Info
        };
    }

    private static int RecomputePriority(FinancialAdviceSeverity severity, double confidence)
    {
        var severityWeight = severity switch
        {
            FinancialAdviceSeverity.Critical => 95,
            FinancialAdviceSeverity.High => 82,
            FinancialAdviceSeverity.Moderate => 65,
            FinancialAdviceSeverity.Low => 45,
            _ => 25
        };
        return Math.Clamp(severityWeight + (int)Math.Round(Math.Clamp(confidence, 0d, 1d) * 10d), 1, 100);
    }
}
