namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed record SavingsRoutingDecision(
    bool ShouldEvaluate,
    string RoutingTier,
    bool ProviderStructuralSupport,
    bool ContextualSupport,
    int RepetitionStrength,
    bool StrongPhraseSupport,
    bool WeakSupportOnlySignalsPresent,
    bool LegacySupportOnly,
    bool ExternalCounterpartyRisk,
    int AmountRiskModifier,
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

        var providerStructuralSupport = feature.HasProviderTransferHint && feature.HasStrongSavingsKeyword;
        var contextualSupport = feature.NearbyMerchantOutflowCount > 0
                                && feature.AbsoluteAmount <= 25m
                                && !feature.HasTransferKeyword;
        var repetitionStrength = ResolveRepetitionStrength(feature.RepeatedSmallAuxiliaryOutflowPatternCount);
        var strongPhraseSupport = feature.HasStrongSavingsKeyword
                                  && (feature.HasProviderTransferHint || feature.NearbyMerchantOutflowCount > 0 || repetitionStrength > 0);
        var weakSupportOnlySignalsPresent = feature.HasWeakSavingsSupportKeyword
                                            || (feature.HasSavingsKeyword && !feature.HasStrongSavingsKeyword)
                                            || feature.AbsoluteAmount <= 5m
                                            || feature.NearbyMerchantOutflowCount == 1
                                            || hasLegacySavingsMarker;

        var transferContradiction = feature.HasTransferKeyword
            && feature.HasCounterpartyAccounts
            && !feature.HasStrongSavingsKeyword
            && !contextualSupport
            && !providerStructuralSupport
            && !strongPhraseSupport;
        if (transferContradiction)
        {
            return BuildBlockedDecision(
                feature,
                "blocked_transfer_like_signal",
                hasLegacySavingsMarker,
                providerStructuralSupport,
                contextualSupport,
                repetitionStrength,
                strongPhraseSupport,
                weakSupportOnlySignalsPresent);
        }

        var tierAProvider = providerStructuralSupport;
        var tierARepetition = repetitionStrength >= 2;
        var tierAContextual = contextualSupport
                              && (feature.HasProviderTransferHint
                                  || repetitionStrength >= 1
                                  || strongPhraseSupport);
        var tierAStrongPhrase = strongPhraseSupport
                                && (contextualSupport
                                    || providerStructuralSupport
                                    || repetitionStrength >= 1);

        var shouldEvaluate = tierAProvider || tierARepetition || tierAContextual || tierAStrongPhrase;
        var routingTier = ResolveRoutingTier(tierAProvider, tierARepetition, tierAContextual, tierAStrongPhrase);
        var legacySupportOnly = hasLegacySavingsMarker
                                && !providerStructuralSupport
                                && !contextualSupport
                                && repetitionStrength == 0
                                && !strongPhraseSupport;

        return new SavingsRoutingDecision(
            ShouldEvaluate: shouldEvaluate,
            RoutingTier: routingTier,
            ProviderStructuralSupport: providerStructuralSupport,
            ContextualSupport: contextualSupport,
            RepetitionStrength: repetitionStrength,
            StrongPhraseSupport: strongPhraseSupport,
            WeakSupportOnlySignalsPresent: weakSupportOnlySignalsPresent,
            LegacySupportOnly: legacySupportOnly,
            ExternalCounterpartyRisk: feature.LooksLikeExternalCounterparty,
            AmountRiskModifier: ResolveAmountRiskModifier(feature.AbsoluteAmount),
            BlockedReason: shouldEvaluate ? null : "insufficient_savings_routing_evidence");
    }

    private static SavingsRoutingDecision BuildBlockedDecision(
        DeterministicTransactionFeature feature,
        string blockedReason,
        bool hasLegacySavingsMarker,
        bool providerStructuralSupport = false,
        bool contextualSupport = false,
        int repetitionStrength = 0,
        bool strongPhraseSupport = false,
        bool weakSupportOnlySignalsPresent = false)
    {
        return new SavingsRoutingDecision(
            ShouldEvaluate: false,
            RoutingTier: "none",
            ProviderStructuralSupport: providerStructuralSupport,
            ContextualSupport: contextualSupport,
            RepetitionStrength: repetitionStrength,
            StrongPhraseSupport: strongPhraseSupport,
            WeakSupportOnlySignalsPresent: weakSupportOnlySignalsPresent || hasLegacySavingsMarker,
            LegacySupportOnly: hasLegacySavingsMarker,
            ExternalCounterpartyRisk: feature.LooksLikeExternalCounterparty,
            AmountRiskModifier: ResolveAmountRiskModifier(feature.AbsoluteAmount),
            BlockedReason: blockedReason);
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
