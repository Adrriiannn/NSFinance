using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

internal sealed class AzureOpenAIProviderTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<AIIntegrationOptions> options,
    AzureOpenAIApiKeyAuthStrategy apiKeyAuthStrategy,
    AzureOpenAIManagedIdentityAuthStrategy managedIdentityAuthStrategy,
    ILogger<AzureOpenAIProviderTransport> logger) : IAIProviderTransport
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AIProviderKind Kind => AIProviderKind.AzureOpenAI;

    public async Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = options.Value;
        if (!config.AzureOpenAI.Enabled)
        {
            return AIResponse.Failed(
                provider: AIProviderKind.AzureOpenAI.ToString(),
                model: route.Model,
                deployment: route.Deployment,
                failureReason: "Azure OpenAI provider is disabled by configuration.");
        }

        if (string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint))
        {
            return AIResponse.Failed(
                provider: AIProviderKind.AzureOpenAI.ToString(),
                model: route.Model,
                deployment: route.Deployment,
                failureReason: "Azure OpenAI endpoint is missing.");
        }

        var endpoint = config.AzureOpenAI.Endpoint!.TrimEnd('/');
        var requestUri = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(route.Deployment)}/chat/completions?api-version={Uri.EscapeDataString(config.AzureOpenAI.ApiVersion)}";

        // Reasoning-class deployments reject any non-default temperature with
        // HTTP 400; when a deployment says so, retry once without it. The
        // serializer drops null properties, so null means "omit".
        double? temperature = request.Temperature ?? 0.2d;

        long latencyMs;
        string rawJson;
        for (var attempt = 0; ; attempt++)
        {
            var payload = new
            {
                messages = BuildMessages(request),
                temperature,
                max_completion_tokens = request.MaxOutputTokens,
                response_format = request.StructuredOutputSchemaName is null ? null : new { type = "json_object" },
                user = request.CorrelationId
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(payload, options: SerializerOptions)
            };

            httpRequest.Headers.TryAddWithoutValidation("x-correlation-id", request.CorrelationId);

            IAzureOpenAIAuthStrategy auth = config.AzureOpenAI.UseManagedIdentity
                ? managedIdentityAuthStrategy
                : apiKeyAuthStrategy;

            if (!await auth.ApplyAsync(httpRequest, cancellationToken))
            {
                return AIResponse.Failed(
                    provider: AIProviderKind.AzureOpenAI.ToString(),
                    model: route.Model,
                    deployment: route.Deployment,
                    failureReason: auth.FailureReason);
            }

            var client = httpClientFactory.CreateClient("AI.AzureOpenAI");
            var started = DateTime.UtcNow;

            using var httpResponse = await client.SendAsync(httpRequest, cancellationToken);
            latencyMs = (long)Math.Max(0, (DateTime.UtcNow - started).TotalMilliseconds);
            rawJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (httpResponse.IsSuccessStatusCode)
            {
                break;
            }

            logger.LogWarning(
                "Azure OpenAI request failed statusCode={StatusCode} task={TaskType} correlationId={CorrelationId} errorBody={ErrorBody}",
                (int)httpResponse.StatusCode,
                request.TaskType,
                request.CorrelationId,
                Truncate(rawJson, 500));

            if (attempt == 0
                && temperature is not null
                && httpResponse.StatusCode == System.Net.HttpStatusCode.BadRequest
                && RejectedParameter(rawJson) == "temperature")
            {
                logger.LogInformation(
                    "Azure OpenAI deployment {Deployment} rejects temperature; retrying without it correlationId={CorrelationId}",
                    route.Deployment,
                    request.CorrelationId);
                temperature = null;
                continue;
            }

            return new AIResponse(
                Content: null,
                StructuredPayloadJson: null,
                FinishReason: null,
                Provider: AIProviderKind.AzureOpenAI.ToString(),
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: null,
                OutputTokenEstimate: null,
                LatencyMs: latencyMs,
                WasMocked: false,
                RawDiagnostics: rawJson,
                Succeeded: false,
                FailureReason: $"Azure OpenAI HTTP {(int)httpResponse.StatusCode}");
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;

            var content = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            var finishReason = root.GetProperty("choices")[0].TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString()
                : null;

            int? promptTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out var usageElement))
            {
                if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement)
                    && promptTokensElement.TryGetInt32(out var parsedPromptTokens))
                {
                    promptTokens = parsedPromptTokens;
                }

                if (usageElement.TryGetProperty("completion_tokens", out var completionTokensElement)
                    && completionTokensElement.TryGetInt32(out var parsedCompletionTokens))
                {
                    completionTokens = parsedCompletionTokens;
                }
            }

            return new AIResponse(
                Content: content,
                StructuredPayloadJson: content,
                FinishReason: finishReason,
                Provider: AIProviderKind.AzureOpenAI.ToString(),
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: promptTokens,
                OutputTokenEstimate: completionTokens,
                LatencyMs: latencyMs,
                WasMocked: false,
                RawDiagnostics: null,
                Succeeded: !string.IsNullOrWhiteSpace(content),
                FailureReason: string.IsNullOrWhiteSpace(content) ? "Azure OpenAI returned empty content." : null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Azure OpenAI response parsing failed task={TaskType} correlationId={CorrelationId}",
                request.TaskType,
                request.CorrelationId);

            return new AIResponse(
                Content: null,
                StructuredPayloadJson: null,
                FinishReason: null,
                Provider: AIProviderKind.AzureOpenAI.ToString(),
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: null,
                OutputTokenEstimate: null,
                LatencyMs: latencyMs,
                WasMocked: false,
                RawDiagnostics: rawJson,
                Succeeded: false,
                FailureReason: "Azure OpenAI response parse failed.");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    // Azure OpenAI 400 bodies name the offending parameter:
    // {"error":{"param":"temperature","code":"unsupported_value",...}}
    private static string? RejectedParameter(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty("error", out var error)
                   && error.TryGetProperty("param", out var param)
                   && param.ValueKind == JsonValueKind.String
                ? param.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<object> BuildMessages(AIRequest request)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemInstructions))
        {
            messages.Add(new
            {
                role = "system",
                content = request.SystemInstructions.Trim()
            });
        }

        foreach (var message in request.Messages)
        {
            var role = message.Role switch
            {
                AIMessageRole.System => "system",
                AIMessageRole.Developer => "developer",
                AIMessageRole.Assistant => "assistant",
                AIMessageRole.Tool => "tool",
                _ => "user"
            };

            messages.Add(new
            {
                role,
                content = message.Content
            });
        }

        return messages;
    }
}
