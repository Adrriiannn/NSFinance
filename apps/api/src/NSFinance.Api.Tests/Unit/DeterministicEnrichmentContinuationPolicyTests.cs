using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Tests.Unit;

public class DeterministicEnrichmentContinuationPolicyTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    public void ShouldContinue_UsesActionableRemainingTruth(int actionableRemaining, bool expected)
    {
        var shouldContinue = DeterministicEnrichmentContinuationPolicy.ShouldContinue(actionableRemaining);

        Assert.Equal(expected, shouldContinue);
    }

    [Theory]
    [InlineData(10, 3, "actionable_remaining_rows")]
    [InlineData(4, 0, "deferred_only_remaining_rows")]
    [InlineData(0, 0, "no_remaining_rows")]
    public void ResolveReason_ExplainsContinuationDecision(
        int rowsRemaining,
        int rowsActionableRemaining,
        string expectedReason)
    {
        var reason = DeterministicEnrichmentContinuationPolicy.ResolveReason(rowsRemaining, rowsActionableRemaining);

        Assert.Equal(expectedReason, reason);
    }
}
