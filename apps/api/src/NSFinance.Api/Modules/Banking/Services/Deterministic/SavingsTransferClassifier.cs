using System.Text.Json;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class SavingsTransferClassifier
{
    public DeterministicClassificationOutcome? Classify(
        DeterministicTransactionFeature feature,
        bool hasLegacySavingsMarker)
    {
        if (!feature.IsOutflow || feature.LooksLikeExternalCounterparty)
        {
            return null;
        }

        var providerStructuralSignal = feature.HasProviderTransferHint
            && (feature.HasStrongSavingsKeyword || hasLegacySavingsMarker)
            && feature.AbsoluteAmount <= 50m;
        var contextualRoundupSignal = feature.IsOutflow
            && feature.AbsoluteAmount <= 5m
            && feature.NearbyMerchantOutflowCount > 0
            && feature.RepeatedSmallAuxiliaryOutflowPatternCount >= 2
            && !feature.HasCounterpartyAccounts;
        var repeatedBehaviorSignal = feature.IsOutflow
            && feature.AbsoluteAmount <= 10m
            && feature.RepeatedSmallAuxiliaryOutflowPatternCount >= 3
            && feature.NearbyMerchantOutflowCount > 0;

        if (providerStructuralSignal)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.provider_structural_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal,
                score: 9,
                feature,
                evidenceClass: "provider_structural_signal",
                providerStructuralSignal,
                contextualRoundupSignal,
                repeatedBehaviorSignal,
                hasLegacySavingsMarker);
        }

        if (repeatedBehaviorSignal)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.repeated_auxiliary_pattern_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsRepeatedAuxiliaryPattern,
                score: 8,
                feature,
                evidenceClass: "repeated_auxiliary_pattern",
                providerStructuralSignal,
                contextualRoundupSignal,
                repeatedBehaviorSignal,
                hasLegacySavingsMarker);
        }

        if (contextualRoundupSignal)
        {
            return BuildSavingsOutcome(
                ruleKey: "savings_transfer.contextual_pattern_v4",
                reasonCode: DeterministicClassificationReasonCodes.SavingsContextNearbySpend,
                score: 7,
                feature,
                evidenceClass: "contextual_nearby_spend",
                providerStructuralSignal,
                contextualRoundupSignal,
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
        bool providerStructuralSignal,
        bool contextualRoundupSignal,
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
                providerStructuralSignal,
                contextualRoundupSignal,
                repeatedBehaviorSignal,
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
