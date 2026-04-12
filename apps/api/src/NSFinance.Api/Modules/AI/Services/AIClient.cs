using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence.Entities;
using System.Linq;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIClient(
    IEnumerable<IAIProviderTransport> providers,
    IOptions<AIIntegrationOptions> options,
    IAIProviderCircuitBreaker circuitBreaker,
    IOperationalFailureRecorder failureRecorder,
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
            await failureRecorder.RecordAsync(
                new OperationalFailureRecordInput(
                    OperationalFailureArea.AIProvider,
                    OperationalFailureSeverity.Error,
                    "provider_not_registered",
                    $"provider_not_registered:{selectedProvider}",
                    request.CorrelationId,
                    request.TaskType.ToString(),
                    "Configured AI provider is not registered.",
                    null),
                cancellationToken);

            return AIResponse.Failed(
                provider: selectedProvider.ToString(),
                model: route.Model,
                deployment: route.Deployment,
                failureReason: "Configured AI provider is not registered.",
                wasMocked: selectedProvider == AIProviderKind.Mock);
        }

        var nowUtc = DateTime.UtcNow;
        if (circuitBreaker.TryGetOpenState(selectedProvider, nowUtc, out var retryAfter))
        {
            var failureReason = $"Provider circuit open. Retry after {(int)Math.Ceiling(retryAfter.TotalSeconds)}s.";
            logger.LogWarning(
                "AI provider circuit open provider={Provider} task={TaskType} correlationId={CorrelationId} retryAfterMs={RetryAfterMs}",
                selectedProvider,
                request.TaskType,
                request.CorrelationId,
                (int)retryAfter.TotalMilliseconds);

            await failureRecorder.RecordAsync(
                new OperationalFailureRecordInput(
                    OperationalFailureArea.AIProvider,
                    OperationalFailureSeverity.Warning,
                    "provider_circuit_open",
                    $"provider_circuit_open:{selectedProvider}",
                    request.CorrelationId,
                    request.TaskType.ToString(),
                    failureReason,
                    null),
                cancellationToken);

            return AIResponse.Failed(
                provider: selectedProvider.ToString(),
                model: route.Model,
                deployment: route.Deployment,
                failureReason: failureReason,
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
                    circuitBreaker.RecordSuccess(selectedProvider, DateTime.UtcNow);
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
                circuitBreaker.RecordFailure(selectedProvider, response.FailureReason, DateTime.UtcNow, config.Execution);
                logger.LogWarning(
                    "AI call failed attempt={Attempt}/{MaxAttempts} provider={Provider} task={TaskType} correlationId={CorrelationId} reason={FailureReason}",
                    attempt,
                    maxAttempts,
                    selectedProvider,
                    request.TaskType,
                    request.CorrelationId,
                    response.FailureReason);

                if (!ShouldRetry(response.FailureReason))
                {
                    break;
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                circuitBreaker.RecordFailure(selectedProvider, "timeout", DateTime.UtcNow, config.Execution);
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
                circuitBreaker.RecordFailure(selectedProvider, ex.Message, DateTime.UtcNow, config.Execution);
                logger.LogWarning(
                    ex,
                    "AI call exception attempt={Attempt}/{MaxAttempts} provider={Provider} task={TaskType} correlationId={CorrelationId}",
                    attempt,
                    maxAttempts,
                    selectedProvider,
                    request.TaskType,
                    request.CorrelationId);

                if (!ShouldRetry(ex.Message))
                {
                    break;
                }
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs * attempt, cancellationToken);
            }
        }

        if (lastFailureResponse is not null)
        {
            await RecordProviderFailureAsync(
                selectedProvider,
                request,
                route,
                lastFailureResponse.FailureReason ?? "ai_call_failed",
                cancellationToken);
            return lastFailureResponse;
        }

        await RecordProviderFailureAsync(
            selectedProvider,
            request,
            route,
            lastException?.Message ?? "AI request failed.",
            cancellationToken);

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

    private static bool ShouldRetry(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return true;
        }

        if (failureReason.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("not registered", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("schema", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("401", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task RecordProviderFailureAsync(
        AIProviderKind provider,
        AIRequest request,
        AIModelRoute route,
        string failureReason,
        CancellationToken cancellationToken)
    {
        await failureRecorder.RecordAsync(
            new OperationalFailureRecordInput(
                OperationalFailureArea.AIProvider,
                OperationalFailureSeverity.Error,
                "provider_call_failed",
                $"provider_call_failed:{provider}:{request.TaskType}:{route.Deployment}:{NormalizeFingerprintComponent(failureReason)}",
                request.CorrelationId,
                request.TaskType.ToString(),
                failureReason,
                $"{{\"provider\":\"{provider}\",\"task\":\"{request.TaskType}\",\"deployment\":\"{route.Deployment}\"}}"),
            cancellationToken);
    }

    private static string NormalizeFingerprintComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim().ToLowerInvariant().Replace(' ', '_');
        normalized = new string(normalized.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == ':').ToArray());
        if (normalized.Length > 80)
        {
            normalized = normalized[..80];
        }

        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}
