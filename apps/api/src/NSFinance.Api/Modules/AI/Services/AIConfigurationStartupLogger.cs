using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIConfigurationStartupLogger(
    IOptions<AIIntegrationOptions> options,
    ILogger<AIConfigurationStartupLogger>? logger = null,
    IHostEnvironment? hostEnvironment = null) : IHostedService
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

        logger?.LogInformation(
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

        if (provider == AIProviderKind.Mock || config.UseMockProvider)
        {
            var isProduction = hostEnvironment?.IsProduction() == true;
            var isDevelopment = hostEnvironment?.IsDevelopment() != false;
            var environmentName = hostEnvironment?.EnvironmentName ?? Environments.Development;
            if (isProduction && !IsMockAllowedInProduction())
            {
                logger?.LogCritical(
                    "Mock AI provider is active in production and is blocked. Set AI_ALLOW_MOCK_PROVIDER_IN_PRODUCTION=true only for emergency diagnostics.");
                throw new InvalidOperationException("Mock AI provider is not allowed in production.");
            }

            if (!isDevelopment)
            {
                logger?.LogWarning(
                    "Mock AI provider is active in non-development environment={EnvironmentName}.",
                    environmentName);
            }
        }

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

    private static bool IsMockAllowedInProduction()
    {
        var raw = Environment.GetEnvironmentVariable("AI_ALLOW_MOCK_PROVIDER_IN_PRODUCTION");
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
