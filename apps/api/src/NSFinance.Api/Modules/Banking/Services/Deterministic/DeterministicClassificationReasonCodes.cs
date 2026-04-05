namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public static class DeterministicClassificationReasonCodes
{
    public const string TransferPairStrictMatch = "transfer_pair_strict_match";
    public const string TransferPairMutualBest = "transfer_pair_mutual_best";
    public const string TransferPairDuplicateCluster = "transfer_pair_duplicate_cluster";
    public const string TransferRejectedAmbiguous = "transfer_rejected_ambiguous";
    public const string TransferRejectedAmbiguousDuplicateCluster = "transfer_rejected_ambiguous_duplicate_cluster";
    public const string TransferRejectedNoCounterpart = "transfer_rejected_no_counterpart";

    public const string SavingsProviderStructuralSignal = "savings_provider_structural_signal";
    public const string SavingsContextNearbySpend = "savings_context_nearby_spend";
    public const string SavingsRepeatedAuxiliaryPattern = "savings_repeated_auxiliary_pattern";
    public const string SavingsRejectedTransferTakesPrecedence = "savings_rejected_transfer_takes_precedence";
    public const string SavingsRejectedInsufficientContext = "savings_rejected_insufficient_context";
    public const string SavingsRejectedMerchantLikelihoodVeto = "savings_rejected_merchant_likelihood_veto";
    public const string LegacySignalSupportOnly = "legacy_signal_support_only";

    // Compatibility aliases retained for existing tests and historical evidence references.
    public const string MatchedExactInverseAmount = TransferPairStrictMatch;
    public const string MatchedMutualBestCandidate = TransferPairMutualBest;
    public const string MatchedDuplicateClusterStablePairing = TransferPairDuplicateCluster;
    [Obsolete("Use SavingsProviderStructuralSignal in new deterministic savings rules.", false)]
    public const string MatchedSavingsKeywordSignal = SavingsProviderStructuralSignal;
    [Obsolete("Use SavingsProviderStructuralSignal in new deterministic savings rules.", false)]
    public const string MatchedSavingsOneSidedSignal = SavingsProviderStructuralSignal;
    [Obsolete("Use SavingsContextNearbySpend in new deterministic savings rules.", false)]
    public const string MatchedSavingsContextualPattern = SavingsContextNearbySpend;
    public const string DeferredMissingCounterparty = "deferred_missing_counterparty";
    public const string DeferredPendingBookedContext = "deferred_pending_booked_context";
    [Obsolete("Savings counterparty defer is deprecated for generic savings flow.", false)]
    public const string DeferredStrongSavingsMissingCounterparty = "deferred_strong_savings_missing_counterparty";
    public const string RejectedAmbiguousCandidates = TransferRejectedAmbiguous;
    public const string RejectedAmbiguousDuplicateCluster = TransferRejectedAmbiguousDuplicateCluster;
    public const string EvaluatedUnsupportedFamily = "evaluated_unsupported_family";
    public const string EvaluatedInsufficientSignals = TransferRejectedNoCounterpart;
    public const string EvaluatedSavingsInsufficientSignals = SavingsRejectedInsufficientContext;
}
