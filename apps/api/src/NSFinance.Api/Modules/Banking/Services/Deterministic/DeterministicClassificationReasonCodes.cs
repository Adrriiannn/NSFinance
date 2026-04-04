namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public static class DeterministicClassificationReasonCodes
{
    public const string MatchedExactInverseAmount = "matched_exact_inverse_amount";
    public const string MatchedMutualBestCandidate = "matched_mutual_best_candidate";
    public const string MatchedDuplicateClusterStablePairing = "matched_duplicate_cluster_stable_pairing";
    public const string MatchedSavingsKeywordSignal = "matched_savings_keyword_signal";
    public const string MatchedSavingsOneSidedSignal = "matched_savings_one_sided_signal";
    public const string MatchedSavingsContextualPattern = "matched_savings_contextual_pattern";
    public const string DeferredMissingCounterparty = "deferred_missing_counterparty";
    public const string DeferredPendingBookedContext = "deferred_pending_booked_context";
    public const string DeferredStrongSavingsMissingCounterparty = "deferred_strong_savings_missing_counterparty";
    public const string RejectedAmbiguousCandidates = "rejected_ambiguous_candidates";
    public const string RejectedAmbiguousDuplicateCluster = "rejected_ambiguous_duplicate_cluster";
    public const string EvaluatedUnsupportedFamily = "evaluated_unsupported_family";
    public const string EvaluatedInsufficientSignals = "insufficient_transfer_signals";
    public const string EvaluatedSavingsInsufficientSignals = "insufficient_savings_signals";
}
