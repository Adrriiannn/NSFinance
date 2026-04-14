using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIIntegrationOptionsValidator : IValidateOptions<AIIntegrationOptions>
{
    public ValidateOptionsResult Validate(string? name, AIIntegrationOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (!string.IsNullOrWhiteSpace(options.Models.Heavy)
            && !string.IsNullOrWhiteSpace(options.Routing.HeavyModelName)
            && !string.Equals(options.Models.Heavy.Trim(), options.Routing.HeavyModelName.Trim(), StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail("AI configuration conflict: Models.Heavy does not match Routing.HeavyModelName.");
        }

        var provider = options.UseMockProvider ? AIProviderKind.Mock : options.ProviderKind;
        if (provider != AIProviderKind.AzureOpenAI)
        {
            return ValidateOptionsResult.Success;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.AzureOpenAI.Endpoint))
        {
            missing.Add("Endpoint");
        }

        if (!options.AzureOpenAI.UseManagedIdentity && string.IsNullOrWhiteSpace(options.AzureOpenAI.ApiKey))
        {
            missing.Add("ApiKey");
        }

        if (string.IsNullOrWhiteSpace(options.Models.Heavy))
        {
            missing.Add("Models.Heavy");
        }

        if (missing.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail($"AI configuration missing: {string.Join(" / ", missing)}");
    }
}
