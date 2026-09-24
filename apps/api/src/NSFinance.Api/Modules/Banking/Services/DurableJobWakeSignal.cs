using System.Threading.Channels;

namespace NSFinance.Api.Modules.Banking.Services;

// Lets an in-process enqueue wake its durable worker immediately, so the idle
// database poll can stay slow without delaying user-triggered work. A wake
// raised while the worker is busy is retained, so no request is missed.
internal sealed class DurableJobWakeSignal
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    public void Wake() => _signals.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan idleTimeout, CancellationToken stoppingToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutSource.CancelAfter(idleTimeout);
        try
        {
            await _signals.Reader.WaitToReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Idle timeout elapsed; the caller runs its safety-net poll.
        }

        _signals.Reader.TryRead(out _);
    }
}
