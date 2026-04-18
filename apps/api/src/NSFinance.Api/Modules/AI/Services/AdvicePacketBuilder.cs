namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdvicePacketBuildRequest(
    DateTime ComputedAtUtc,
    FinancialCompanionIntent Intent,
    IReadOnlyList<FinancialAdviceFinding> DeterministicFindings,
    IReadOnlyList<FinancialAdvicePolicyReviewedFinding> PolicyReviewedFindings,
    FinancialAdviceAdjudicationResult Adjudication,
    IReadOnlyList<string> EvidenceSummary);

public interface IAdvicePacketBuilder
{
    FinancialAdviceDecisionPacket Build(FinancialAdvicePacketBuildRequest request);
}

public sealed class AdvicePacketBuilder(
    IAdviceLifecycleMetadataBuilder lifecycleMetadataBuilder,
    IAdviceSummaryBuilder summaryBuilder) : IAdvicePacketBuilder
{
    public FinancialAdviceDecisionPacket Build(FinancialAdvicePacketBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var finalInsights = BuildFinalInsights(request.PolicyReviewedFindings, request.Adjudication);
        var finalWarnings = request.PolicyReviewedFindings
            .SelectMany(item => item.Warnings.Concat(item.Exclusions))
            .Concat(request.Adjudication.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var lifecycle = lifecycleMetadataBuilder.Build(finalInsights, request.ComputedAtUtc);
        var userSafeSummary = summaryBuilder.BuildUserSafeSummary(finalInsights, request.Adjudication);
        var conclusionSource = request.Adjudication.UsedAi && request.Adjudication.Succeeded
            ? FinancialAdviceConclusionSource.Adjudicated
            : FinancialAdviceConclusionSource.DeterministicOnly;
        var highestPriority = finalInsights.Count == 0 ? 0 : finalInsights.Max(item => item.PriorityScore);

        return new FinancialAdviceDecisionPacket(
            ComputedAtUtc: request.ComputedAtUtc,
            Intent: request.Intent,
            ConclusionSource: conclusionSource,
            EvidenceWindow: lifecycle.EvidenceWindow,
            DeterministicFindings: request.DeterministicFindings,
            PolicyReviewedFindings: request.PolicyReviewedFindings,
            Adjudication: request.Adjudication,
            FinalInsights: finalInsights,
            EvidenceSummary: request.EvidenceSummary,
            Warnings: finalWarnings,
            UserSafeSummary: userSafeSummary,
            HighestPriorityScore: highestPriority,
            RequiresRefresh: lifecycle.RequiresRefresh,
            RefreshHints: lifecycle.RefreshHints);
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

                if (outcome.Outcome is FinancialAdviceAdjudicationOutcome.LowerConfidence
                    or FinancialAdviceAdjudicationOutcome.InsufficientEvidence)
                {
                    var delta = outcome.ConfidenceDelta ?? -0.12d;
                    var adjustedConfidence = Math.Clamp(finding.Confidence + delta, 0d, finding.Confidence);
                    var adjustedSeverity = adjustedConfidence < 0.5d
                        ? LowerSeverity(finding.Severity)
                        : finding.Severity;

                    finding = finding with
                    {
                        Confidence = adjustedConfidence,
                        Severity = adjustedSeverity,
                        PriorityScore = FinancialAdvicePriorityScoring.Compute(adjustedSeverity, adjustedConfidence),
                        UncertaintyMarkers = finding.UncertaintyMarkers
                            .Concat(["adjudication_lowered_confidence"])
                            .Distinct(StringComparer.Ordinal)
                            .ToArray()
                    };
                }

                var summary = !string.IsNullOrWhiteSpace(outcome.RefinedSummary)
                    ? outcome.RefinedSummary!.Trim()
                    : finding.EvidenceSummary;
                insights.Add(
                    new FinancialAdviceInsightItem(
                        InsightId: $"insight_{finding.FindingId}",
                        FindingId: finding.FindingId,
                        FindingType: finding.FindingType,
                        Severity: finding.Severity,
                        PriorityScore: finding.PriorityScore,
                        Confidence: finding.Confidence,
                        Headline: BuildHeadline(finding),
                        Summary: summary,
                        Actions: finding.RecommendedActions,
                        Freshness: finding.Freshness,
                        ConclusionSource: source));
                continue;
            }

            insights.Add(
                new FinancialAdviceInsightItem(
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
}
