using System.Reflection;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;

namespace NSFinance.Api.Tests.Unit;

public class DeterministicEnrichmentWorkerReasonTests
{
    [Fact]
    public void PendingFlagReason_UsesRuleVersionReason_WhenVersionBehind()
    {
        var method = typeof(BankDeterministicEnrichmentBackgroundWorker).GetMethod(
            "ResolvePendingFlagReason",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var reason = method!.Invoke(
            null,
            [
                false,
                DateTime.UtcNow,
                DeterministicCategorizationConstants.CurrentClassificationVersion - 1
            ]) as string;

        Assert.Equal(
            DeterministicReclassificationTriggerReasons.DeterministicRuleVersionChanged,
            reason);
    }
}
