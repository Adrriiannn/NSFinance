using System.Text.Json;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class SavingsTransferClassifier
{
    public DeterministicClassificationOutcome? Classify(
        DeterministicTransactionFeature feature,
        IReadOnlyDictionary<Guid, DeterministicTransactionFeature> featuresById,
        Guid? linkedTransactionId)
    {
        if (!feature.HasSavingsKeyword)
        {
            return null;
        }

        var savingsPairCandidate = linkedTransactionId.HasValue && featuresById.ContainsKey(linkedTransactionId.Value)
            ? linkedTransactionId
            : FindSavingsCounterpart(feature, featuresById.Values);

        if (savingsPairCandidate.HasValue)
        {
            var groupId = BuildPairGroupId(feature.TransactionId, savingsPairCandidate.Value);
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.ClassifiedMatchedRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "savings_transfer.paired_signal_v3",
                ReasonCode: DeterministicClassificationReasonCodes.MatchedSavingsKeywordSignal,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    paired = true,
                    candidateId = savingsPairCandidate.Value
                }),
                MatchScore: 10,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
                LinkedTransactionId: savingsPairCandidate.Value,
                RelationshipType: "savings_transfer",
                RelationshipGroupId: groupId);
        }

        if (feature.HasStrongSavingsKeyword)
        {
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.ClassifiedMatchedRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "savings_transfer.one_sided_signal_v3",
                ReasonCode: DeterministicClassificationReasonCodes.MatchedSavingsOneSidedSignal,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    paired = false,
                    oneSided = true,
                    strongSignal = true
                }),
                MatchScore: 8,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
                LinkedTransactionId: null,
                RelationshipType: "savings_transfer",
                RelationshipGroupId: null);
        }

        return new DeterministicClassificationOutcome(
            DeterministicClassificationStatus.DeferredWaitingForCounterparty,
            Terminal: false,
            RetryEligible: true,
            RuleKey: "savings_transfer.pending_counterparty_v3",
            ReasonCode: DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
            EvidenceJson: JsonSerializer.Serialize(new
            {
                family = "savings_transfer",
                paired = false,
                oneSided = false,
                strongSignal = false
            }),
            MatchScore: 5,
            ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
            ClassificationSubcategoryId: DeterministicCategorizationConstants.SavingsTransferSubcategoryId,
            LinkedTransactionId: null,
            RelationshipType: "savings_transfer",
            RelationshipGroupId: null);
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
                && Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalHours) <= DeterministicCategorizationConstants.TransferCandidateWindowHours)
            .OrderBy(candidate => Math.Abs((candidate.BookedAtUtc - source.BookedAtUtc).TotalMinutes))
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
