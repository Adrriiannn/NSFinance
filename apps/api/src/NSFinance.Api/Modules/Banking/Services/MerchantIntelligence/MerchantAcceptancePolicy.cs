namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantAcceptancePolicy : IMerchantAcceptancePolicy
{
    private static readonly HashSet<string> DangerousBroadAliasTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "amazon",
        "google",
        "apple",
        "microsoft",
        "paypal"
    };

    public MerchantAcceptanceDecision Evaluate(MerchantInvestigationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var reasonCodes = new List<string>();
        if (!result.Succeeded)
        {
            reasonCodes.Add("investigation_failed");
            if (result.ParserRejected)
            {
                reasonCodes.Add("parser_rejected_output");
            }

            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                0d,
                null,
                reasonCodes);
        }

        if (result.InsufficientEvidence || result.Candidates.Count == 0)
        {
            reasonCodes.Add("insufficient_evidence");
            reasonCodes.Add($"recommendation_{result.Recommendation.ToString().ToLowerInvariant()}");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                Math.Clamp(result.OverallConfidence, 0d, 1d),
                result.Candidates.Count > 0 ? result.Candidates[0] : null,
                reasonCodes);
        }

        var orderedCandidates = result.Candidates
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.DescriptorMatchStrength)
            .ThenByDescending(x => x.EntityMatchStrength)
            .ThenBy(x => x.AmbiguityScore)
            .ToArray();
        var topCandidate = orderedCandidates[0];
        var secondCandidate = orderedCandidates.Length > 1 ? orderedCandidates[1] : null;

        var dominanceGap = secondCandidate is null
            ? topCandidate.Confidence
            : Math.Max(0d, topCandidate.Confidence - secondCandidate.Confidence);
        var ambiguityLevel = Math.Clamp(result.AmbiguityLevel, 0d, 1d);
        var evidenceStrength = result.Evidence.Count == 0
            ? 0d
            : Math.Clamp(result.Evidence.Average(x => (x.Confidence + x.Relevance) / 2d), 0d, 1d);
        var descriptorEntityStrength = Math.Clamp(
            (Math.Clamp(topCandidate.DescriptorMatchStrength, 0d, 1d) * 0.55d)
            + (Math.Clamp(topCandidate.EntityMatchStrength, 0d, 1d) * 0.45d),
            0d,
            1d);

        var dangerousAliasSuggestionDetected = HasDangerousAliasSuggestion(topCandidate, result.AliasSuggestions);
        var recommendationIsTrustSeeking = result.Recommendation is MerchantInvestigationRecommendation.AcceptCandidate
            or MerchantInvestigationRecommendation.AcceptCautiously;

        var contradictionDetected = topCandidate.HasContradictions;
        if (contradictionDetected)
        {
            reasonCodes.Add("contradictory_evidence");
        }

        if (topCandidate.MixedUseRisk)
        {
            reasonCodes.Add("mixed_use_risk");
        }

        if (dangerousAliasSuggestionDetected)
        {
            reasonCodes.Add("dangerous_alias_suggestion_detected");
        }

        if (recommendationIsTrustSeeking)
        {
            reasonCodes.Add("ai_recommendation_trust_seeking");
        }

        var compositeConfidence = Math.Clamp(
            (Math.Clamp(topCandidate.Confidence, 0d, 1d) * 0.42d)
            + (descriptorEntityStrength * 0.22d)
            + (Math.Clamp(1d - ambiguityLevel, 0d, 1d) * 0.14d)
            + (evidenceStrength * 0.12d)
            + (Math.Clamp(dominanceGap, 0d, 1d) * 0.10d),
            0d,
            1d);

        if (contradictionDetected)
        {
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Rejected,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (dangerousAliasSuggestionDetected && result.Recommendation == MerchantInvestigationRecommendation.AcceptCandidate)
        {
            reasonCodes.Add("trusted_blocked_due_to_alias_risk");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.LowConfidence,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        var trustedEligible = topCandidate.Confidence >= 0.92d
                             && descriptorEntityStrength >= 0.88d
                             && ambiguityLevel <= 0.18d
                             && dominanceGap >= 0.12d
                             && evidenceStrength >= 0.62d
                             && !topCandidate.MixedUseRisk
                             && !dangerousAliasSuggestionDetected;

        if (trustedEligible)
        {
            reasonCodes.Add("trusted_threshold_met");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.AcceptedTrusted,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        var cautiousEligible = topCandidate.Confidence >= 0.78d
                              && descriptorEntityStrength >= 0.72d
                              && ambiguityLevel <= 0.42d
                              && dominanceGap >= 0.05d
                              && evidenceStrength >= 0.42d;

        if (cautiousEligible)
        {
            reasonCodes.Add("cautious_threshold_met");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.AcceptedCautious,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (topCandidate.Confidence >= 0.50d)
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

    private static bool HasDangerousAliasSuggestion(
        MerchantInvestigationCandidate candidate,
        IReadOnlyList<MerchantInvestigationAliasSuggestion>? rootSuggestions)
    {
        if (ContainsDangerousAlias(candidate.AliasSuggestions)
            || ContainsDangerousAlias(rootSuggestions))
        {
            return true;
        }

        foreach (var alias in candidate.AliasCandidates)
        {
            if (IsBroadDangerousAlias(alias))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDangerousAlias(IReadOnlyList<MerchantInvestigationAliasSuggestion>? suggestions)
    {
        if (suggestions is null || suggestions.Count == 0)
        {
            return false;
        }

        foreach (var suggestion in suggestions)
        {
            if (IsBroadDangerousAlias(suggestion.AliasText) && suggestion.Confidence >= 0.60d)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBroadDangerousAlias(string? aliasText)
    {
        if (string.IsNullOrWhiteSpace(aliasText))
        {
            return false;
        }

        var normalized = aliasText.Trim().ToLowerInvariant();
        if (DangerousBroadAliasTokens.Contains(normalized))
        {
            return true;
        }

        var singleToken = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return singleToken.Length == 1 && DangerousBroadAliasTokens.Contains(singleToken[0]);
    }
}
