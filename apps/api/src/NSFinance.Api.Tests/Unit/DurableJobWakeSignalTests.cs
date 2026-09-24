using System.Diagnostics;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class DurableJobWakeSignalTests
{
    private static readonly TimeSpan LongIdle = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task WaitAsync_ReturnsPromptlyWhenWokenWhileWaiting()
    {
        var signal = new DurableJobWakeSignal();
        var stopwatch = Stopwatch.StartNew();

        var wait = signal.WaitAsync(LongIdle, CancellationToken.None);
        signal.Wake();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitAsync_RetainsWakeRaisedBeforeWaiting()
    {
        var signal = new DurableJobWakeSignal();
        signal.Wake();
        signal.Wake();

        await signal.WaitAsync(LongIdle, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Repeated wakes coalesce: the next wait falls back to the idle timeout.
        var stopwatch = Stopwatch.StartNew();
        await signal.WaitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task WaitAsync_ReturnsAfterIdleTimeoutWithoutWake()
    {
        var signal = new DurableJobWakeSignal();

        await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitAsync_ThrowsWhenStopping()
    {
        var signal = new DurableJobWakeSignal();
        using var stopping = new CancellationTokenSource();

        var wait = signal.WaitAsync(LongIdle, stopping.Token);
        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
