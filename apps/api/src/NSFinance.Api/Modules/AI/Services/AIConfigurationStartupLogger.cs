using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIConfigurationStartupLogger(
    IOptions<AIIntegrationOptions> options,
    ILogger<AIConfigurationStartupLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        var provider = config.UseMockProvider ? AIProviderKind.Mock : config.ProviderKind;
        var endpointHost = ResolveEndpointHost(config.AzureOpenAI.Endpoint);
        var heavyModel = string.IsNullOrWhiteSpace(config.Routing.HeavyModelName)
            ? "missing"
            : config.Routing.HeavyModelName;

        logger.LogInformation(
            "AI configuration loaded provider={Provider} endpointHost={EndpointHost} heavyModel={HeavyModel}",
            provider,
            endpointHost,
            heavyModel);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string ResolveEndpointHost(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "missing";
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return "invalid";
    }
}
