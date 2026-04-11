using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIClient(
    IEnumerable<IAIProviderTransport> providers,
    IOptions<AIIntegrationOptions> options,
    ILogger<AIClient> logger) : IAIClient
{
    private readonly Dictionary<AIProviderKind, IAIProviderTransport> _providers = providers.ToDictionary(
        provider => provider.Kind,
        provider => provider);

    public async Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(route);

        var config = options.Value;
        var selectedProvider = ResolveProvider(config);
        if (!_providers.TryGetValue(selectedProvider, out var provider))
        {
            logger.LogError(
                "AI provider unavailable provider={ProviderKind} task={TaskType} correlationId={CorrelationId}",
                selectedProvider,
                request.TaskType,
                request.CorrelationId);

            return AIResponse.Failed(
                provider: selectedProvider.ToString(),
                model: route.Model,
                deployment: route.Deployment,
                failureReason: "Configured AI provider is not registered.",
                wasMocked: selectedProvider == AIProviderKind.Mock);
        }

        var maxAttempts = Math.Max(1, config.Execution.MaxRetryAttempts + 1);
        var timeoutSeconds = Math.Clamp(config.Execution.TimeoutSeconds, 5, 120);
        var delayMs = Math.Max(50, config.Execution.RetryBaseDelayMs);
        Exception? lastException = null;
        AIResponse? lastFailureResponse = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var started = DateTime.UtcNow;
                var response = await provider.SendAsync(request, route, timeoutCts.Token);
                var elapsed = (long)Math.Max(0, (DateTime.UtcNow - started).TotalMilliseconds);

                if (response.Succeeded)
                {
                    logger.LogInformation(
                        "AI call succeeded provider={Provider} task={TaskType} model={Model} deployment={Deployment} latencyMs={LatencyMs} correlationId={CorrelationId} mocked={WasMocked}",
                        response.Provider,
                        request.TaskType,
                        response.Model,
                        response.Deployment,
                        response.LatencyMs > 0 ? response.LatencyMs : elapsed,
                        request.CorrelationId,
                        response.WasMocked);
                    return response with { LatencyMs = response.LatencyMs > 0 ? response.LatencyMs : elapsed };
                }

                lastFailureResponse = response with { LatencyMs = response.LatencyMs > 0 ? response.LatencyMs : elapsed };
                logger.LogWarning(
                    "AI call failed attempt={Attempt}/{MaxAttempts} provider={Provider} task={TaskType} correlationId={CorrelationId} reason={FailureReason}",
                    attempt,
                    maxAttempts,
                    selectedProvider,
                    request.TaskType,
                    request.CorrelationId,
                    response.FailureReason);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                logger.LogWarning(
                    ex,
                    "AI call timed out attempt={Attempt}/{MaxAttempts} provider={Provider} task={TaskType} correlationId={CorrelationId}",
                    attempt,
                    maxAttempts,
                    selectedProvider,
                    request.TaskType,
                    request.CorrelationId);
            }
            catch (Exception ex)
            {
                lastException = ex;
                logger.LogWarning(
                    ex,
                    "AI call exception attempt={Attempt}/{MaxAttempts} provider={Provider} task={TaskType} correlationId={CorrelationId}",
                    attempt,
                    maxAttempts,
                    selectedProvider,
                    request.TaskType,
                    request.CorrelationId);
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs * attempt, cancellationToken);
            }
        }

        if (lastFailureResponse is not null)
        {
            return lastFailureResponse;
        }

        return AIResponse.Failed(
            provider: selectedProvider.ToString(),
            model: route.Model,
            deployment: route.Deployment,
            failureReason: lastException?.Message ?? "AI request failed.",
            wasMocked: selectedProvider == AIProviderKind.Mock);
    }

    private static AIProviderKind ResolveProvider(AIIntegrationOptions options)
    {
        if (!options.Enabled)
        {
            return AIProviderKind.Mock;
        }

        if (options.UseMockProvider)
        {
            return AIProviderKind.Mock;
        }

        return options.ProviderKind;
    }
}
