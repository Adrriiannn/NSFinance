namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantAcceptancePolicy : IMerchantAcceptancePolicy
{
    public MerchantAcceptanceDecision Evaluate(MerchantInvestigationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var reasonCodes = new List<string>();
        if (!result.Succeeded)
        {
            reasonCodes.Add("investigation_failed");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                0d,
                null,
                reasonCodes);
        }

        if (result.InsufficientEvidence || result.Candidates.Count == 0)
        {
            reasonCodes.Add("insufficient_evidence");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                0d,
                null,
                reasonCodes);
        }

        var orderedCandidates = result.Candidates
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.AmbiguityScore)
            .ToArray();
        var topCandidate = orderedCandidates[0];
        var secondCandidate = orderedCandidates.Length > 1 ? orderedCandidates[1] : null;
        var ambiguityGap = secondCandidate is null
            ? 1d
            : Math.Max(0d, topCandidate.Confidence - secondCandidate.Confidence);
        var evidenceStrength = result.Evidence.Count == 0
            ? 0d
            : Math.Clamp(result.Evidence.Average(x => x.Confidence), 0d, 1d);
        var compositeConfidence = Math.Clamp(
            (topCandidate.Confidence * 0.7d)
            + (evidenceStrength * 0.2d)
            + (ambiguityGap * 0.1d),
            0d,
            1d);

        if (topCandidate.HasContradictions)
        {
            reasonCodes.Add("contradictory_evidence");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Rejected,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (topCandidate.MixedUseRisk)
        {
            reasonCodes.Add("mixed_use_risk");
        }

        if (topCandidate.Confidence >= 0.93d
            && ambiguityGap >= 0.10d
            && evidenceStrength >= 0.65d
            && !topCandidate.MixedUseRisk)
        {
            reasonCodes.Add("trusted_threshold_met");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.AcceptedTrusted,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (topCandidate.Confidence >= 0.82d
            && ambiguityGap >= 0.06d
            && evidenceStrength >= 0.5d)
        {
            reasonCodes.Add("cautious_threshold_met");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.AcceptedCautious,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (topCandidate.Confidence >= 0.55d)
        {
            reasonCodes.Add("below_acceptance_threshold");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.LowConfidence,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        reasonCodes.Add("confidence_too_low");
        return new MerchantAcceptanceDecision(
            MerchantAcceptanceDecisionType.Rejected,
            compositeConfidence,
            topCandidate,
            reasonCodes);
    }
}
