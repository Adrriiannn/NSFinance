using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceAdjudicationPlanSelector
{
    FinancialAdviceAdjudicationPlan SelectPlan(
        CompanionIntentRoutingResult routing,
        IReadOnlyList<FinancialAdvicePolicyReviewedFinding> policyReviewed);
}

public sealed class FinancialAdviceAdjudicationPlanSelector(
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceAdjudicationPlanSelector
{
    private readonly CompanionAdviceOptions adviceOptions = options.Value;

    public FinancialAdviceAdjudicationPlan SelectPlan(
        CompanionIntentRoutingResult routing,
        IReadOnlyList<FinancialAdvicePolicyReviewedFinding> policyReviewed)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(policyReviewed);

        var eligible = policyReviewed
            .Where(item => item.Decision != FinancialAdvicePolicyDecision.Rejected)
            .Where(item => item.Finding.AiAdjudicationAllowed)
            .OrderByDescending(item => item.Finding.PriorityScore)
            .Take(Math.Clamp(adviceOptions.MaxAdjudicatedFindings, 1, 6))
            .ToArray();
        if (eligible.Length == 0)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                TargetFindingIds: [],
                ReasonCodes: ["adjudication_skip_no_eligible_findings"]);
        }

        var onlyLowImpact = eligible.All(item =>
            item.Finding.FindingType is FinancialAdviceFindingType.NoMaterialIssueDetected
                or FinancialAdviceFindingType.InsufficientEvidence);
        if (onlyLowImpact)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                TargetFindingIds: [],
                ReasonCodes: ["adjudication_skip_low_impact_findings"]);
        }

        var highImpactRequiresReview = eligible.Any(item =>
            item.Finding.Severity >= FinancialAdviceSeverity.High
            && item.Finding.Confidence < adviceOptions.HighConfidenceSkipThreshold);
        if (highImpactRequiresReview)
        {
            return new FinancialAdviceAdjudicationPlan(
                Mode: FinancialAdviceAdjudicationMode.Required,
                TargetFindingIds: eligible.Select(item => item.Finding.FindingId).ToArray(),
                ReasonCodes: ["adjudication_required_high_impact"]);
        }

        var hasBorderlineConfidence = eligible.Any(item =>
            item.Finding.Confidence >= adviceOptions.BorderlineConfidenceThreshold
            && item.Finding.Confidence < adviceOptions.HighConfidenceSkipThreshold);
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
}
