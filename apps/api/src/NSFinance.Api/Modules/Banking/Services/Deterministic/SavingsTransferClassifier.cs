using System.Text.Json;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class SavingsTransferClassifier
{
    private static readonly SavingsRoutingPolicy DefaultRoutingPolicy = new();

    public DeterministicClassificationOutcome? Classify(
        DeterministicTransactionFeature feature,
        bool hasLegacySavingsMarker)
    {
        var routingDecision = DefaultRoutingPolicy.Evaluate(feature, hasLegacySavingsMarker);
        return Classify(feature, routingDecision, hasLegacySavingsMarker);
    }

    public DeterministicClassificationOutcome? Classify(
        DeterministicTransactionFeature feature,
        SavingsRoutingDecision routingDecision,
        bool hasLegacySavingsMarker)
    {
        if (!routingDecision.ShouldEvaluate)
        {
            return null;
        }

        var providerStructuralSignal = routingDecision.ProviderStructuralSupport;
        var contextualSupportSignal = routingDecision.ContextualSupport;
        var repeatedBehaviorSignal = routingDecision.RepetitionStrength >= 2;
        var strongPhraseWithSupportSignal = routingDecision.StrongPhraseSupport;

        var repetitionScore = routingDecision.RepetitionStrength switch
        {
            <= 0 => 0,
            1 => 1,
            2 => 2,
            _ => 3
        };
        var score = 0;
        if (providerStructuralSignal)
        {
            score += 4;
        }

        if (contextualSupportSignal)
        {
            score += 2;
        }

        score += repetitionScore;
        if (strongPhraseWithSupportSignal)
        {
            score += 2;
        }

        if (hasLegacySavingsMarker
            && (providerStructuralSignal || contextualSupportSignal || routingDecision.RepetitionStrength > 0))
        {
            score += 1;
        }

        score += routingDecision.AmountRiskModifier;

        if (feature.HasTransferKeyword && feature.HasCounterpartyAccounts && feature.AccountHint is not null)
        {
            score -= 2;
        }

        if (feature.LooksLikeExternalCounterparty)
        {
            score -= 5;
        }

        var meetsProviderThreshold = providerStructuralSignal && score >= 4;
        var meetsContextThreshold = contextualSupportSignal && (routingDecision.RepetitionStrength >= 1 || strongPhraseWithSupportSignal) && score >= 5;
        var meetsRepetitionThreshold = repeatedBehaviorSignal && score >= 5;
        var meetsStrongPhraseThreshold = strongPhraseWithSupportSignal && score >= 5;

        if (meetsProviderThreshold)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.provider_structural_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal,
                score,
                feature,
                evidenceClass: "provider_structural_signal",
                routingDecision,
                providerStructuralSignal,
                contextualSupportSignal,
                repeatedBehaviorSignal,
                hasLegacySavingsMarker);
        }

        if (meetsContextThreshold)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.contextual_pattern_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsContextNearbySpend,
                score,
                feature,
                evidenceClass: "contextual_nearby_spend",
                routingDecision,
                providerStructuralSignal,
                contextualSupportSignal,
                repeatedBehaviorSignal,
                hasLegacySavingsMarker);
        }

        if (meetsRepetitionThreshold)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.repeated_auxiliary_pattern_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsRepeatedAuxiliaryPattern,
                score,
                feature,
                evidenceClass: "repeated_auxiliary_pattern",
                routingDecision,
                providerStructuralSignal,
                contextualSupportSignal,
                repeatedBehaviorSignal,
                hasLegacySavingsMarker);
        }

        if (meetsStrongPhraseThreshold)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.strong_phrase_support_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal,
                score,
                feature,
                evidenceClass: "strong_phrase_with_support_signal",
                routingDecision,
                providerStructuralSignal,
                contextualSupportSignal,
                repeatedBehaviorSignal,
                hasLegacySavingsMarker);
        }

        return null;
    }

    private static DeterministicClassificationOutcome BuildSavingsOutcome(
        string ruleKey,
        string reasonCode,
        int score,
        DeterministicTransactionFeature feature,
        string evidenceClass,
        SavingsRoutingDecision routingDecision,
        bool providerStructuralSignal,
        bool contextualSupportSignal,
        bool repeatedBehaviorSignal,
        bool hasLegacySavingsMarker)
    {
        return new DeterministicClassificationOutcome(
            DeterministicClassificationStatus.ClassifiedMatchedRule,
            Terminal: true,
            RetryEligible: false,
            RuleKey: ruleKey,
            ReasonCode: reasonCode,
            EvidenceJson: JsonSerializer.Serialize(new
            {
                family = "savings_transfer",
                evidenceClass,
                paired = false,
                routingTier = routingDecision.RoutingTier,
                providerStructuralSignal,
                contextualSupportSignal,
                repeatedBehaviorSignal,
                repetitionStrength = routingDecision.RepetitionStrength,
                amountRiskModifier = routingDecision.AmountRiskModifier,
                weakSupportOnlySignalsPresent = routingDecision.WeakSupportOnlySignalsPresent,
                externalCounterpartyRisk = routingDecision.ExternalCounterpartyRisk,
                feature.NearbyMerchantOutflowCount,
                feature.RepeatedSmallAuxiliaryOutflowPatternCount,
                legacySignalSupportOnly = hasLegacySavingsMarker
            }),
            MatchScore: score,
            ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
            ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
            LinkedTransactionId: null,
            RelationshipType: "savings_transfer",
            RelationshipGroupId: null);
    }

    public static Guid BuildPairGroupId(Guid firstId, Guid secondId)
    {
        var ordered = new[] { firstId, secondId }
            .OrderBy(x => x)
            .Select(x => x.ToString("N"))
            .ToArray();
        using var hash = System.Security.Cryptography.MD5.Create();
        var bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{ordered[0]}:{ordered[1]}"));
        return new Guid(bytes);
    }
}
