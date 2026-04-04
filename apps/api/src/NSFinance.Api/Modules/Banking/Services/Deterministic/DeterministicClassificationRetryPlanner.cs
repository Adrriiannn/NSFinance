using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class DeterministicClassificationRetryPlanner
{
    public bool IsRetryEligible(DeterministicClassificationStatus status, bool hasCounterpartyAccounts, bool isPending)
    {
        return status switch
        {
            DeterministicClassificationStatus.DeferredWaitingForCounterparty => hasCounterpartyAccounts,
            DeterministicClassificationStatus.DeferredWaitingForMoreContext => true,
            DeterministicClassificationStatus.SupersededRecomputeRequired => true,
            DeterministicClassificationStatus.RejectedAmbiguousMatch => false,
            DeterministicClassificationStatus.EvaluatedNoMatchingRule => false,
            DeterministicClassificationStatus.ClassifiedMatchedRule => false,
            DeterministicClassificationStatus.NotEvaluated => true,
            DeterministicClassificationStatus.Evaluating => true,
            _ => isPending
        };
    }

    public static bool IsTerminal(DeterministicClassificationStatus status)
    {
        return status is DeterministicClassificationStatus.ClassifiedMatchedRule
            or DeterministicClassificationStatus.EvaluatedNoMatchingRule
            or DeterministicClassificationStatus.RejectedAmbiguousMatch;
    }
}
