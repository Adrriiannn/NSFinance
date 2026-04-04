namespace NSFinance.Api.Persistence.Entities;

public enum DeterministicClassificationStatus
{
    NotEvaluated = 0,
    Evaluating = 1,
    ClassifiedMatchedRule = 2,
    EvaluatedNoMatchingRule = 3,
    DeferredWaitingForCounterparty = 4,
    DeferredWaitingForMoreContext = 5,
    RejectedAmbiguousMatch = 6,
    SupersededRecomputeRequired = 7
}
