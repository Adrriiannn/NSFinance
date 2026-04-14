namespace NSFinance.Api.Modules.AI.Services;

public static class AIIntegrationOptionsNormalizer
{
    public static void Normalize(AIIntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Models ??= new AIModelNameOptions();
        options.Routing ??= new AIModelRoutingOptions();
        options.AzureOpenAI ??= new AzureOpenAIOptions();

        var providerToken = options.Provider?.Trim();
        if (!string.IsNullOrWhiteSpace(providerToken)
            && Enum.TryParse<AIProviderKind>(providerToken, true, out var parsedProvider))
        {
            options.ProviderKind = parsedProvider;
            options.UseMockProvider = parsedProvider == AIProviderKind.Mock;
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            options.AzureOpenAI.Endpoint = options.Endpoint.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.AzureOpenAI.ApiKey = options.ApiKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.Models.Fast))
        {
            options.Routing.FastModelName = options.Models.Fast.Trim();
        }

        if (string.IsNullOrWhiteSpace(options.Models.Heavy)
            && !string.IsNullOrWhiteSpace(options.Routing.HeavyModelName))
        {
            options.Models.Heavy = options.Routing.HeavyModelName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.Models.Heavy))
        {
            options.Routing.HeavyModelName = options.Models.Heavy.Trim();
            options.Routing.HeavyModelEnabled = true;
        }

        if (options.ProviderKind == AIProviderKind.AzureOpenAI && !options.UseMockProvider)
        {
            options.AzureOpenAI.Enabled = true;
        }
    }
}
