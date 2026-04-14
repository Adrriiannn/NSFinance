using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIIntegrationOptionsValidator : IValidateOptions<AIIntegrationOptions>
{
    private static readonly HashSet<string> LegacyRoutingValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "merchant-investigation",
        "gpt-4-1-chat"
    };

    public ValidateOptionsResult Validate(string? name, AIIntegrationOptions options)
    {
        var errors = new List<string>();

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (!string.IsNullOrWhiteSpace(options.Models.Fast)
            && !string.IsNullOrWhiteSpace(options.Routing.FastModelName)
            && !string.Equals(options.Models.Fast.Trim(), options.Routing.FastModelName.Trim(), StringComparison.Ordinal))
        {
            errors.Add("AI configuration conflict: Models.Fast does not match Routing.FastModelName.");
        }

        if (!string.IsNullOrWhiteSpace(options.Models.Heavy)
            && !string.IsNullOrWhiteSpace(options.Routing.HeavyModelName)
            && !string.Equals(options.Models.Heavy.Trim(), options.Routing.HeavyModelName.Trim(), StringComparison.Ordinal))
        {
            errors.Add("AI configuration conflict: Models.Heavy does not match Routing.HeavyModelName.");
        }

        var provider = options.UseMockProvider ? AIProviderKind.Mock : options.ProviderKind;
        if (provider != AIProviderKind.AzureOpenAI)
        {
            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors);
        }

        if (string.IsNullOrWhiteSpace(options.Routing.FastModelName))
        {
            errors.Add("AI Routing misconfiguration: Routing:FastModelName is required for AzureOpenAI provider.");
        }

        if (string.IsNullOrWhiteSpace(options.Routing.FastDeploymentName))
        {
            errors.Add("AI Routing misconfiguration: Routing:FastDeploymentName is required for AzureOpenAI provider.");
        }

        if (!options.Routing.HeavyModelEnabled)
        {
            errors.Add("AI Routing misconfiguration: Routing:HeavyModelEnabled must be true for AzureOpenAI provider.");
        }

        if (string.IsNullOrWhiteSpace(options.Routing.HeavyModelName))
        {
            errors.Add("AI Routing misconfiguration: Routing:HeavyModelName is required for AzureOpenAI provider.");
        }

        if (string.IsNullOrWhiteSpace(options.Routing.HeavyDeploymentName))
        {
            errors.Add("AI Routing misconfiguration: Routing:HeavyDeploymentName is required for AzureOpenAI provider.");
        }

        if (IsLegacyRoutingValue(options.Routing.FastModelName))
        {
            errors.Add($"AI Routing misconfiguration: Routing:FastModelName uses legacy value '{options.Routing.FastModelName}'. Configure gpt-4.1.");
        }

        if (IsLegacyRoutingValue(options.Routing.FastDeploymentName))
        {
            errors.Add($"AI Routing misconfiguration: Routing:FastDeploymentName uses legacy value '{options.Routing.FastDeploymentName}'. Configure gpt-4.1.");
        }

        if (IsLegacyRoutingValue(options.Routing.HeavyModelName))
        {
            errors.Add($"AI Routing misconfiguration: Routing:HeavyModelName uses legacy value '{options.Routing.HeavyModelName}'. Configure gpt-5-chat.");
        }

        if (IsLegacyRoutingValue(options.Routing.HeavyDeploymentName))
        {
            errors.Add($"AI Routing misconfiguration: Routing:HeavyDeploymentName uses legacy value '{options.Routing.HeavyDeploymentName}'. Configure gpt-5-chat.");
        }

        if (string.IsNullOrWhiteSpace(options.AzureOpenAI.Endpoint))
        {
            errors.Add("AI configuration missing: Endpoint (AI:Endpoint or AI:AzureOpenAI:Endpoint).");
        }

        if (!options.AzureOpenAI.UseManagedIdentity && string.IsNullOrWhiteSpace(options.AzureOpenAI.ApiKey))
        {
            errors.Add("AI configuration missing: ApiKey (AI:ApiKey or AI:AzureOpenAI:ApiKey).");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsLegacyRoutingValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && LegacyRoutingValues.Contains(value);
    }
}
