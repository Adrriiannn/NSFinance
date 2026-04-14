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
        var routing = config.Routing;
        var heavyRouteConfigured =
            !string.IsNullOrWhiteSpace(routing.HeavyModelName)
            && !string.IsNullOrWhiteSpace(routing.HeavyDeploymentName);
        var heavyEnabled = provider == AIProviderKind.Mock
            || (routing.HeavyModelEnabled ?? heavyRouteConfigured);

        logger.LogInformation(
            "AI configuration loaded provider={Provider} endpointHost={EndpointHost} useMockProvider={UseMockProvider} aliasNormalizationApplied={AliasNormalizationApplied} fastModel={FastModel} fastDeployment={FastDeployment} heavyModel={HeavyModel} heavyDeployment={HeavyDeployment} heavyEnabled={HeavyEnabled}",
            provider,
            endpointHost,
            config.UseMockProvider,
            config.AliasNormalizationApplied,
            DisplayOrMissing(routing.FastModelName),
            DisplayOrMissing(routing.FastDeploymentName),
            DisplayOrMissing(routing.HeavyModelName),
            DisplayOrMissing(routing.HeavyDeploymentName),
            heavyEnabled);

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

    private static string DisplayOrMissing(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "missing"
            : value.Trim();
    }
}
