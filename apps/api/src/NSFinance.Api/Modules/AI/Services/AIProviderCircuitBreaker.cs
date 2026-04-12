using System.Collections.Concurrent;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIProviderCircuitBreaker : IAIProviderCircuitBreaker
{
    private readonly ConcurrentDictionary<AIProviderKind, ProviderCircuitState> _states = new();

    public bool TryGetOpenState(AIProviderKind provider, DateTime nowUtc, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_states.TryGetValue(provider, out var state))
        {
            return false;
        }

        lock (state.Lock)
        {
            if (!state.OpenUntilUtc.HasValue)
            {
                retryAfter = TimeSpan.Zero;
                return false;
            }

            if (state.OpenUntilUtc.Value <= nowUtc)
            {
                state.OpenUntilUtc = null;
                state.ConsecutiveFailureCount = 0;
                return false;
            }

            retryAfter = state.OpenUntilUtc.Value - nowUtc;
            return true;
        }
    }

    public void RecordSuccess(AIProviderKind provider, DateTime nowUtc)
    {
        var state = _states.GetOrAdd(provider, _ => new ProviderCircuitState());
        lock (state.Lock)
        {
            state.ConsecutiveFailureCount = 0;
            state.OpenUntilUtc = null;
            state.LastFailureUtc = null;
            state.LastFailureReason = null;
            state.LastSuccessUtc = nowUtc;
        }
    }

    public void RecordFailure(AIProviderKind provider, string? failureReason, DateTime nowUtc, AIExecutionOptions options)
    {
        if (!options.CircuitBreakerEnabled)
        {
            return;
        }

        var state = _states.GetOrAdd(provider, _ => new ProviderCircuitState());
        lock (state.Lock)
        {
            state.ConsecutiveFailureCount += 1;
            state.LastFailureUtc = nowUtc;
            state.LastFailureReason = failureReason;

            var threshold = Math.Max(1, options.CircuitBreakerFailureThreshold);
            if (state.ConsecutiveFailureCount < threshold)
            {
                return;
            }

            var openSeconds = ResolveOpenSeconds(failureReason, options);
            state.OpenUntilUtc = nowUtc.AddSeconds(openSeconds);
        }
    }

    private static int ResolveOpenSeconds(string? failureReason, AIExecutionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            if (failureReason.Contains("429", StringComparison.OrdinalIgnoreCase)
                || failureReason.Contains("rate", StringComparison.OrdinalIgnoreCase)
                || failureReason.Contains("throttl", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(5, options.CircuitBreakerRateLimitOpenSeconds);
            }

            if (failureReason.Contains("401", StringComparison.OrdinalIgnoreCase)
                || failureReason.Contains("403", StringComparison.OrdinalIgnoreCase)
                || failureReason.Contains("auth", StringComparison.OrdinalIgnoreCase)
                || failureReason.Contains("credential", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(10, options.CircuitBreakerAuthOpenSeconds);
            }
        }

        return Math.Max(5, options.CircuitBreakerOpenSeconds);
    }

    private sealed class ProviderCircuitState
    {
        public object Lock { get; } = new();
        public int ConsecutiveFailureCount { get; set; }
        public DateTime? OpenUntilUtc { get; set; }
        public DateTime? LastFailureUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        public string? LastFailureReason { get; set; }
    }
}
