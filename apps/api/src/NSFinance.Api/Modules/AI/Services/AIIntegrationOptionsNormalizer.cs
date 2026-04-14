namespace NSFinance.Api.Modules.AI.Services;

public static class AIIntegrationOptionsNormalizer
{
    public static void Normalize(AIIntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Models ??= new AIModelNameOptions();
        options.Routing ??= new AIModelRoutingOptions();
        options.AzureOpenAI ??= new AzureOpenAIOptions();

        var providerToken = NormalizeNullable(options.Provider);
        options.Provider = providerToken;
        if (!string.IsNullOrWhiteSpace(providerToken)
            && Enum.TryParse<AIProviderKind>(providerToken, true, out var parsedProvider))
        {
            options.ProviderKind = parsedProvider;
            options.UseMockProvider = parsedProvider == AIProviderKind.Mock;
        }

        options.Endpoint = NormalizeNullable(options.Endpoint);
        options.ApiKey = NormalizeNullable(options.ApiKey);
        options.AzureOpenAI.Endpoint = NormalizeNullable(options.AzureOpenAI.Endpoint);
        options.AzureOpenAI.ApiKey = NormalizeNullable(options.AzureOpenAI.ApiKey);
        options.Models.Fast = NormalizeNullable(options.Models.Fast);
        options.Models.Heavy = NormalizeNullable(options.Models.Heavy);
        options.Routing.FastModelName = NormalizeOrEmpty(options.Routing.FastModelName);
        options.Routing.FastDeploymentName = NormalizeOrEmpty(options.Routing.FastDeploymentName);
        options.Routing.HeavyModelName = NormalizeOrEmpty(options.Routing.HeavyModelName);
        options.Routing.HeavyDeploymentName = NormalizeOrEmpty(options.Routing.HeavyDeploymentName);
        var fastAliasProvided = !string.IsNullOrWhiteSpace(options.Models.Fast);
        var heavyAliasProvided = !string.IsNullOrWhiteSpace(options.Models.Heavy);

        var aliasNormalizationApplied = false;

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            options.AzureOpenAI.Endpoint = options.Endpoint;
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.AzureOpenAI.ApiKey = options.ApiKey;
        }

        // Alias model names (AI:Models:*) can seed routing values when explicit routing keys are absent.
        if (fastAliasProvided)
        {
            if (string.IsNullOrWhiteSpace(options.Routing.FastModelName))
            {
                options.Routing.FastModelName = options.Models.Fast!;
                aliasNormalizationApplied = true;
            }

            if (string.IsNullOrWhiteSpace(options.Routing.FastDeploymentName))
            {
                options.Routing.FastDeploymentName = options.Models.Fast!;
                aliasNormalizationApplied = true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.Routing.FastModelName))
        {
            options.Models.Fast = options.Routing.FastModelName;
        }

        if (string.IsNullOrWhiteSpace(options.Models.Heavy)
            && !string.IsNullOrWhiteSpace(options.Routing.HeavyModelName))
        {
            options.Models.Heavy = options.Routing.HeavyModelName;
        }

        if (heavyAliasProvided && !string.IsNullOrWhiteSpace(options.Models.Heavy))
        {
            if (string.IsNullOrWhiteSpace(options.Routing.HeavyModelName))
            {
                options.Routing.HeavyModelName = options.Models.Heavy!;
                aliasNormalizationApplied = true;
            }

            if (string.IsNullOrWhiteSpace(options.Routing.HeavyDeploymentName))
            {
                options.Routing.HeavyDeploymentName = options.Models.Heavy!;
                aliasNormalizationApplied = true;
            }

            options.Routing.HeavyModelEnabled = true;
        }

        if (options.ProviderKind == AIProviderKind.AzureOpenAI && !options.UseMockProvider)
        {
            options.AzureOpenAI.Enabled = true;
        }

        options.AliasNormalizationApplied = aliasNormalizationApplied;
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string NormalizeOrEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
