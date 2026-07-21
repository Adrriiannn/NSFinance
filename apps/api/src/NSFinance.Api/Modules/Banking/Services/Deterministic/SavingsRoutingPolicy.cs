namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed record SavingsRoutingDecision(
    bool ShouldEvaluate,
    string RoutingTier,
    bool ProviderStructuralSupport,
    bool ProviderProductSupport,
    bool ContextualSupport,
    int RepetitionStrength,
    bool StrongPhraseSupport,
    bool PositiveSavingsEvidence,
    bool WeakSupportOnlySignalsPresent,
    bool LegacySupportOnly,
    bool ExternalCounterpartyRisk,
    int AmountRiskModifier,
    int MerchantLikelihoodScore,
    bool MerchantLikelihoodVeto,
    bool MerchantVetoOverridden,
    IReadOnlyList<string> MerchantEvidenceClasses,
    IReadOnlyList<string> PositiveEvidenceClasses,
    string? BlockedReason);

public sealed class SavingsRoutingPolicy
{
    public SavingsRoutingDecision Evaluate(
        DeterministicTransactionFeature feature,
        bool hasLegacySavingsMarker)
    {
        if (feature.IsInflow)
        {
            return BuildBlockedDecision(feature, "inflow_not_supported", hasLegacySavingsMarker);
        }

        if (feature.LooksLikeExternalCounterparty)
        {
            return BuildBlockedDecision(feature, "external_counterparty_risk", hasLegacySavingsMarker);
        }

        var repetitionStrength = ResolveRepetitionStrength(feature.RepeatedSmallAuxiliaryOutflowPatternCount);
        var contextualSupport = feature.NearbyMerchantOutflowCount > 0
                                && feature.AbsoluteAmount <= 25m
                                && !feature.HasTransferKeyword;
        // Provider reference tokens (e.g. AIB's IE references) ride on nearly
        // every SEPA row, so without savings language they must never create
        // structural support on their own - that path claimed a telecom
        // direct debit as a savings transfer in production. Savings language
        // is the gate; the marker then corroborates.
        var hasAnySavingsLanguage = feature.HasSavingsKeyword || feature.HasStrongSavingsKeyword;
        var providerStructuralSupport = feature.HasProviderTransferHint
                                        && hasAnySavingsLanguage
                                        && (feature.HasStrongSavingsKeyword
                                            || feature.HasProviderSpecificTransferMarker);
        var providerProductSupport = providerStructuralSupport
                                     || (feature.HasStrongSavingsKeyword
                                         && (feature.HasProviderTransferHint
                                             || feature.HasProviderSpecificTransferMarker
                                             || repetitionStrength >= 1))
                                     || (feature.HasSavingsKeyword && feature.HasProviderSpecificTransferMarker);
        var strongPhraseSupport = feature.HasStrongSavingsKeyword
                                  && (providerProductSupport
                                      || contextualSupport
                                      || repetitionStrength >= 1);
        var positiveSavingsEvidence = providerProductSupport
                                      || strongPhraseSupport
                                      || (repetitionStrength >= 2 && feature.HasSavingsKeyword);
        var weakSupportOnlySignalsPresent = feature.HasWeakSavingsSupportKeyword
                                            || (feature.HasSavingsKeyword && !feature.HasStrongSavingsKeyword)
                                            || feature.AbsoluteAmount <= 5m
                                            || feature.NearbyMerchantOutflowCount == 1
                                            || hasLegacySavingsMarker;
        var merchantEvidenceClasses = feature.NarrativeSignals.MerchantLikeTokens
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var positiveEvidenceClasses = BuildPositiveEvidenceClasses(
            feature,
            hasLegacySavingsMarker,
            providerStructuralSupport,
            providerProductSupport,
            contextualSupport,
            repetitionStrength,
            strongPhraseSupport);

        var merchantLikelihoodVeto = feature.MerchantLikelihoodVeto;
        var merchantVetoOverridden = merchantLikelihoodVeto
                                     && (providerStructuralSupport
                                         || (feature.HasStrongSavingsKeyword
                                             && feature.HasProviderTransferHint
                                             && (contextualSupport || repetitionStrength >= 2)));
        if (merchantLikelihoodVeto && !merchantVetoOverridden)
        {
            return BuildBlockedDecision(
                feature,
                "merchant_likelihood_veto",
                hasLegacySavingsMarker,
                providerStructuralSupport,
                providerProductSupport,
                contextualSupport,
                repetitionStrength,
                strongPhraseSupport,
                positiveSavingsEvidence,
                weakSupportOnlySignalsPresent,
                merchantVetoOverridden,
                merchantEvidenceClasses,
                positiveEvidenceClasses);
        }

        var transferContradiction = feature.HasTransferKeyword
            && feature.HasCounterpartyAccounts
            && !providerProductSupport
            && !contextualSupport;
        if (transferContradiction)
        {
            return BuildBlockedDecision(
                feature,
                "blocked_transfer_like_signal",
                hasLegacySavingsMarker,
                providerStructuralSupport,
                providerProductSupport,
                contextualSupport,
                repetitionStrength,
                strongPhraseSupport,
                positiveSavingsEvidence,
                weakSupportOnlySignalsPresent,
                merchantVetoOverridden,
                merchantEvidenceClasses,
                positiveEvidenceClasses);
        }

        var tierAProvider = providerStructuralSupport;
        var tierARepetition = repetitionStrength >= 2 && providerProductSupport;
        var tierAContextual = contextualSupport
                              && positiveSavingsEvidence;
        var tierAStrongPhrase = strongPhraseSupport && providerProductSupport;
        var shouldEvaluate = tierAProvider || tierARepetition || tierAContextual || tierAStrongPhrase;
        var routingTier = ResolveRoutingTier(tierAProvider, tierARepetition, tierAContextual, tierAStrongPhrase);
        var legacySupportOnly = hasLegacySavingsMarker
                                && !providerStructuralSupport
                                && !providerProductSupport
                                && !contextualSupport
                                && repetitionStrength == 0
                                && !strongPhraseSupport;

        return new SavingsRoutingDecision(
            ShouldEvaluate: shouldEvaluate,
            RoutingTier: routingTier,
            ProviderStructuralSupport: providerStructuralSupport,
            ProviderProductSupport: providerProductSupport,
            ContextualSupport: contextualSupport,
            RepetitionStrength: repetitionStrength,
            StrongPhraseSupport: strongPhraseSupport,
            PositiveSavingsEvidence: positiveSavingsEvidence,
            WeakSupportOnlySignalsPresent: weakSupportOnlySignalsPresent,
            LegacySupportOnly: legacySupportOnly,
            ExternalCounterpartyRisk: feature.LooksLikeExternalCounterparty,
            AmountRiskModifier: ResolveAmountRiskModifier(feature.AbsoluteAmount),
            MerchantLikelihoodScore: feature.MerchantLikelihoodScore,
            MerchantLikelihoodVeto: merchantLikelihoodVeto,
            MerchantVetoOverridden: merchantVetoOverridden,
            MerchantEvidenceClasses: merchantEvidenceClasses,
            PositiveEvidenceClasses: positiveEvidenceClasses,
            BlockedReason: shouldEvaluate ? null : "insufficient_savings_routing_evidence");
    }

    private static SavingsRoutingDecision BuildBlockedDecision(
        DeterministicTransactionFeature feature,
        string blockedReason,
        bool hasLegacySavingsMarker,
        bool providerStructuralSupport = false,
        bool providerProductSupport = false,
        bool contextualSupport = false,
        int repetitionStrength = 0,
        bool strongPhraseSupport = false,
        bool positiveSavingsEvidence = false,
        bool weakSupportOnlySignalsPresent = false,
        bool merchantVetoOverridden = false,
        IReadOnlyList<string>? merchantEvidenceClasses = null,
        IReadOnlyList<string>? positiveEvidenceClasses = null)
    {
        return new SavingsRoutingDecision(
            ShouldEvaluate: false,
            RoutingTier: "none",
            ProviderStructuralSupport: providerStructuralSupport,
            ProviderProductSupport: providerProductSupport,
            ContextualSupport: contextualSupport,
            RepetitionStrength: repetitionStrength,
            StrongPhraseSupport: strongPhraseSupport,
            PositiveSavingsEvidence: positiveSavingsEvidence,
            WeakSupportOnlySignalsPresent: weakSupportOnlySignalsPresent || hasLegacySavingsMarker,
            LegacySupportOnly: hasLegacySavingsMarker,
            ExternalCounterpartyRisk: feature.LooksLikeExternalCounterparty,
            AmountRiskModifier: ResolveAmountRiskModifier(feature.AbsoluteAmount),
            MerchantLikelihoodScore: feature.MerchantLikelihoodScore,
            MerchantLikelihoodVeto: feature.MerchantLikelihoodVeto,
            MerchantVetoOverridden: merchantVetoOverridden,
            MerchantEvidenceClasses: merchantEvidenceClasses ?? [],
            PositiveEvidenceClasses: positiveEvidenceClasses ?? [],
            BlockedReason: blockedReason);
    }

    private static IReadOnlyList<string> BuildPositiveEvidenceClasses(
        DeterministicTransactionFeature feature,
        bool hasLegacySavingsMarker,
        bool providerStructuralSupport,
        bool providerProductSupport,
        bool contextualSupport,
        int repetitionStrength,
        bool strongPhraseSupport)
    {
        var classes = new List<string>(8);
        if (providerStructuralSupport)
        {
            classes.Add("provider_structural_support");
        }

        if (providerProductSupport)
        {
            classes.Add("provider_product_support");
        }

        if (contextualSupport)
        {
            classes.Add("contextual_support");
        }

        if (repetitionStrength > 0)
        {
            classes.Add("repetition_support");
        }

        if (strongPhraseSupport)
        {
            classes.Add("strong_phrase_support");
        }

        if (feature.HasStrongSavingsKeyword)
        {
            classes.Add("strong_savings_keyword");
        }

        if (feature.NarrativeSignals.ProviderSpecificReferenceTokens.Count > 0)
        {
            classes.Add("provider_specific_reference_token");
        }

        if (hasLegacySavingsMarker)
        {
            classes.Add("legacy_savings_marker");
        }

        return classes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static int ResolveRepetitionStrength(int repeatedPatternCount)
    {
        return repeatedPatternCount switch
        {
            <= 0 => 0,
            1 => 1,
            2 => 2,
            _ => 3
        };
    }

    private static int ResolveAmountRiskModifier(decimal absoluteAmount)
    {
        if (absoluteAmount <= 5m)
        {
            return 2;
        }

        if (absoluteAmount <= 20m)
        {
            return 1;
        }

        if (absoluteAmount <= 200m)
        {
            return 0;
        }

        if (absoluteAmount <= 1000m)
        {
            return -1;
        }

        return -2;
    }

    private static string ResolveRoutingTier(
        bool tierAProvider,
        bool tierARepetition,
        bool tierAContextual,
        bool tierAStrongPhrase)
    {
        if (tierAProvider)
        {
            return "tier_a_provider_structural";
        }

        if (tierAContextual && tierARepetition)
        {
            return "tier_a_contextual_repetition";
        }

        if (tierAContextual)
        {
            return "tier_a_contextual";
        }

        if (tierARepetition)
        {
            return "tier_a_repetition";
        }

        if (tierAStrongPhrase)
        {
            return "tier_a_strong_phrase_support";
        }

        return "none";
    }
}
