using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Tests.Unit;

public class DeterministicEnrichmentContinuationPolicyTests
{
    [Theory]
    [InlineData(10, 1, 0, 0, 0, 0, 0, true)]
    [InlineData(10, 0, 7, 0, 0, 0, 0, true)]
    [InlineData(10, 0, 0, 1, 0, 0, 0, true)]
    [InlineData(10, 0, 0, 0, 2, 0, 0, true)]
    [InlineData(10, 0, 0, 0, 0, 4, 0, true)]
    [InlineData(10, 0, 0, 0, 0, 0, 3, true)]
    [InlineData(4, 0, 0, 0, 0, 0, 0, false)]
    [InlineData(0, 0, 0, 0, 0, 0, 0, false)]
    public void ShouldContinue_UsesRowDebtSignals(
        int rowsRemaining,
        int rowsActionableRemaining,
        int rowsNotEvaluated,
        int rowsEvaluating,
        int rowsVersionBehind,
        int rowsMarkedForReclassification,
        int rowsSupersededRecomputeRequired,
        bool expected)
    {
        var shouldContinue = DeterministicEnrichmentContinuationPolicy.ShouldContinue(
            rowsRemaining,
            rowsActionableRemaining,
            rowsNotEvaluated,
            rowsEvaluating,
            rowsVersionBehind,
            rowsMarkedForReclassification,
            rowsSupersededRecomputeRequired);

        Assert.Equal(expected, shouldContinue);
    }

    [Theory]
    [InlineData(10, 3, 0, 0, 0, 0, 0, "actionable_remaining_rows")]
    [InlineData(10, 0, 3, 0, 0, 0, 0, "not_evaluated_rows_remaining")]
    [InlineData(10, 0, 0, 0, 2, 0, 0, "version_behind_rows_remaining")]
    [InlineData(10, 0, 0, 0, 0, 1, 0, "explicit_recompute_rows_remaining")]
    [InlineData(10, 0, 0, 0, 0, 0, 2, "explicit_recompute_rows_remaining")]
    [InlineData(10, 0, 0, 1, 0, 0, 0, "evaluating_rows_remaining")]
    [InlineData(4, 0, 0, 0, 0, 0, 0, "deferred_only_remaining_rows")]
    [InlineData(0, 0, 0, 0, 0, 0, 0, "no_remaining_rows")]
    public void ResolveReason_ExplainsContinuationDecision(
        int rowsRemaining,
        int rowsActionableRemaining,
        int rowsNotEvaluated,
        int rowsEvaluating,
        int rowsVersionBehind,
        int rowsMarkedForReclassification,
        int rowsSupersededRecomputeRequired,
        string expectedReason)
    {
        var reason = DeterministicEnrichmentContinuationPolicy.ResolveReason(
            rowsRemaining,
            rowsActionableRemaining,
            rowsNotEvaluated,
            rowsEvaluating,
            rowsVersionBehind,
            rowsMarkedForReclassification,
            rowsSupersededRecomputeRequired);

        Assert.Equal(expectedReason, reason);
    }
}
