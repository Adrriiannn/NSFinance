using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

internal interface IAzureOpenAIAuthStrategy
{
    Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);
    string FailureReason { get; }
}

internal sealed class AzureOpenAIApiKeyAuthStrategy(
    IOptions<AIIntegrationOptions> options) : IAzureOpenAIAuthStrategy
{
    public string FailureReason { get; private set; } = string.Empty;

    public Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apiKey = options.Value.AzureOpenAI.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            FailureReason = "Azure OpenAI API key is missing.";
            return Task.FromResult(false);
        }

        request.Headers.Add("api-key", apiKey.Trim());
        return Task.FromResult(true);
    }
}

internal sealed class AzureOpenAIManagedIdentityAuthStrategy(
    ILogger<AzureOpenAIManagedIdentityAuthStrategy> logger) : IAzureOpenAIAuthStrategy
{
    public string FailureReason { get; private set; } = "Managed identity auth is not configured in this environment.";

    public Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogWarning(
            "Managed identity auth path requested for Azure OpenAI but no token provider is configured.");

        request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer <managed-identity-token-not-configured>");
        return Task.FromResult(false);
    }
}
