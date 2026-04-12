namespace NSFinance.Api.Modules.AI.Services;

public interface IAIProviderCircuitBreaker
{
    bool TryGetOpenState(AIProviderKind provider, DateTime nowUtc, out TimeSpan retryAfter);
    void RecordSuccess(AIProviderKind provider, DateTime nowUtc);
    void RecordFailure(AIProviderKind provider, string? failureReason, DateTime nowUtc, AIExecutionOptions options);
}
