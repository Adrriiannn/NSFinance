using System.Text.Json;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class SavingsTransferClassifier
{
    public DeterministicClassificationOutcome? Classify(
        DeterministicTransactionFeature feature,
        IReadOnlyDictionary<Guid, DeterministicTransactionFeature> featuresById,
        Guid? linkedTransactionId,
        bool hasLegacySavingsMarker)
    {
        var pairedCounterpart = linkedTransactionId.HasValue && featuresById.ContainsKey(linkedTransactionId.Value)
            ? linkedTransactionId
            : FindSavingsCounterpart(feature, featuresById.Values);

        var providerStructuralSignal = feature.HasProviderTransferHint
            && (feature.HasStrongSavingsKeyword || hasLegacySavingsMarker)
            && !feature.LooksLikeExternalCounterparty;
        var contextualRoundupSignal = feature.IsOutflow
            && feature.AbsoluteAmount <= 5m
            && feature.NearbyMerchantOutflowCount > 0
            && feature.RepeatedSmallAuxiliaryOutflowPatternCount >= 2
            && !feature.LooksLikeExternalCounterparty;
        var repeatedBehaviorSignal = feature.IsOutflow
            && feature.AbsoluteAmount <= 10m
            && feature.RepeatedSmallAuxiliaryOutflowPatternCount >= 3
            && feature.NearbyMerchantOutflowCount > 0
            && !feature.LooksLikeExternalCounterparty;

        if (pairedCounterpart.HasValue)
        {
            var groupId = BuildPairGroupId(feature.TransactionId, pairedCounterpart.Value);
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.ClassifiedMatchedRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "savings_transfer.paired_structural_v3",
                ReasonCode: DeterministicClassificationReasonCodes.MatchedSavingsKeywordSignal,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    evidenceClass = "paired_internal_savings_movement",
                    paired = true,
                    candidateId = pairedCounterpart.Value,
                    providerStructuralSignal,
                    contextualRoundupSignal,
                    repeatedBehaviorSignal,
                    hasLegacySavingsMarker
                }),
                MatchScore: 10,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
                LinkedTransactionId: pairedCounterpart.Value,
                RelationshipType: "savings_transfer",
                RelationshipGroupId: groupId);
        }

        var hasStrongContextualSignal = providerStructuralSignal || contextualRoundupSignal || repeatedBehaviorSignal;
        if (!hasStrongContextualSignal)
        {
            return null;
        }

        if (feature.HasCounterpartyAccounts && feature.IsBooked && feature.IsOutflow)
        {
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.DeferredWaitingForCounterparty,
                Terminal: false,
                RetryEligible: true,
                RuleKey: "savings_transfer.pending_counterparty_structural_v3",
                ReasonCode: DeterministicClassificationReasonCodes.DeferredStrongSavingsMissingCounterparty,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    evidenceClass = "strong_savings_signal_missing_counterparty",
                    paired = false,
                    providerStructuralSignal,
                    contextualRoundupSignal,
                    repeatedBehaviorSignal,
                    feature.NearbyMerchantOutflowCount,
                    feature.RepeatedSmallAuxiliaryOutflowPatternCount,
                    hasLegacySavingsMarker
                }),
                MatchScore: 7,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
                LinkedTransactionId: null,
                RelationshipType: "savings_transfer",
                RelationshipGroupId: null);
        }

        if (contextualRoundupSignal || repeatedBehaviorSignal)
        {
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.ClassifiedMatchedRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "savings_transfer.contextual_pattern_v3",
                ReasonCode: DeterministicClassificationReasonCodes.MatchedSavingsContextualPattern,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    evidenceClass = "contextual_cooccurrence",
                    paired = false,
                    contextualRoundupSignal,
                    repeatedBehaviorSignal,
                    feature.NearbyMerchantOutflowCount,
                    feature.RepeatedSmallAuxiliaryOutflowPatternCount
                }),
                MatchScore: 8,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
                LinkedTransactionId: null,
                RelationshipType: "savings_transfer",
                RelationshipGroupId: null);
        }

        return null;
    }

    private static Guid? FindSavingsCounterpart(
        DeterministicTransactionFeature source,
        IEnumerable<DeterministicTransactionFeature> candidates)
    {
        return candidates
            .Where(candidate =>
                candidate.TransactionId != source.TransactionId
                && candidate.Currency == source.Currency
                && candidate.AbsoluteAmount == source.AbsoluteAmount
                && candidate.IsOutflow != source.IsOutflow
                && candidate.FinancialAccountId != source.FinancialAccountId
                && !candidate.LooksLikeExternalCounterparty
                && Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours) <= DeterministicCategorizationConstants.TransferCandidateWindowHours)
            .OrderByDescending(candidate => candidate.HasStrongSavingsKeyword)
            .ThenByDescending(candidate => candidate.HasProviderTransferHint)
            .ThenBy(candidate => Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalMinutes))
            .Select(candidate => (Guid?)candidate.TransactionId)
            .FirstOrDefault();
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
