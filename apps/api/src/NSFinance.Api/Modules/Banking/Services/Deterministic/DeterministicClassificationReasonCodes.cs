namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public static class DeterministicClassificationReasonCodes
{
    public const string MatchedExactInverseAmount = "matched_exact_inverse_amount";
    public const string MatchedMutualBestCandidate = "matched_mutual_best_candidate";
    public const string MatchedSavingsKeywordSignal = "matched_savings_keyword_signal";
    public const string MatchedSavingsOneSidedSignal = "matched_savings_one_sided_signal";
    public const string DeferredMissingCounterparty = "deferred_missing_counterparty";
    public const string DeferredPendingBookedContext = "deferred_pending_booked_context";
    public const string RejectedAmbiguousCandidates = "rejected_ambiguous_candidates";
    public const string EvaluatedUnsupportedFamily = "evaluated_unsupported_family";
    public const string EvaluatedInsufficientSignals = "insufficient_transfer_signals";
}
