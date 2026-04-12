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
    private static readonly HashSet<string> GenericMerchantTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "shop",
        "store",
        "services",
        "solutions",
        "group",
        "global",
        "online",
        "digital",
        "payments",
        "payment",
        "merchant",
        "company",
        "limited",
        "ltd",
        "inc",
        "llc"
    };
    private static readonly HashSet<string> DomainNoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "www",
        "com",
        "co",
        "net",
        "org",
        "io",
        "app",
        "payments",
        "pay"
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
        var sourceTrustScore = ComputeSourceTrustScore(result.Evidence, topCandidate.EvidenceItems);
        var hasAuthoritativeSource = HasAuthoritativeSource(result.Evidence, topCandidate.EvidenceItems);
        var weakSourceProfile = sourceTrustScore < 0.55d || !hasAuthoritativeSource;
        var domainMismatchRisk = topCandidate.DomainNameMismatchRisk || HasDomainNameMismatch(topCandidate);
        var genericNameRisk = IsGenericMerchantName(topCandidate.CanonicalName);
        var suspiciousIdentityRisk = topCandidate.SuspiciousIdentityRisk
                                     || (domainMismatchRisk && weakSourceProfile)
                                     || (genericNameRisk && sourceTrustScore < 0.65d);
        var singleSourceOverconfidence = topCandidate.Confidence >= 0.92d
                                         && ((result.Evidence?.Count ?? 0) + (topCandidate.EvidenceItems?.Count ?? 0)) <= 1
                                         && sourceTrustScore < 0.70d;

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

        if (weakSourceProfile || topCandidate.WeakSourceRisk)
        {
            reasonCodes.Add("weak_source_trust_profile");
        }

        if (domainMismatchRisk)
        {
            reasonCodes.Add("domain_name_mismatch_risk");
        }

        if (genericNameRisk)
        {
            reasonCodes.Add("generic_merchant_name_risk");
        }

        if (singleSourceOverconfidence)
        {
            reasonCodes.Add("single_source_overconfidence_risk");
        }

        if (suspiciousIdentityRisk)
        {
            reasonCodes.Add("suspicious_identity_signal");
        }

        if (recommendationIsTrustSeeking)
        {
            reasonCodes.Add("ai_recommendation_trust_seeking");
        }
        else
        {
            reasonCodes.Add("ai_recommendation_non_trust");
        }

        var compositeConfidence = Math.Clamp(
            (Math.Clamp(topCandidate.Confidence, 0d, 1d) * 0.42d)
            + (descriptorEntityStrength * 0.22d)
            + (Math.Clamp(1d - ambiguityLevel, 0d, 1d) * 0.14d)
            + (((evidenceStrength * 0.50d) + (sourceTrustScore * 0.50d)) * 0.12d)
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
                MerchantAcceptanceDecisionType.Rejected,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (!recommendationIsTrustSeeking)
        {
            reasonCodes.Add("acceptance_blocked_by_recommendation");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if ((domainMismatchRisk && weakSourceProfile) || suspiciousIdentityRisk || singleSourceOverconfidence)
        {
            reasonCodes.Add("acceptance_blocked_identity_trust");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        if (genericNameRisk && !hasAuthoritativeSource)
        {
            reasonCodes.Add("acceptance_blocked_generic_name_without_authoritative_source");
            return new MerchantAcceptanceDecision(
                MerchantAcceptanceDecisionType.Unresolved,
                compositeConfidence,
                topCandidate,
                reasonCodes);
        }

        var trustedEligible = result.Recommendation == MerchantInvestigationRecommendation.AcceptCandidate
                              && topCandidate.Confidence >= 0.92d
                              && descriptorEntityStrength >= 0.88d
                              && ambiguityLevel <= 0.18d
                              && dominanceGap >= 0.12d
                              && evidenceStrength >= 0.62d
                              && sourceTrustScore >= 0.72d
                              && !topCandidate.MixedUseRisk
                              && !dangerousAliasSuggestionDetected
                              && hasAuthoritativeSource
                              && !domainMismatchRisk
                              && !genericNameRisk;

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
                              && evidenceStrength >= 0.42d
                              && sourceTrustScore >= 0.40d;
        if (cautiousEligible && topCandidate.MixedUseRisk)
        {
            cautiousEligible = ambiguityLevel <= 0.30d
                              && dominanceGap >= 0.10d
                              && evidenceStrength >= 0.50d
                              && sourceTrustScore >= 0.52d;
        }

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

    private static double ComputeSourceTrustScore(
        IReadOnlyList<MerchantInvestigationEvidence> rootEvidence,
        IReadOnlyList<MerchantInvestigationEvidence>? candidateEvidence)
    {
        var combined = new List<MerchantInvestigationEvidence>(rootEvidence.Count + (candidateEvidence?.Count ?? 0));
        combined.AddRange(rootEvidence);
        if (candidateEvidence is { Count: > 0 })
        {
            combined.AddRange(candidateEvidence);
        }

        if (combined.Count == 0)
        {
            return 0d;
        }

        return Math.Clamp(combined.Average(e =>
        {
            var trustWeight = ResolveSourceTrustLevel(e) switch
            {
                MerchantSourceTrustLevel.OfficialDomain => 1.00d,
                MerchantSourceTrustLevel.AuthoritativeListing => 0.90d,
                MerchantSourceTrustLevel.PublicDirectory => 0.66d,
                MerchantSourceTrustLevel.WeakWebMention => 0.35d,
                MerchantSourceTrustLevel.AIInferenceOnly => 0.25d,
                MerchantSourceTrustLevel.NoSource => 0.15d,
                _ => 0.45d
            };
            return Math.Clamp((e.Confidence * 0.45d) + (e.Relevance * 0.25d) + (trustWeight * 0.30d), 0d, 1d);
        }), 0d, 1d);
    }

    private static bool HasAuthoritativeSource(
        IReadOnlyList<MerchantInvestigationEvidence> rootEvidence,
        IReadOnlyList<MerchantInvestigationEvidence>? candidateEvidence)
    {
        var combined = rootEvidence.Concat(candidateEvidence ?? []);
        return combined.Any(e =>
            ResolveSourceTrustLevel(e) is MerchantSourceTrustLevel.OfficialDomain or MerchantSourceTrustLevel.AuthoritativeListing
            && e.Confidence >= 0.60d
            && e.Relevance >= 0.55d);
    }

    private static MerchantSourceTrustLevel ResolveSourceTrustLevel(MerchantInvestigationEvidence evidence)
    {
        if (evidence.SourceTrustLevel != MerchantSourceTrustLevel.Unknown)
        {
            return evidence.SourceTrustLevel;
        }

        var sourceClass = evidence.SourceClass?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sourceClass))
        {
            return MerchantSourceTrustLevel.NoSource;
        }

        if (sourceClass.Contains("official", StringComparison.Ordinal)
            || sourceClass.Contains("merchant_site", StringComparison.Ordinal)
            || sourceClass.Contains("verified_domain", StringComparison.Ordinal))
        {
            return MerchantSourceTrustLevel.OfficialDomain;
        }

        if (sourceClass.Contains("authoritative", StringComparison.Ordinal)
            || sourceClass.Contains("regulator", StringComparison.Ordinal)
            || sourceClass.Contains("government", StringComparison.Ordinal)
            || sourceClass.Contains("exchange", StringComparison.Ordinal))
        {
            return MerchantSourceTrustLevel.AuthoritativeListing;
        }

        if (sourceClass.Contains("directory", StringComparison.Ordinal)
            || sourceClass.Contains("listing", StringComparison.Ordinal))
        {
            return MerchantSourceTrustLevel.PublicDirectory;
        }

        if (sourceClass.Contains("ai", StringComparison.Ordinal)
            || sourceClass.Contains("inference", StringComparison.Ordinal)
            || sourceClass.Contains("model", StringComparison.Ordinal))
        {
            return MerchantSourceTrustLevel.AIInferenceOnly;
        }

        if (sourceClass.Contains("web", StringComparison.Ordinal)
            || sourceClass.Contains("blog", StringComparison.Ordinal)
            || sourceClass.Contains("forum", StringComparison.Ordinal)
            || sourceClass.Contains("social", StringComparison.Ordinal))
        {
            return MerchantSourceTrustLevel.WeakWebMention;
        }

        return MerchantSourceTrustLevel.Unknown;
    }

    private static bool HasDomainNameMismatch(MerchantInvestigationCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.OfficialWebsite)
            || string.IsNullOrWhiteSpace(candidate.CanonicalName))
        {
            return false;
        }

        if (!Uri.TryCreate(candidate.OfficialWebsite, UriKind.Absolute, out var uri))
        {
            return true;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var hostTokens = host
            .Replace('-', ' ')
            .Replace('.', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !DomainNoiseTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hostTokens.Length == 0)
        {
            return true;
        }

        var merchantTokens = candidate.CanonicalName
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !GenericMerchantTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (merchantTokens.Length == 0)
        {
            return true;
        }

        var overlap = merchantTokens.Any(token => hostTokens.Any(hostToken =>
            hostToken.Contains(token, StringComparison.OrdinalIgnoreCase)
            || token.Contains(hostToken, StringComparison.OrdinalIgnoreCase)));

        return !overlap;
    }

    private static bool IsGenericMerchantName(string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            return true;
        }

        var tokens = canonicalName
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        var meaningful = tokens.Count(token => token.Length >= 4 && !GenericMerchantTokens.Contains(token));
        return meaningful == 0;
    }
}
